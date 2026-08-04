using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Controls;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Views;

/// <summary>
/// The 应用 page: the search header, the update notice, the categories and the three non-menu states.
/// All state lives in <see cref="LauncherViewModel"/> (still exposed as <see cref="ViewModel"/>, which
/// the shell reads for the launcher's commands and for the <c>PendingFocus</c> hand-off); this class is
/// the thin view layer that wires clicks and keys to it and owns the two things that genuinely need
/// this page's own visual tree: geometric keyboard navigation across the tiles, and the search box.
/// <para>
/// Round 7, Task 1 brought the search box back here from MainWindow's title strip, where Rounds 2-6 had
/// it. The Aero conversion deletes that strip, and Microsoft's window-frame guidance rules on the
/// relocation directly: "Don't add controls to a window frame. Put the controls within the window
/// instead." What did NOT come back with it is the ⋯ overflow button — its commands have been rail rows
/// since Round 3, Task 2 and a settings card since Round 5, item 2.
/// </para>
/// </summary>
public partial class AppPage : UserControl, IDisposable
{
    private readonly LauncherViewModel _viewModel;
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

        _host = App.Services.GetRequiredService<IWindowHost>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _viewModel = LauncherViewModel.Create(App.Services);
        DataContext = _viewModel;

        // Taskbar recall is the primary product workflow. Check after EVERY reveal (startup, tray,
        // hotkey, or second-instance activation) in addition to once here, since the state can change
        // behind our back (the user may pin from Explorer between reveals).
        _events.LauncherShown += OnLauncherShown;
        _ = RefreshPinNoticeAsync();

        // MainWindow re-fits the window to content whenever either docked notice's visibility might
        // have changed (see NoticeVisibilityChanged's own remarks) — both PinNotice.IsOpen and
        // ShowUpdateNotice can flip well after the launcher's own initial fit already ran, since each
        // is resolved by an async call (a COM query here, an HTTP check for the update banner) that can
        // easily outlast it.
        PinNotice.PropertyChanged += OnPinNoticePropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChangedForNotice;
    }

    /// <summary>The one <see cref="LauncherViewModel"/> instance for the launcher's whole life. Exposed
    /// so the shell can reach it directly: MainWindow's three rail command rows
    /// (打开主目录/刷新/退出) dispatch through it, and its <c>PendingFocus</c> flag is the channel every
    /// "focus the search box" request travels on. There is deliberately only ever one instance.</summary>
    public LauncherViewModel ViewModel => _viewModel;

    /// <summary>Raised whenever the taskbar-pin InfoBar or the update banner might have opened or
    /// closed. MainWindow re-runs <c>WindowHost.FitToContent</c> from this: a notice that appears AFTER
    /// the launcher's initial fit steals height from <c>Body</c> (whose vertical scrolling is disabled),
    /// silently re-wrapping a category that used to render in a single column and putting back the
    /// horizontal scrollbar the fit exists to avoid. <c>FitToContent</c>'s own guards (maximized, already
    /// manually resized this process) decide whether the re-fit this triggers actually does anything.
    /// </summary>
    public event Action? NoticeVisibilityChanged;

    private void OnPinNoticePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == InfoBar.IsOpenProperty)
            NoticeVisibilityChanged?.Invoke();
    }

    private void OnViewModelPropertyChangedForNotice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.ShowUpdateNotice))
            NoticeVisibilityChanged?.Invoke();
    }

    /// <summary>Kick off the menu build and return the SAME memoized Task every caller of
    /// <c>LauncherViewModel.InitializeAsync</c> shares. Deliberately NOT done in the constructor: this
    /// control is constructed while the shell is still being built — MainWindow's own field initializer
    /// runs before <c>InitializeComponent</c>, before <c>Show()</c> — and the launcher's startup contract
    /// is that the build begins strictly after the window is shown. Called from <c>MainWindow.OnOpened</c>
    /// on every <c>Show()</c> (cold start and every later un-hide alike — memoization makes every call
    /// after the first an instant no-op), in the same position the pre-split window called
    /// <c>LauncherViewModel.InitializeAsync</c> from. Returning the Task (rather than the old
    /// fire-and-forget <c>void</c>) is what lets <c>MainWindow</c> await it before fitting the window to
    /// whatever it built — see <c>MainWindow.InitializeAndFitAsync</c>.</summary>
    public Task BeginInitialize() => _viewModel.InitializeAsync();

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
        PinNotice.PropertyChanged -= OnPinNoticePropertyChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChangedForNotice;
        _viewModel.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Recovery actions (empty state + overflow menu)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // The empty state's "打开设置…" recovery button — not the overflow menu's old 选项 entry, which is
    // gone now that the rail's 设置 entry is the way to reach settings. Goes through
    // LauncherViewModel.OpenSettings -> ISettingsLauncher, which (since Task 13) navigates to the 设置
    // page and brings the window forward rather than opening a separate window.
    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => _viewModel.OpenSettings();

    // The empty state's 刷新 recovery button. The rail's own 刷新 row is a separate handler in
    // MainWindow's code-behind that reaches the same view model through AppPage.ViewModel.
    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _viewModel.RefreshAsync();

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
            PinNotice.CloseButtonAutomationName = AppLanguage.Text("Dismiss", lang);
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

    /// <summary>不再提示 is an immediate config write, and since Round 8, Task 1 it is the ONLY caller
    /// of <see cref="SettingsWrite.CommitAsync"/> left — the settings card groups moved to a delayed
    /// commit and this did not, because dismissing a notice is a launcher-page ACTION with no 确定 to
    /// wait for and no 取消 to take it back. The contract is unchanged: a failed save rolls the flag
    /// back, re-evaluates the notice from the restored config, and reports the error. A bare
    /// <c>SaveAsync()</c> here would dismiss the bar on screen while the flag never reached disk, and
    /// the InfoBar would silently return on the next launch.</summary>
    private async void OnPinNeverClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = App.Services.GetRequiredService<ConfigService>();
            var previous = cfg.Config.General.TaskbarPinPromptDismissed;
            cfg.Config.General.TaskbarPinPromptDismissed = true;
            PinNotice.IsOpen = false; // immediate feedback; the reload below puts it back on failure

            await SettingsWrite.CommitAsync(cfg, _events, _host, this,
                rollback: () => cfg.Config.General.TaskbarPinPromptDismissed = previous,
                reload: () => _ = RefreshPinNoticeAsync());
        }
        catch (Exception ex)
        {
            // Avalonia has no global unhandled-exception hook for an escaping async void, so this is
            // the floor. CommitAsync already handles the save failure itself.
            Serilog.Log.Error(ex, "Failed to persist the taskbar-pin dismissal");
        }
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
    //
    // Round 7, Task 1 brought the control back into this page from MainWindow's title strip, which
    // the Aero caption deletes (win-window-frames forbids controls in a window frame). Three members
    // moved with it and moved unchanged: OnSearchFocusChanged, OnSearchKeyDown and FocusSearchBox,
    // the entry point MainWindow's PendingFocus consumer calls.
    //
    // ROUND 7, TASK 9 VOIDED BOTH HALVES OF "THREE, UNCHANGED", so the sentence above is history and
    // not a description of what is here. OnSearchFocusChanged now also calls SearchBox.SelectAll()
    // when the box TAKES focus — published behaviour for a search box, cited in that member's own
    // remarks — and OnSearchClearPressed is a fourth member, the handler for the right-end "x" the
    // same task added. OnClearFilterClick is a fifth and belongs to the empty state rather than to
    // the field; it lives here because it makes the identical ClearFilter + PendingFocus pair.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Same "please focus the search box" request a fresh summon makes
    /// (<c>LauncherViewModel.HandleLauncherShownAsync</c> sets <c>PendingFocus</c>). It still goes
    /// through the flag rather than calling <see cref="FocusSearchBox"/> directly: MainWindow is the
    /// one place that knows whether this page is the one on screen, and routing every request through
    /// it keeps a request that arrives on 设置 deferred instead of lost.</summary>
    private void OnClearFilterClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearFilter();
        _viewModel.PendingFocus = true;
    }

    /// <summary>Focus the search box. Called by <c>MainWindow.ConsumePendingFocus</c>, which owns the
    /// decision of WHEN (see its own remarks — it defers while 设置 is showing). Swallowing is
    /// deliberate and is carried over verbatim from the pre-move version: focus is a nicety and must
    /// never take startup down.</summary>
    public void FocusSearchBox()
    {
        try
        {
            SearchBox.Focus();
        }
        catch
        {
            /* focus is a nicety; never let it take startup down */
        }
    }

    /// <summary>Focus is drawn on the FIELD (the bordered box), not on the bare TextBox inside it, so
    /// the outline frames the glyph too. The TextBox has no border of its own to light up — it is given
    /// a local <c>BorderThickness="0"</c>, so Aero's own focus brush pair is applied to the field's
    /// Border by <c>Border.tm-searchfield.focused</c> instead.</summary>
    /// <remarks>Round 7, Task 9 added the <c>SelectAll</c>. PUBLISHED,
    /// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-search-boxes</c>: "On input
    /// focus, automatically select any previously entered text." It is guarded on the box actually
    /// being focused because this one handler serves both GotFocus and LostFocus.</remarks>
    private void OnSearchFocusChanged(object? sender, RoutedEventArgs e)
    {
        var focused = SearchBox.IsFocused;
        SearchField.Classes.Set("focused", focused);
        if (focused)
            SearchBox.SelectAll();
    }

    /// <summary>The right-end "x". PUBLISHED,
    /// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-search-boxes</c>: "a clear
    /// button appears after the user starts typing … On hover, the 'x' gets a button look and can be
    /// clicked." Clearing and then re-focusing is the same pair the empty state's 清除筛选 button does,
    /// through the same <c>PendingFocus</c> channel, so there is one path for every "focus the box"
    /// request and MainWindow still owns the timing.</summary>
    /// <remarks>Primary button only, for the same reason <see cref="OnGroupTitlePressed"/> guards:
    /// <c>PointerPressed</c> fires for every button, and a right-click here would otherwise wipe the
    /// filter. The affordance is a Border rather than a Button because every Button theme in this app
    /// carries a height floor that would hold the 23 DIP field open — see the
    /// <c>Border.tm-search-affordance</c> rule.</remarks>
    private void OnSearchClearPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _viewModel.ClearFilter();
        _viewModel.PendingFocus = true;
        e.Handled = true;
    }

    /// <summary>Enter launches the first match, Escape clears then hides, Down enters the tile
    /// grid.</summary>
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
            // Off the top of the grid: back to where typing goes. Routed through PendingFocus rather
            // than calling FocusSearchBox() directly, for the same reason OnClearFilterClick is — one
            // path for every "focus the box" request, and MainWindow owns the timing.
            _viewModel.PendingFocus = true;
            e.Handled = true;
        }
    }

    private static bool IsTile(Control c) => c.Classes.Contains("tm-tile");

    /// <summary>Focus the first rendered tile. Called from <see cref="OnSearchKeyDown"/>'s Down case.
    /// Public because it was MainWindow's entry point into this grid for the four rounds the search box
    /// spent in the title strip; kept public rather than narrowed, since nothing about walking this
    /// page's own tiles is private to the keyboard handler.</summary>
    public bool FocusFirstTile()
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
}
