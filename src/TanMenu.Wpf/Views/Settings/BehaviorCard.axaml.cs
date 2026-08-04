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
/// hotkey. Every property handler here writes the shared <see cref="SettingsDraft"/>'s working clone and
/// calls <see cref="SettingsDraft.Revalidate"/>; nothing reaches disk until 确定 / 应用 runs
/// <see cref="SettingsCommit.ApplyAsync"/> (Round 8, Task 1).
/// <para>
/// 开机自启 is the one row with no <c>AppConfig</c> field at all — the OS (Run key, or the MSIX
/// StartupTask) IS the state — so it is buffered by hand in <see cref="SettingsDraft.AutoStartDesired"/>
/// instead of riding the clone. Until Round 8 it was the odd one out in the other direction: it wrote
/// the registry on the click while the four rows around it waited for nothing at all, which is exactly
/// the incoherence delayed commit exists to remove. 固定到任务栏 stays immediate — it is a command, not a
/// property.
/// </para>
/// </summary>
public partial class BehaviorCard : UserControl, ISettingsCard
{
    private readonly SettingsDraft _draft;
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
    /// trigger more often than a real pin-state change (every 确定/应用 re-fires it, and so does a
    /// data-folder change or a 恢复).</summary>
    private TaskbarPinState _taskbarPinState = new(TaskbarPinStatus.Available);

    /// <summary>Guards <see cref="RefreshTaskbarPinStateAsync"/> against stacking: a fast detach/
    /// re-attach (e.g. quick nav-rail clicks) could start a second query before an in-flight one's
    /// <c>GetStateAsync</c> COM call returns.</summary>
    private bool _taskbarPinRefreshing;

    private string UiLanguage => _draft.Working.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public BehaviorCard()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
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
        ApplyAutoStartRow();
        RunSafely(RefreshTaskbarPinStateAsync);
    }

    /// <summary>Push the 开机自启 row's state onto the check box: the PENDING desire if there is one,
    /// and otherwise the LIVE OS state. Synchronous because <see cref="IAutoStartService.IsEnabled"/> is
    /// (a registry read, or a blocking WinRT call on the packaged build), unlike the pin's async COM
    /// query.</summary>
    /// <remarks>
    /// <para>
    /// This is the ONLY reader of <see cref="SettingsDraft.AutoStartDesired"/>, and the null-coalesce is
    /// what makes delayed commit work for a row whose state lives in the OS: until Round 8, Task 1 this
    /// method read <c>_autoStart.IsEnabled()</c> unconditionally, so a buffered desire would have been
    /// clobbered the next time anything called it — and both <see cref="OnAttachedToVisualTree"/> and
    /// <see cref="ReloadFromConfig"/> do, the latter after every commit. The OS is now read only when
    /// nothing is pending, which is what leaves both of those call sites correct untouched.
    /// </para>
    /// <para>
    /// The <c>_loading</c> guard is saved and RESTORED rather than simply cleared, because
    /// <see cref="ReloadFromConfig"/> calls this from inside its own guarded block — clearing it would
    /// un-guard the rest of that repopulation and let it write the values it is loading straight back
    /// to the draft.
    /// </para>
    /// </remarks>
    private void ApplyAutoStartRow()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            AutoStartToggle.IsEnabled = !_isPortable;
            AutoStartToggle.IsChecked = !_isPortable && (_draft.AutoStartDesired ?? _autoStart.IsEnabled());
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

    /// <summary>Populate every control from the draft's working clone. Sets a guard so the change
    /// handlers do not write back the values they are being given.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            var g = _draft.Working.General;
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
            ApplyAutoStartRow();

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

    private void OnAutoCloseChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        // WindowHost reads General.AutoClose LIVE on every Deactivated (see WindowHost.Attach), so
        // committing the field is the whole job — no separate "apply" call needed.
        _draft.Working.General.AutoClose = AutoCloseToggle.IsChecked == true;
        _draft.Revalidate();
    }

    /// <summary>窗口置顶 applies on commit, for free: WindowHost.Attach subscribes <c>ApplyTopmost</c>
    /// to <see cref="AppEvents.SettingsChanged"/>, which <see cref="SettingsCommit.ApplyAsync"/> raises
    /// once the save has landed — so this handler must NOT also poke <c>IWindowHost.Window.Topmost</c>
    /// directly, or the live window would float above everything while the draft was still pending and
    /// 取消 would leave it there with no config value to justify it.</summary>
    private void OnTopMostChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _draft.Working.General.TopMost = TopMostToggle.IsChecked == true;
        _draft.Revalidate();
    }

    /// <summary>
    /// The ONLY writer of <see cref="SettingsDraft.AutoStartDesired"/>. 开机自启 has no field in
    /// <c>AppConfig</c> — the OS (Run key or MSIX StartupTask) IS the state — so it cannot ride the
    /// draft's config clone and is buffered by hand here instead; the write itself happens in
    /// <see cref="SettingsCommit.ApplyAutoStart"/>, on 确定 / 应用.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until Round 8, Task 1 this handler called <c>_autoStart.SetEnabled</c> on the click — the one
    /// control on the surface that wrote anything immediately, and the one that "delete the twelve
    /// CommitAsync call sites" would never have reached, because it never had one. 取消 needs no undo
    /// now, because nothing is written until the commit.
    /// </para>
    /// <para>
    /// The reason that write is worth re-verifying afterwards has NOT gone away, and moved with it: the
    /// packaged <c>StartupTaskAutoStartService</c> swallows every exception internally and never
    /// inspects the <c>StartupTaskState</c> its own WinRT call returns, so a silent Windows/policy
    /// refusal leaves <c>SetEnabled</c> looking like it succeeded. <see cref="SettingsCommit"/> clears
    /// the desire back to null before re-populating the cards, so <see cref="ApplyAutoStartRow"/> falls
    /// through to the live OS truth on both the success and the failure path — the same unconditional
    /// re-verification this method used to do, relocated to where the write now lives.
    /// </para>
    /// <para>
    /// Still a plain <c>void</c> handler with no <see cref="RunSafely"/> wrapper, unlike the five around
    /// it: it never awaited anything, and now it cannot throw either. The <c>_loading</c> and
    /// <c>_isPortable</c> guards are kept verbatim — a portable build never records a desire, which is
    /// what keeps <see cref="SettingsDraft.AutoStartDesired"/> null there and out of
    /// <see cref="SettingsDraft.IsDirty"/>.
    /// </para>
    /// </remarks>
    private void OnAutoStartChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading || _isPortable)
            return;

        _draft.AutoStartDesired = AutoStartToggle.IsChecked == true;
        _draft.Revalidate();
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
    /// disabled action rather than a still-live call to action. That is delivered by
    /// <c>IsEnabled</c> alone, which tracks <see cref="TaskbarPinStatus.Pinned"/> from the live COM
    /// query, and by the <c>Content</c> re-label below.</summary>
    /// <remarks>Between Round 3 and Round 7 this method also SWAPPED the button's Theme, because a
    /// disabled <c>TmAccentButton</c> still painted a dimmed blue swatch: that theme's inherited
    /// <c>^:disabled /template/ Border#Root</c> rule only drops Root to 60% opacity and never repaints
    /// Background off AccentDefaultBrush. Round 7, Task 3 deleted TmAccentButton outright — the Aero
    /// control themes have one command button, not a neutral one and an accent one — at which point
    /// both branches of the swap read TmButton and the swap was deleted with it. The button's Theme is
    /// now the static one BehaviorCard.axaml gives it, and a disabled TmButton has no accent fill to
    /// dim, so item 8's finding stays closed with one moving part fewer.</remarks>
    private void ApplyTaskbarPinState(TaskbarPinState state)
    {
        _taskbarPinState = state;
        var isPinned = state.Status is TaskbarPinStatus.Pinned;
        PinTaskbarBtn.IsEnabled = !isPinned;

        // No Theme is assigned here any more; BehaviorCard.axaml's static TmButton is the only one this
        // button ever has. That also retires a hazard this call site used to carry: ReloadFromConfig
        // reaches here while SettingsPage's constructor is still running — BEFORE MainWindow.ShowPageCore
        // assigns the freshly built page to PageHost.Content — with the whole card tree detached from
        // the logical tree, and a detached control's StylingParent chain terminates before ever reaching
        // Application.Styles (decompiled Avalonia 12.1.0), so FindResource returned UnsetValue and the
        // `as ControlTheme` yielded a hard null that would have clobbered a theme applied on a previous
        // visit. Full writeup of that episode:
        // docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md.
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

    private void OnHotkeyEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _draft.Working.General.GlobalHotkeyEnabled = HotkeyEnabledToggle.IsChecked == true;
        UpdateHotkeyEnabledState(); // immediate UX feedback; ReloadFromConfig repeats this either way
        _draft.Revalidate();
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (_loading)
            return;

        e.Handled = true; // capture the combo; never type into the read-only box
        var combo = HotkeyParser.FromKeyEvent(e.Key, e.KeyModifiers);
        if (combo == null)
            return; // still composing (modifier-only) or no modifier held — keep waiting

        if (combo == _draft.Working.General.GlobalHotkey)
            return; // no actual change — nothing to buffer

        _draft.Working.General.GlobalHotkey = combo;
        HotkeyBox.Text = combo; // the box is read-only, so this is the only thing that shows the capture
        _draft.Revalidate();
    }

    private void OnHotkeyClearClick(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        if (string.IsNullOrEmpty(_draft.Working.General.GlobalHotkey))
            return; // already clear — nothing to buffer

        _draft.Working.General.GlobalHotkey = "";
        HotkeyBox.Text = AppLanguage.Text("None", UiLanguage);
        _draft.Revalidate();
    }

    /// <summary>Same shape as <see cref="SettingsCommit.ApplyAsync"/>'s own error dialog: suppress
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
