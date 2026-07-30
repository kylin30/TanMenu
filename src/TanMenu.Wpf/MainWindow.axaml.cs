using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf;

/// <summary>
/// The launcher popup. All state lives in <see cref="LauncherViewModel"/>; this class is the thin
/// view layer: it wires clicks and keys to the view model, performs the one-shot reveal, and owns the
/// three things that genuinely need the visual tree — deferred search focus, the acrylic fallback,
/// and geometric keyboard navigation across the tile grid.
/// </summary>
public partial class MainWindow : Window
{
    // partial so the source-generated LibraryImport stub below can live here (NativeAOT: no
    // marshalling IL is generated at run time).
    private readonly LauncherViewModel _viewModel;
    private readonly IWindowHost _host;

    // One-shot latch: the reveal happens on the first layout pass in which the REAL content exists
    // (the menu, or the empty state after a failed build) — never on the loading splash.
    private bool _revealed;

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
        ApplyRoundedCorners();

        // The host owns show/hide/size/place and needs the window once its HWND exists. The DI
        // registration deliberately hands out the same instance for IWindowHost and WindowHost.
        (_host as WindowHost)?.Attach(this);

        // The build runs on a background STA worker; the window stays fully transparent until it
        // finishes and the reveal below runs.
        _ = _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Acrylic is a request, not a guarantee: remote desktop, "show transparency" turned off, and
    /// some GPU paths all refuse it, and the window would then be a hole rather than a surface. When
    /// the platform did not grant transparency, paint the opaque Fluent surface colour instead.
    /// </summary>
    private void ApplyBackdrop()
    {
        var granted = ActualTransparencyLevel != WindowTransparencyLevel.None;
        Acrylic.IsVisible = granted;
        OpaqueFallback.IsVisible = !granted;
    }

    /// <summary>
    /// Ask DWM to round the window, which is what makes this a Windows 11 window rather than a
    /// rectangle with rounded paint inside it.
    /// </summary>
    /// <remarks>
    /// Windows 11 rounds ordinary windows automatically, but NOT a borderless one — and this window
    /// has no decorations at all. Without the attribute the drawn 8 px surface would sit inside
    /// square window edges, and the shadow DWM casts would be square too. Windows 10 does not know
    /// the attribute and fails the call harmlessly, which is why the result is ignored.
    /// </remarks>
    private void ApplyRoundedCorners()
    {
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return;

            var preference = DwmWindowCornerPreferenceRound;
            _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll is present on every supported Windows; guard anyway rather than let a
            // decoration concern take the launcher down.
        }
    }

    private const int DwmWindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int DwmWindowCornerPreferenceRound = 2; // DWMWCP_ROUND

    [System.Runtime.InteropServices.LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Reveal + deferred focus
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LauncherViewModel.LoadedOnce)
            or nameof(LauncherViewModel.HasGroups)
            or nameof(LauncherViewModel.PendingFocus)))
            return;

        // Loaded priority sits one step below the priority Avalonia runs layout at, so by the time
        // this callback fires the property change has already been measured and arranged — i.e. the
        // window has its final desired size and the search box exists if it is going to.
        Dispatcher.UIThread.Post(ConsumePendingViewWork, DispatcherPriority.Loaded);
    }

    private void ConsumePendingViewWork()
    {
        if (!_revealed && _viewModel.LoadedOnce)
        {
            _revealed = true;
            // Size + place happen inside, BEFORE the window becomes visually present, so a cold start
            // never flashes an unsized rectangle at screen top-left.
            _host.ShowAndActivate();
        }

        // Unlike the retro layout, the search box is now in the header and therefore always present,
        // so this no longer has to wait for groups to exist.
        if (_viewModel.PendingFocus)
        {
            _viewModel.PendingFocus = false;
            try
            {
                SearchBox.Focus();
            }
            catch
            {
                /* focus is a nicety; never let it take the reveal down */
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Header
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // The launcher is shown/hidden, never closed. There is no minimize any more: on a fixed-size
    // popup it did exactly what close did.
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
