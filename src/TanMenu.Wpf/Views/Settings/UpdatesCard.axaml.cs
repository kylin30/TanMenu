using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 更新 card group: one card carrying the update status, a channel-specific help line, the download
/// progress and the check/download actions. Ported from <c>SettingsWindow</c>'s 更新 tab
/// (<c>Updates_StateChanged</c>, <c>RenderUpdateState</c>, <c>CheckUpdate_Click</c>,
/// <c>UpdateAction_Click</c>).
/// <para>
/// Round 5, item 3 deleted this group's second card, 当前版本 — an information row with no control in an
/// action group, whose version line would otherwise have been shown twice one card apart once 关于 moved
/// into 设置. The version now lives on the 关于 card and the channel help line moved onto this card's
/// expansion row; <c>SettingsWindow</c>'s <c>GetCurrentVersion</c> port went with the card.
/// </para>
/// Unlike every other card group, this one writes no config at all —
/// <see cref="IAppUpdateService"/> IS the state, the same way <see cref="IAutoStartService"/> is for
/// 开机自启 in <see cref="BehaviorCard"/> — so it neither writes <see cref="SettingsDraft.Working"/> nor
/// calls <see cref="SettingsDraft.Revalidate"/> anywhere in this file, and 确定 / 取消 / 应用 have nothing
/// on this card to commit or discard.
///
/// The 应用 page (<c>AppPage</c> / <c>LauncherViewModel</c>) shows its own update notice driven by the
/// SAME <see cref="IAppUpdateService"/> singleton, subscribing to the same <c>StateChanged</c> event and
/// reading the same <c>State</c> independently. The two never need to be told about each other: because
/// there is exactly one source of truth, both always render whatever it currently reports, so they
/// cannot disagree. They also cannot both drive a download at once —
/// <c>VelopackAppUpdateService.DownloadUpdateAsync</c> guards itself with a non-blocking
/// <c>SemaphoreSlim.WaitAsync(0, ...)</c>: whichever caller (this card's 下载更新 or the launcher's
/// notice click) arrives second while a download is already in flight simply returns immediately
/// without starting a second one or changing the state.
/// </summary>
public partial class UpdatesCard : UserControl, ISettingsCard
{
    private readonly SettingsDraft _draft;
    private readonly IAppUpdateService _updates;

    /// <summary>Read off the draft rather than the live config, like every other card since Round 8,
    /// Task 1 — this group writes nothing, but a pending 语言 change has to re-label it too when the
    /// commit's SettingsChanged comes round.</summary>
    private string UiLanguage => _draft.Working.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public UpdatesCard()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _updates = App.Services.GetRequiredService<IAppUpdateService>();

        // The StateChanged subscription itself is set up in OnAttachedToVisualTree, not here — see its
        // remarks for why a constructor-only subscription would leak this control forever.
    }

    /// <summary><see cref="IAppUpdateService"/> is a singleton kept alive for the app's whole life, while
    /// this card is detached and re-attached every time its container comes and goes — the cached
    /// <c>SettingsPage</c> was detached by every navigation away from 设置, and Round 8, Task 2's
    /// <c>OptionsWindow</c> is destroyed on close and realises this card's tab only while it is the
    /// selected one. Subscribing once in the
    /// constructor and never unsubscribing would make the singleton hold this control (and everything it
    /// references) for the rest of the process after the very first visit, and would leave the handler
    /// wired even while this card sits detached inside a window nobody is looking at. Pair with
    /// <see cref="OnDetachedFromVisualTree"/> instead — same pattern
    /// <c>BehaviorCard.OnAttachedToVisualTree</c> / <c>FoldersCard.OnAttachedToVisualTree</c> use for
    /// their own re-attach hooks. Re-render on every attach, too: the state can have moved on while this
    /// card was detached (e.g. the launcher's own update notice drove a download to completion while the
    /// user was on 应用), so the status shown here on return must not be stale.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _updates.StateChanged += OnUpdateStateChanged;
        RenderUpdateState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _updates.StateChanged -= OnUpdateStateChanged;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Populate every control from the live config/update state. No <c>_loading</c> guard,
    /// unlike most other card groups: every control here is either read-only display text, a
    /// ProgressBar, or a Button — nothing a Click could re-enter the way a ToggleSwitch/ComboBox's
    /// Changed event is re-entered by a value being assigned programmatically. Same reasoning as
    /// <c>FoldersCard</c>; see its class remarks.</summary>
    public void ReloadFromConfig()
    {
        var lang = UiLanguage;

        GroupHead.Text = AppLanguage.Text("Updates", lang);

        UpdateStatusTitle.Text = AppLanguage.Text("UpdateStatusTitle", lang);
        UpdateHelpText.Text = _updates.State.Status switch
        {
            AppUpdateStatus.StoreManaged => L("UpdateStoreManaged"),
            AppUpdateStatus.Disabled => L("UpdateUnavailable"),
            _ => L("UpdatePortableHelp"),
        };

        RenderUpdateState();
    }

    /// <summary>Velopack's download-progress callback runs on a thread-pool thread, not the UI thread
    /// (see <see cref="IAppUpdateService.StateChanged"/>'s remarks) — ported verbatim from
    /// <c>SettingsWindow.Updates_StateChanged</c>. Touching a control off the UI thread in Avalonia is a
    /// crash, not a glitch, so every path here marshals before calling <see cref="RenderUpdateState"/>.</summary>
    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RenderUpdateState);
            return;
        }

        RenderUpdateState();
    }

    private void RenderUpdateState()
    {
        var state = _updates.State;
        UpdateStatusText.Text = state.Status switch
        {
            AppUpdateStatus.StoreManaged => L("UpdateStoreManagedStatus"),
            AppUpdateStatus.Disabled => L("UpdateDisabledStatus"),
            AppUpdateStatus.Checking => L("UpdateChecking"),
            AppUpdateStatus.UpToDate => L("UpdateUpToDate"),
            AppUpdateStatus.Available => L("UpdateAvailable", state.Version ?? ""),
            AppUpdateStatus.Downloading => L("UpdateDownloading", state.Version ?? "", state.Progress),
            AppUpdateStatus.ReadyToRestart => L("UpdateReady", state.Version ?? ""),
            AppUpdateStatus.Failed => L("UpdateFailed", state.Error ?? L("UnknownError")),
            _ => L("UpdateIdle"),
        };

        UpdateProgress.IsVisible = state.Status == AppUpdateStatus.Downloading;
        UpdateProgress.Value = state.Progress;

        CheckUpdateBtn.Content = L("CheckForUpdates");
        CheckUpdateBtn.IsEnabled = state.Status is AppUpdateStatus.Idle
            or AppUpdateStatus.UpToDate
            or AppUpdateStatus.Available
            or AppUpdateStatus.Failed;

        UpdateActionBtn.IsVisible = state.Status is AppUpdateStatus.Available or AppUpdateStatus.ReadyToRestart;
        UpdateActionBtn.Content = state.Status == AppUpdateStatus.ReadyToRestart
            ? L("RestartToUpdate")
            : L("DownloadUpdate");
    }

    // The service catches internally and reports Failed, so there is no try/catch here — same as
    // SettingsWindow.CheckUpdate_Click.
    private void OnCheckUpdateClick(object? sender, RoutedEventArgs e) =>
        RunSafely(() => _updates.CheckForUpdatesAsync());

    private void OnUpdateActionClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_updates.State.Status == AppUpdateStatus.ReadyToRestart)
            _updates.ApplyAndRestart();
        else if (_updates.State.Status == AppUpdateStatus.Available)
            await _updates.DownloadUpdateAsync();
    });

    /// <summary>Run an async click handler without letting a fault escape the event — same rationale as
    /// <c>SettingsWindow.RunSafely</c> / <c>BehaviorCard.RunSafely</c>: Avalonia has no WPF-style global
    /// DispatcherUnhandledException hook, so an escaping async-void fault would tear the process down,
    /// and logging is the floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "更新设置操作失败"); }
    }
}
