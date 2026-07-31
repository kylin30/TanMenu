using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>The last taskbar-pin state this card knows about. Queried once, from the constructor
    /// (the closest analog to <c>SettingsWindow</c>'s per-open <c>Opened</c> query: <c>SettingsPage</c>
    /// is built once and cached for the app's life — see <c>MainWindow.ShowPage</c> — so there is no
    /// per-visit "window opened" moment to hook here), then only ever re-labelled (never re-queried)
    /// by <see cref="ReloadFromConfig"/>, which the shared SettingsChanged event can trigger far more
    /// often than a real pin-state change (any card's commit, anywhere on the page, re-fires it).</summary>
    private TaskbarPinState _taskbarPinState = new(TaskbarPinStatus.Available);

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

        RunSafely(RefreshTaskbarPinStateAsync);
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
            AutoStartToggle.IsEnabled = !_isPortable;
            AutoStartToggle.IsChecked = !_isPortable && _autoStart.IsEnabled();

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
    /// here. A failed registry/StartupTask write is logged rather than shown as a save-failed dialog —
    /// the same contract <c>SettingsWindow.CommitAsync</c> had: there, <c>_autoStart.SetEnabled</c> ran
    /// OUTSIDE the try/catch that wrapped <c>_config.SaveAsync</c>, so a thrown exception only ever hit
    /// the window's top-level <c>RunSafely</c> log-and-swallow, never a dialog. What must NOT happen is
    /// the toggle being left showing the click that failed: on an exception, it is reset to what
    /// <see cref="IAutoStartService.IsEnabled"/> actually reports, not just flipped back.</summary>
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
            _loading = true;
            try { AutoStartToggle.IsChecked = _autoStart.IsEnabled(); }
            finally { _loading = false; }
        }
    }

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

    private async Task RefreshTaskbarPinStateAsync()
    {
        PinTaskbarBtn.IsEnabled = false; // dead while the async query runs
        ApplyTaskbarPinState(await _taskbarPin.GetStateAsync());
    }

    private void ApplyTaskbarPinState(TaskbarPinState state)
    {
        _taskbarPinState = state;
        PinTaskbarBtn.IsEnabled = state.Status is not TaskbarPinStatus.Pinned;
        PinTaskbarBtn.Content = state.Status is TaskbarPinStatus.Pinned
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
