using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf;

/// <summary>
/// The main launcher window. All state lives in <see cref="LauncherViewModel"/>; this class is the
/// thin view layer: it wires clicks and keys to the view model, and owns the three things that
/// genuinely need the visual tree — deferred search focus, the Mica fallback, and geometric keyboard
/// navigation across the tile grid.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly IWindowHost _host;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        _viewModel = LauncherViewModel.Create(App.Services);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyBackdrop();

        // The host owns show/hide/placement and needs the window once its HWND exists. The DI
        // registration deliberately hands out the same instance for IWindowHost and WindowHost.
        var host = _host as WindowHost;
        host?.Attach(this);
        host?.RestorePlacement();

        _ = _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Mica is a request, not a guarantee: Windows 10, "show transparency" turned off and
    /// remote desktop all refuse it, and the window would then be a hole rather than a surface. Paint
    /// the opaque Fluent surface colour when it was not granted. (Same guard SettingsWindow used.)</summary>
    private void ApplyBackdrop()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
            Background = this.FindResource("SurfaceBaseBrush") as IBrush ?? Brushes.White;
    }

    /// <summary>Closing hides to the tray; the app exits only through 退出 (tray menu or overflow
    /// menu), which calls <see cref="App.ExitApp"/> and shuts the lifetime down explicitly.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            _host.Hide();
            return;
        }

        base.OnClosing(e);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Deferred focus
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LauncherViewModel.PendingFocus))
            return;

        // Loaded priority sits one step below the priority Avalonia runs layout at, so by the time
        // this callback fires the property change has already been measured and arranged — i.e. the
        // search box exists if it is going to.
        Dispatcher.UIThread.Post(ConsumePendingViewWork, DispatcherPriority.Loaded);
    }

    private void ConsumePendingViewWork()
    {
        if (_viewModel.PendingFocus)
        {
            _viewModel.PendingFocus = false;
            try
            {
                SearchBox.Focus();
            }
            catch
            {
                /* focus is a nicety; never let it take startup down */
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Header
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // The header's own hide glyph. Minimize and the system Close button are real OS window
    // operations now (system title bar); this button and the system Close both hide to the tray
    // rather than exiting — see MainWindow.OnClosing.
    private void OnCloseClick(object? sender, RoutedEventArgs e) => _viewModel.HideWindow();

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _viewModel.OpenRoot();

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => _viewModel.OpenSettings();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _viewModel.RefreshAsync();

    private void OnAboutClick(object? sender, RoutedEventArgs e) => _viewModel.OpenAbout();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _viewModel.ExitApp();

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Search box
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Focus is drawn on the FIELD (the bordered box), not on the bare TextBox inside it, so
    /// the accent outline frames the glyph too. The TextBox has no border of its own to light up.</summary>
    private void OnSearchFocusChanged(object? sender, RoutedEventArgs e) =>
        SearchField.Classes.Set("focused", SearchBox.IsFocused);

    private void OnClearFilterClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearFilter();
        SearchBox.Focus();
    }

    /// <summary>Enter launches the first match, Escape clears then hides, Down enters the tile grid.
    /// Everything else is left to the TextBox.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.HandleSearchEnter();
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.HandleSearchEscape();
                e.Handled = true;
                break;
            case Key.Down:
                e.Handled = FocusFirstTile();
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Keyboard navigation across the tile grid
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Arrow keys move by GEOMETRY, not by index. The tiles wrap, so "the tile below" depends on the
    /// rendered row the WrapPanel produced at the current width — an index step of "one row" would be
    /// wrong the moment the window is a different size. Enter and Space are left alone: a focused
    /// Avalonia Button already handles both.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        // Escape works anywhere in the window, not only in the search box, so a user who tabbed into
        // the grid is never stuck.
        if (e.Key == Key.Escape)
        {
            _viewModel.HandleSearchEscape();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;
        if (FocusManager?.GetFocusedElement() is not Button current || !IsTile(current))
            return;

        var next = FindAdjacentTile(current, e.Key);
        if (next is not null)
        {
            next.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            // Off the top of the grid: back to where typing goes.
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private static bool IsTile(Control c) => c.Classes.Contains("tm-tile");

    private bool FocusFirstTile()
    {
        var first = Tiles().FirstOrDefault();
        if (first is null)
            return false;
        first.Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Every focusable tile currently rendered, in document order (section by section).</summary>
    private List<Button> Tiles() =>
        Body.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => IsTile(b) && b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
            .ToList();

    private Button? FindAdjacentTile(Button current, Key direction)
    {
        var origin = CenterOf(current);
        if (origin is not { } from)
            return null;

        Button? best = null;
        var bestScore = double.MaxValue;

        foreach (var candidate in Tiles())
        {
            if (ReferenceEquals(candidate, current))
                continue;
            if (CenterOf(candidate) is not { } to)
                continue;

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            // Same row when the vertical centres are within half a tile of each other. Tiles in one
            // row all share a height, so this is a reliable row test without asking the panel.
            var sameRow = Math.Abs(dy) < current.Bounds.Height / 2;

            double score;
            switch (direction)
            {
                case Key.Right when sameRow && dx > 0: score = dx; break;
                case Key.Left when sameRow && dx < 0: score = -dx; break;
                // Vertical moves prefer the nearest row, then the nearest column within it, so
                // crossing a section boundary lands under the finger rather than at the row start.
                case Key.Down when dy > 0: score = dy * 1000 + Math.Abs(dx); break;
                case Key.Up when dy < 0: score = -dy * 1000 + Math.Abs(dx); break;
                default: continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private Point? CenterOf(Visual v) =>
        v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), Body);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Tiles, sections, update notice, overlays
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MenuItemVm item)
            return;
        _viewModel.PlayClickSound();
        _viewModel.LaunchItem(item);
    }

    private void OnItemPointerEntered(object? sender, PointerEventArgs e)
    {
        // Disabled items make no sound. (A disabled control gets no pointer input anyway; this keeps
        // the rule explicit rather than relying on that.)
        if ((sender as Control)?.DataContext is MenuItemVm { IsDisabled: false })
            _viewModel.PlayHoverSound();
    }

    private void OnGroupTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        // Primary button only. PointerPressed fires for EVERY button, so without this guard a
        // right-click on a section heading opened Explorer.
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (control.DataContext is MenuGroupVm group)
            _viewModel.OpenGroupFolder(group);
    }

    private void OnUpdateNoticeClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.HandleUpdateNoticeAsync();

    private void OnToastPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.DismissToast();
        e.Handled = true;
    }

    private void OnAboutPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CloseAbout();
        e.Handled = true;
    }
}
