using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 行为 card group: hide-on-blur, always-on-top, start with Windows, pin to taskbar, and the global
/// hotkey. Ported from <c>SettingsWindow</c>'s 行为 tab, minus the staging: every handler here writes
/// straight to <see cref="ConfigService.Config"/> (or, for autostart, straight to the registry /
/// StartupTask — that setting has no field in <c>AppConfig</c> at all; <see cref="IAutoStartService"/>
/// reads the OS state live), then hands config changes off to <see cref="SettingsWrite.CommitAsync"/>,
/// which raises <see cref="AppEvents.SettingsChanged"/> on success or rolls the field back and reports
/// the error on failure — instead of mutating a working clone for a later Apply.
/// </summary>
public partial class BehaviorCard : UserControl, ISettingsCard
{
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly IWindowHost _host;
    private readonly IAutoStartService _autoStart;
    private readonly TaskbarPinService _taskbarPin;
    private readonly bool _isPortable;

    /// <summary>Guards every change handler against the values ReloadFromConfig assigns while it
    /// repopulates the controls — without it, populating a ToggleSwitch would write the freshly-loaded
    /// value straight back to config as if the user had just flipped it.</summary>
    private bool _loading;

    /// <summary>The last taskbar-pin state this card knows about, re-queried on every
    /// <see cref="OnAttachedToVisualTree"/> (see its remarks) and otherwise only ever re-labelled —
    /// never re-queried — by <see cref="ReloadFromConfig"/>, which the shared SettingsChanged event can
    /// trigger far more often than a real pin-state change (any card's commit, anywhere on the page,
    /// re-fires it).</summary>
    private TaskbarPinState _taskbarPinState = new(TaskbarPinStatus.Available);

    /// <summary>Guards <see cref="RefreshTaskbarPinStateAsync"/> against stacking: a fast detach/
    /// re-attach (e.g. quick nav-rail clicks) could start a second query before an in-flight one's
    /// <c>GetStateAsync</c> COM call returns.</summary>
    private bool _taskbarPinRefreshing;

    private string UiLanguage => _config.Config.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public BehaviorCard()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _host = App.Services.GetRequiredService<IWindowHost>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _taskbarPin = App.Services.GetRequiredService<TaskbarPinService>();
        _isPortable = App.IsPortable;

        // Tunneling, so the read-only TextBox never sees the key first — same reasoning as
        // SettingsWindow's ctor: Avalonia has no Key.System/e.SystemKey indirection, so an Alt combo
        // arrives as the real key plus KeyModifiers.Alt.
        HotkeyBox.AddHandler(InputElement.KeyDownEvent, OnHotkeyKeyDown, RoutingStrategies.Tunnel);

        // The taskbar-pin query itself is kicked off from OnAttachedToVisualTree, not here — a
        // constructor-only query would go stale for the rest of the session (see its remarks).
    }

    /// <summary>Re-read the two rows whose real state lives in the OS, not in <c>AppConfig</c>: the
    /// taskbar pin and 开机自启. Both can be changed from entirely outside this app — right-click the
    /// taskbar icon / Start-menu 固定到任务栏 for the pin, Task Manager → 启动应用 → 禁用 for autostart —
    /// so both are only trustworthy at the moment they were queried. <c>SettingsWindow</c> re-queried on
    /// every <c>Opened</c>; this card is constructed once and kept alive for the app's whole life
    /// (<c>SettingsPage</c> is cached — see <c>MainWindow.ShowPage</c>), so there is no per-visit
    /// "window opened" moment to hook. Navigating between 应用 and 设置 swaps <c>PageHost.Content</c>,
    /// detaching and re-attaching this control each time, so re-query here instead — on every
    /// re-attach, including the first.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshAutoStartState();
        RunSafely(RefreshTaskbarPinStateAsync);
    }

    /// <summary>Push the LIVE OS autostart state onto the toggle. Synchronous because
    /// <see cref="IAutoStartService.IsEnabled"/> is (a registry read, or a blocking WinRT call on the
    /// packaged build), unlike the pin's async COM query.</summary>
    /// <remarks>
    /// The <c>_loading</c> guard is saved and RESTORED rather than simply cleared, because
    /// <see cref="ReloadFromConfig"/> calls this from inside its own guarded block — clearing it would
    /// un-guard the rest of that repopulation and let it write the values it is loading straight back
    /// to config.
    /// </remarks>
    private void RefreshAutoStartState()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            AutoStartToggle.IsEnabled = !_isPortable;
            AutoStartToggle.IsChecked = !_isPortable && _autoStart.IsEnabled();
        }
        catch (Exception ex)
        {
            // A registry/WinRT read can fail (locked hive, policy). Leave the toggle showing whatever
            // it last showed rather than lying with a hard false.
            Serilog.Log.Error(ex, "读取开机自启状态失败");
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>Populate every control from the live config. Sets a guard so the change handlers do not
    /// write back the values they are being given.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            var g = _config.Config.General;
            var lang = g.Language;

            GroupHead.Text = AppLanguage.Text("Behavior", lang);

            AutoCloseTitle.Text = AppLanguage.Text("AutoClose", lang);
            AutoCloseSub.Text = AppLanguage.Text("AutoCloseCardSub", lang);
            AutoCloseToggle.IsChecked = g.AutoClose;

            TopMostTitle.Text = AppLanguage.Text("TopMost", lang);
            TopMostSub.Text = AppLanguage.Text("TopMostCardSub", lang);
            TopMostToggle.IsChecked = g.TopMost;

            AutoStartTitle.Text = AppLanguage.Text("AutoStart", lang);
            AutoStartSub.Text = _isPortable
                ? AppLanguage.Text("AutoStartPortableHelp", lang)
                : AppLanguage.Text("AutoStartCardSub", lang);
            RefreshAutoStartState();

            PinTaskbarTitle.Text = AppLanguage.Text("PinToTaskbar", lang);
            ApplyTaskbarPinState(_taskbarPinState); // re-label the button/status text; never re-query here

            HotkeyEnabledTitle.Text = AppLanguage.Text("EnableHotkey", lang);
            HotkeyEnabledSub.Text = AppLanguage.Text("HotkeyEnabledCardSub", lang);
            HotkeyEnabledToggle.IsChecked = g.GlobalHotkeyEnabled;

            HotkeyTitle.Text = AppLanguage.Text("Hotkey", lang);
            HotkeyHelpText.Text = AppLanguage.Text("HotkeyHelp", lang);
            ToolTip.SetTip(HotkeyBox, AppLanguage.Text("HotkeyTooltip", lang));
            HotkeyClearBtn.Content = AppLanguage.Text("Clear", lang);
            HotkeyBox.Text = string.IsNullOrWhiteSpace(g.GlobalHotkey) ? AppLanguage.Text("None", lang) : g.GlobalHotkey;
            UpdateHotkeyEnabledState();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnAutoCloseChanged(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        // WindowHost reads General.AutoClose LIVE on every Deactivated (see WindowHost.Attach), so
        // writing the field is the whole job — no separate "apply" call needed.
        var previous = _config.Config.General.AutoClose;
        _config.Config.General.AutoClose = AutoCloseToggle.IsChecked == true;
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.AutoClose = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>窗口置顶 must apply live. It does, for free: WindowHost.Attach subscribes
    /// <c>ApplyTopmost</c> to <see cref="AppEvents.SettingsChanged"/>, which
    /// <see cref="SettingsWrite.CommitAsync"/> raises on a successful save — so this handler must NOT
    /// also poke <c>IWindowHost.Window.Topmost</c> directly, or a failed save's rollback would leave the
    /// live window topmost with no config value to justify it.</summary>
    private void OnTopMostChanged(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        var previous = _config.Config.General.TopMost;
        _config.Config.General.TopMost = TopMostToggle.IsChecked == true;
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.TopMost = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>开机自启 has no field in <c>AppConfig</c> — the OS (Run key or MSIX StartupTask)
    /// IS the state, so there is no config to save and no <see cref="SettingsWrite.CommitAsync"/> call
    /// here. A failed registry write throws and is logged rather than shown as a save-failed dialog —
    /// the same contract <c>SettingsWindow.CommitAsync</c> had: there, <c>_autoStart.SetEnabled</c> ran
    /// OUTSIDE the try/catch that wrapped <c>_config.SaveAsync</c>, so a thrown exception only ever hit
    /// the window's top-level <c>RunSafely</c> log-and-swallow, never a dialog. The packaged/MSIX
    /// <c>StartupTaskAutoStartService</c> never throws at all — it swallows every exception internally
    /// and never inspects the <c>StartupTaskState</c> its own WinRT call returns, so a silent Windows/
    /// policy refusal leaves <c>SetEnabled</c> looking like it succeeded. Either way, the toggle must
    /// never be left showing the click rather than the truth: re-verify unconditionally, on both the
    /// success and failure paths, against what <see cref="IAutoStartService.IsEnabled"/> actually
    /// reports — not just flipped back on a caught exception.</summary>
    private void OnAutoStartChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading || _isPortable)
            return;

        var desired = AutoStartToggle.IsChecked == true;
        try
        {
            _autoStart.SetEnabled(desired);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "开机自启注册表/计划任务写入失败");
        }

        // Unconditional re-verification (success or failure): this is the only way to catch
        // StartupTaskAutoStartService silently no-op-ing on the packaged build (see above).
        RefreshAutoStartState();
    }

    // No _loading check here (unlike every other handler in this file) — that is deliberate, not an
    // oversight: a Click cannot be re-entered by ReloadFromConfig repopulating a control the way
    // setting a ToggleSwitch/TextBox's value re-enters a Changed handler, so there is nothing to guard
    // against. Do not "fix" this by copying the _loading pattern from elsewhere in this file.
    private void OnPinTaskbarClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        PinTaskbarBtn.IsEnabled = false;
        PinTaskbarStatusText.Text = L("PinToTaskbarWorking");

        if (_taskbarPinState.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            await OpenManualTaskbarPinLocationAsync(_taskbarPinState);
            return;
        }

        var state = await _taskbarPin.RequestPinAsync();
        if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            await OpenManualTaskbarPinLocationAsync(state);
            return;
        }

        // Pinned, or Available again because the user declined the Windows consent dialog.
        ApplyTaskbarPinState(state);
    });

    private async Task OpenManualTaskbarPinLocationAsync(TaskbarPinState state)
    {
        ApplyTaskbarPinState(state); // re-enables the button and sets the state message
        if (_taskbarPin.OpenManualPinLocation())
        {
            PinTaskbarStatusText.Text = L("PinToTaskbarManualOpened");
            return;
        }

        PinTaskbarStatusText.Text = L("PinToTaskbarManualOpenFailed");
        await ShowErrorAsync(L("PinToTaskbarManualOpenFailed"));
    }

    /// <summary>Re-query the OS pin state and push it onto the row. The button is disabled for the
    /// duration, so it is never clickable while this card does not yet know what clicking it would
    /// mean.</summary>
    /// <remarks>
    /// The inner try/catch is not decoration. This method's caller is
    /// <see cref="RunSafely"/>, which LOGS AND SWALLOWS — so without it, any fault between the
    /// <c>IsEnabled = false</c> above and <see cref="ApplyTaskbarPinState"/> below would leave the row
    /// permanently dead for the rest of the session: a disabled button whose label and status line
    /// still describe the pre-query guess, and no way for the user to reach the manual fallback. That
    /// is not reachable through <see cref="TaskbarPinService.GetStateAsync"/>'s OWN body today — it
    /// wraps everything, including the awaited WinRT call, in its own catch and returns
    /// <see cref="TaskbarPinStatus.Unsupported"/> rather than throwing (verified by reading it, not
    /// assumed) — but "the query cannot fail" is a property of a collaborator this class does not own,
    /// and the cost of not depending on it is four lines. A caught fault is rendered as
    /// <see cref="TaskbarPinStatus.Failed"/>, which <see cref="ApplyTaskbarPinState"/> already renders
    /// honestly: neutral theme, re-enabled, "open the Start-menu entry yourself" content, and the
    /// error in the status line.
    /// </remarks>
    private async Task RefreshTaskbarPinStateAsync()
    {
        if (_taskbarPinRefreshing)
            return; // a query from a very-close attach/re-attach is already in flight

        _taskbarPinRefreshing = true;
        try
        {
            PinTaskbarBtn.IsEnabled = false; // dead while the async query runs
            TaskbarPinState state;
            try
            {
                state = await _taskbarPin.GetStateAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "查询固定到任务栏状态失败");
                state = new TaskbarPinState(TaskbarPinStatus.Failed, ex.Message);
            }

            // TaskbarPinService.GetStateAsync itself only logs on exception, so this is the one place
            // that proves a re-query actually ran on this visit rather than reusing the cached state —
            // load-bearing for verifying the fix for the "pin status goes stale" defect, not just a
            // debugging nicety.
            Serilog.Log.Information("固定到任务栏状态已重新查询: {Status}", state.Status);

            if (!this.IsAttachedToVisualTree())
                return; // detached while the query was in flight; the next re-attach queries again

            ApplyTaskbarPinState(state);
        }
        finally
        {
            _taskbarPinRefreshing = false;
        }
    }

    /// <summary>Round 3, Task 3 (user item 8): once pinned, this row's button must read as a completed,
    /// disabled action rather than a still-live call to action. <c>IsEnabled</c> already tracked
    /// <see cref="TaskbarPinStatus.Pinned"/> correctly before this change; what was wrong is that the
    /// button kept <c>TmAccentButton</c>'s Theme regardless, and TmAccentButton's own
    /// <c>^:disabled /template/ Border#Root</c> rule (inherited, via BasedOn, from TmButton — see
    /// Launcher.axaml) only dims the whole Root to 60% opacity; it never repaints Background away from
    /// AccentDefaultBrush, so a disabled-and-pinned button still rendered as a muted BLUE swatch —
    /// visually still an affordance, just quieter, exactly the "accent-styled" look the task flagged.
    /// Switching Theme to the plain, neutral TmButton once pinned is what actually reaches a proper
    /// Windows 11 disabled-control look (dimmed neutral surface, dimmed text, no accent colour at all).
    /// This runs from the SAME single call site IsEnabled/Content already did (both branches of
    /// ApplyTaskbarPinState's two callers — the live OS re-query in
    /// <see cref="RefreshTaskbarPinStateAsync"/> on every <see cref="OnAttachedToVisualTree"/>, and
    /// ReloadFromConfig's re-label of the cached <see cref="_taskbarPinState"/>) — so the Theme always
    /// follows the same live-queried state IsEnabled does, never a value captured once at construction.</summary>
    private void ApplyTaskbarPinState(TaskbarPinState state)
    {
        _taskbarPinState = state;
        var isPinned = state.Status is TaskbarPinStatus.Pinned;
        PinTaskbarBtn.IsEnabled = !isPinned;

        // Round 3 final-fix report, finding 1: this runs from ReloadFromConfig too, which
        // SettingsPage's constructor calls BEFORE MainWindow.ShowPageCore ever assigns the freshly
        // built page to PageHost.Content — i.e. while this whole card tree is still detached from the
        // logical tree. Decompiled Avalonia 12.1.0: StyledElement.StylingParent walks
        // InheritanceParent, which a detached control terminates before ever reaching
        // Application.Styles (where TmButton/TmAccentButton both live), so FindResource returns
        // AvaloniaProperty.UnsetValue and the `as ControlTheme` used to yield a hard null — clobbering
        // whatever theme this button already had. On the very first call there is nothing to clobber
        // yet, but on every LATER one there is: OnAttachedToVisualTree already applied the correct
        // theme on a previous visit, and AppEvents.SettingsChanged (which this method's other caller,
        // ReloadFromConfig, is wired to) can fire while 设置 is not even the page on screen — e.g. a
        // hotkey saved from a different card, on an entirely different visit. A failed lookup must
        // therefore leave whatever theme is already applied alone rather than null it out; the next
        // real re-attach (see OnAttachedToVisualTree's own remarks) always re-queries and re-applies
        // correctly once the tree is live again.
        //
        // Round 3 residual review: that guard alone still left the FIRST call of a process rendering
        // wrong, because "nothing has been assigned yet" and "a lookup failed, so nothing was
        // assigned" were the same pixels — stock Fluent — for a Button that carried no Theme and no
        // Classes in XAML at all. BehaviorCard.axaml now gives this button a static default Theme of
        // the NEUTRAL TmButton (see that element's own comment for why neutral and not accent), so a
        // failed lookup here lands on a deliberate, defensible look rather than on Avalonia's default
        // one, and this assignment is a correction of a known-good starting point rather than the only
        // thing standing between the button and stock Fluent.
        if (this.FindResource(isPinned ? "TmButton" : "TmAccentButton") is ControlTheme theme)
            PinTaskbarBtn.Theme = theme;

        PinTaskbarBtn.Content = isPinned
            ? L("PinnedToTaskbar")
            : state.Status is TaskbarPinStatus.Available
                ? L("PinToTaskbar")
                : L("PinToTaskbarManual");
        PinTaskbarStatusText.Text = state.Status switch
        {
            TaskbarPinStatus.Pinned => L("PinnedToTaskbarHelp"),
            TaskbarPinStatus.NotAllowed => L("PinToTaskbarNotAllowed"),
            TaskbarPinStatus.Unsupported => L("PinToTaskbarUnsupported"),
            TaskbarPinStatus.Failed => L("PinToTaskbarFailed", state.Error ?? L("UnknownError")),
            _ => L("PinToTaskbarHelp"),
        };
    }

    // 清除 stays enabled even when the feature is off — that is deliberate (ported verbatim from
    // SettingsWindow: clearing a hotkey while the toggle is off is still a meaningful action).
    private void UpdateHotkeyEnabledState() => HotkeyBox.IsEnabled = HotkeyEnabledToggle.IsChecked == true;

    private void OnHotkeyEnabledChanged(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        var previous = _config.Config.General.GlobalHotkeyEnabled;
        _config.Config.General.GlobalHotkeyEnabled = HotkeyEnabledToggle.IsChecked == true;
        UpdateHotkeyEnabledState(); // immediate UX feedback; ReloadFromConfig repeats this either way
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.GlobalHotkeyEnabled = previous,
            reload: ReloadFromConfig);
    });

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        e.Handled = true; // capture the combo; never type into the read-only box
        var combo = HotkeyParser.FromKeyEvent(e.Key, e.KeyModifiers);
        if (combo == null)
            return; // still composing (modifier-only) or no modifier held — keep waiting

        var previous = _config.Config.General.GlobalHotkey;
        if (combo == previous)
            return; // no actual change — nothing to save

        _config.Config.General.GlobalHotkey = combo;
        HotkeyBox.Text = combo; // immediate feedback before the save round-trip
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.GlobalHotkey = previous,
            reload: ReloadFromConfig);
    });

    private void OnHotkeyClearClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        var previous = _config.Config.General.GlobalHotkey;
        if (string.IsNullOrEmpty(previous))
            return; // already clear — nothing to save

        _config.Config.General.GlobalHotkey = "";
        HotkeyBox.Text = AppLanguage.Text("None", UiLanguage); // immediate feedback
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.GlobalHotkey = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>Same shape as <see cref="SettingsWrite.CommitAsync"/>'s own error dialog: suppress
    /// hide-on-blur while the modal has focus, so a user with 失焦自动隐藏 on does not lose the window
    /// out from under the dialog.</summary>
    private async Task ShowErrorAsync(string message)
    {
        _host.SuppressHide(true);
        try
        {
            await MessageBox.ShowAsync(TopLevel.GetTopLevel(this) as Window, message, "TanMenu",
                MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Run an async change handler without letting a fault escape the event — same rationale as
    /// <c>SettingsWindow.RunSafely</c> / <c>AppearanceCard.RunSafely</c>: Avalonia has no WPF-style
    /// global DispatcherUnhandledException hook, so an escaping async-void fault would tear the process
    /// down, and logging is the floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "行为设置写入失败"); }
    }
}
