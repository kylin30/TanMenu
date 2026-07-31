using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf.Views;

/// <summary>
/// The 应用 page. All state lives in <see cref="LauncherViewModel"/>; this is the thin view layer that
/// wires clicks and keys to it and owns the two things that genuinely need the visual tree — deferred
/// search focus and geometric keyboard navigation across the tiles.
/// </summary>
public partial class AppPage : UserControl, IDisposable
{
    private readonly LauncherViewModel _viewModel;

    // Not yet consumed anywhere in THIS task — the brief's own constructor snippet wires it up
    // verbatim ahead of the tasks that will actually call it (the settings entry points Task 8+ add).
    private readonly IShellNavigator _navigator;

    private readonly IWindowHost _host;
    private readonly AppEvents _events;

    // Guards RefreshPinNoticeAsync against stacking: the constructor's own call and the LauncherShown
    // handler both fire within milliseconds of each other on a cold start (AppPage is built as a
    // MainWindow field initializer, and AppEvents.RaiseLauncherShown follows moments later from
    // OnLauncherOpened), and a rapid hotkey/tray toggle can raise LauncherShown again before an
    // in-flight refresh's GetStateAsync COM call returns.
    private bool _pinNoticeRefreshing;

    // Set once, from Dispose (real application exit only — see Dispose's own remarks). Checked after
    // the one await in RefreshPinNoticeAsync so a refresh that was already in flight cannot touch the
    // control tree once the page is torn down.
    private bool _disposed;

    public AppPage()
    {
        InitializeComponent();

        _navigator = App.Services.GetRequiredService<IShellNavigator>();
        _host = App.Services.GetRequiredService<IWindowHost>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _viewModel = LauncherViewModel.Create(App.Services);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        // Taskbar recall is the primary product workflow. Check after EVERY reveal (startup, tray,
        // hotkey, or second-instance activation) in addition to once here, since the state can change
        // behind our back (the user may pin from Explorer between reveals).
        _events.LauncherShown += OnLauncherShown;
        _ = RefreshPinNoticeAsync();
    }

    /// <summary>Kick off the menu build. Deliberately NOT done in the constructor: this control is
    /// constructed while the shell is still being built — MainWindow's own field initializer runs
    /// before <c>InitializeComponent</c>, before <c>Show()</c> — and the launcher's startup contract
    /// is that the build begins strictly after the window is shown. Called once, from
    /// <c>MainWindow.OnOpened</c>, in the same position the pre-split window called
    /// <c>LauncherViewModel.InitializeAsync</c> from.</summary>
    public void BeginInitialize() => _ = _viewModel.InitializeAsync();

    /// <summary>Escape from anywhere in the shell that this control's own <see cref="OnKeyDown"/> never
    /// sees — currently, a focused nav-rail row — still needs the exact clear-then-hide semantic the
    /// search box's Escape uses, since the pre-split window applied that same logic regardless of
    /// where focus was. Called from <c>MainWindow.OnKeyDown</c>'s fallback.</summary>
    public void HandleGlobalEscape() => _viewModel.HandleSearchEscape();

    /// <summary>Dispose the owned view model. Called exactly once, from <c>MainWindow.OnClosed</c> —
    /// the only path that fires at real application exit, since <c>MainWindow.OnClosing</c> hides to
    /// the tray otherwise. This page is a field <see cref="MainWindow"/> keeps alive for the whole
    /// session (<c>ShowPage</c> never rebuilds it), so disposal must NOT be tied to this control's own
    /// Unloaded/DetachedFromVisualTree: switching the rail to 设置 detaches it from the page host
    /// without ending the launcher's lifetime, and disposing here would break it for the next switch
    /// back to 应用.</summary>
    public void Dispose()
    {
        _disposed = true;
        _events.LauncherShown -= OnLauncherShown;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
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

    // The empty state's "打开选项…" recovery button — not the overflow menu's old 选项 entry, which
    // is gone now that the rail's 设置 entry is the way to reach settings. This one still opens the
    // legacy SettingsWindow via ISettingsLauncher (LauncherViewModel.OpenSettings); rewiring it to
    // _navigator is Task 8's job, once the rail's Settings page is real rather than a placeholder.
    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => _viewModel.OpenSettings();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _viewModel.RefreshAsync();

    private void OnAboutClick(object? sender, RoutedEventArgs e) => _viewModel.OpenAbout();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _viewModel.ExitApp();

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Taskbar-pin notice
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnLauncherShown() => _ = RefreshPinNoticeAsync();

    /// <summary>Evaluate the taskbar-pin notice. Runs on every reveal (the state can change behind our
    /// back — the user may pin from Explorer) but never blocks it.</summary>
    private async Task RefreshPinNoticeAsync()
    {
        if (_pinNoticeRefreshing)
            return; // a refresh from this or a very-close reveal is already in flight

        _pinNoticeRefreshing = true;
        try
        {
            var cfg = App.Services.GetRequiredService<ConfigService>();
            if (cfg.Config.General.TaskbarPinPromptDismissed)
            {
                PinNotice.IsOpen = false;
                return;
            }

            var pin = App.Services.GetRequiredService<TaskbarPinService>();
            var state = await pin.GetStateAsync();
            if (_disposed)
                return; // the page was torn down while the query was in flight

            var lang = cfg.Config.General.Language;

            PinNotice.Message = AppLanguage.Text("TaskbarPinRequiredPrompt", lang);
            PinNowButton.Content = AppLanguage.Text("PinNow", lang);
            PinNeverButton.Content = AppLanguage.Text("PinDontAskAgain", lang);
            PinNotice.IsOpen = state.Status != TaskbarPinStatus.Pinned;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to refresh the taskbar pin notice");
        }
        finally
        {
            _pinNoticeRefreshing = false;
        }
    }

    private void OnPinNoticeDismissed(object? sender, EventArgs e) => PinNotice.IsOpen = false;

    private async void OnPinNeverClick(object? sender, RoutedEventArgs e)
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        cfg.Config.General.TaskbarPinPromptDismissed = true;
        await cfg.SaveAsync();
        PinNotice.IsOpen = false;
    }

    private async void OnPinNowClick(object? sender, RoutedEventArgs e)
    {
        var pin = App.Services.GetRequiredService<TaskbarPinService>();
        var cfg = App.Services.GetRequiredService<ConfigService>();
        var lang = cfg.Config.General.Language;

        // Windows' own pin confirmation takes focus. Keep the window alive across it for users who
        // have 失焦自动隐藏 switched on.
        _host.SuppressHide(true);
        try
        {
            var state = await pin.GetStateAsync();
            if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed
                or TaskbarPinStatus.Failed)
            {
                await ShowManualPinLocationAsync(pin, lang);
                return;
            }

            state = await pin.RequestPinAsync();
            if (state.Status is TaskbarPinStatus.Pinned)
            {
                PinNotice.IsOpen = false;
                return;
            }

            if (state.Status is TaskbarPinStatus.Available)
                return; // the user declined the Windows confirmation; leave the notice up

            await ShowManualPinLocationAsync(pin, lang);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Taskbar pin request failed");
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Windows 10 often cannot expose the consent-based TaskbarManager flow to an unpackaged
    /// desktop app. Show the app's Start entry and let the user use Explorer's supported right-click
    /// command. Never mutate the protected taskbar pin store directly.</summary>
    private async Task ShowManualPinLocationAsync(TaskbarPinService pin, string? lang)
    {
        if (pin.OpenManualPinLocation())
            return;

        await MessageBox.ShowAsync(_host.Window, AppLanguage.Text("PinToTaskbarManualOpenFailed", lang),
            "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

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
    /// Arrow keys move by GEOMETRY, not by index. Items now fill columns and categories sit side by
    /// side, so "the tile to the right" can be the next column of the same category OR the first
    /// column of the next one — an index step could not express either. Enter and Space are left
    /// alone: a focused Avalonia Button already handles both.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        // Escape works anywhere on the page, not only in the search box, so a user who tabbed into
        // the grid is never stuck.
        if (e.Key == Key.Escape)
        {
            _viewModel.HandleSearchEscape();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;

        // Unlike Window, a UserControl has no FocusManager of its own (only TopLevel does) — this is
        // the one real behavioural difference the move to a UserControl introduces, so it has to be
        // resolved explicitly rather than copied verbatim from the old Window override.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button current ||
            !IsTile(current))
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
