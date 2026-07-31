using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.ViewModels;

/// <summary>
/// All launcher state and behavior. This is the port of the old <c>Index.razor</c>, which had no
/// view-model layer at all: Blazor re-rendered the whole component after every UI event handler, so
/// the original mutated private fields and relied on that implicit re-render. Avalonia has no such
/// thing, which is why every piece of state the window shows is an observable property here — the
/// single most likely silent porting bug is a field that changes without notifying.
/// </summary>
public sealed class LauncherViewModel : ObservableObject, IDisposable
{
    private readonly MenuService _menu;
    private readonly ConfigService _configService;
    private readonly IWindowHost _host;
    private readonly ILaunchService _launch;
    private readonly ISettingsLauncher _settings;
    private readonly IShellCommands _shell;
    private readonly IAppUpdateService _updates;
    private readonly IUpdatePromptService _updatePrompts;
    private readonly AppEvents _events;
    private readonly SoundService _sounds;
    private readonly ILogger<LauncherViewModel> _log;

    private AppConfig _cfg = new();
    private AppUpdateState _updateState = new(AppUpdateStatus.Idle);

    private string _filter = "";
    // _filter pre-trimmed once per change so the per-item match test doesn't Trim per rendered item.
    // Trimmed is what matches; the RAW value is what the box shows and what Escape tests for emptiness.
    private string _filterTrimmed = "";

    private bool _loadedOnce;        // first build finished → distinguishes "loading" from "empty"
    private bool _pendingFocus;      // focus the search box on the next layout where it actually exists
    private bool _firstShownHandled; // skip the redundant rescan on the first summon (startup just built it)
    private int? _menuFingerprint;   // last build's folder/tools fingerprint; null = none yet
    private int _iconFillEpoch;      // bumped per build so a superseded async icon-fill is ignored
    private int _toastVersion;       // latest-wins guard for the auto-clearing toast

    private bool _showAbout;
    private string? _toastText;

    /// <summary>Version shown in the About/splash footer — the assembly version (3 parts).</summary>
    private static readonly string AppVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Font used when <c>General.FontFamily</c> is blank. Defined once by
    /// <see cref="AppFonts.Default"/> so the launcher and the standard controls agree.</summary>
    private static FontFamily ThemeChromeFont => AppFonts.Default;

    public LauncherViewModel(
        MenuService menu,
        ConfigService configService,
        IWindowHost host,
        ILaunchService launch,
        ISettingsLauncher settings,
        IShellCommands shell,
        IAppUpdateService updates,
        IUpdatePromptService updatePrompts,
        AppEvents events,
        SoundService sounds,
        ILogger<LauncherViewModel> log)
    {
        _menu = menu;
        _configService = configService;
        _host = host;
        _launch = launch;
        _settings = settings;
        _shell = shell;
        _updates = updates;
        _updatePrompts = updatePrompts;
        _events = events;
        _sounds = sounds;
        _log = log;

        _cfg = configService.Config;

        _events.SettingsChanged += OnSettingsChanged;
        _events.LauncherShown += OnLauncherShown;
        _updates.StateChanged += OnUpdateStateChanged;
        _updateState = _updates.State; // seed synchronously; the event only reports CHANGES
    }

    /// <summary>Builds the view model straight from the container. Avoids requiring a DI registration
    /// of its own, matching how the rest of the shell reaches services (<c>App.Services</c>).</summary>
    public static LauncherViewModel Create(IServiceProvider services) => new(
        services.GetRequiredService<MenuService>(),
        services.GetRequiredService<ConfigService>(),
        services.GetRequiredService<IWindowHost>(),
        services.GetRequiredService<ILaunchService>(),
        services.GetRequiredService<ISettingsLauncher>(),
        services.GetRequiredService<IShellCommands>(),
        services.GetRequiredService<IAppUpdateService>(),
        services.GetRequiredService<IUpdatePromptService>(),
        services.GetRequiredService<AppEvents>(),
        services.GetRequiredService<SoundService>(),
        services.GetRequiredService<ILogger<LauncherViewModel>>());

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Content state
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The groups grid. One horizontal row that never wraps.</summary>
    public ObservableCollection<MenuGroupVm> Groups { get; } = new();

    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    /// True once a build has actually produced groups — or once the INITIAL build threw.
    /// <para>
    /// The menu build runs on a background STA worker, so without this latch a slow cold start would
    /// flash the "还没有任何快捷方式" prompt for a second before the real menu appeared. The second half
    /// matters just as much: if the very first build throws, forcing the latch true is what stops the
    /// UI hanging on the splash forever and lets the reveal run at all.
    /// </para>
    /// </summary>
    public bool LoadedOnce
    {
        get => _loadedOnce;
        private set
        {
            if (!Set(ref _loadedOnce, value))
                return;
            RaisePropertyChanged(nameof(ShowSplash));
            RaisePropertyChanged(nameof(ShowEmpty));
        }
    }

    /// <summary>Loading: no groups yet and the first build hasn't finished.</summary>
    public bool ShowSplash => !HasGroups && !LoadedOnce;

    /// <summary>Loaded, but there is genuinely nothing to show.</summary>
    public bool ShowEmpty => !HasGroups && LoadedOnce;

    /// <summary>Set on every summon; consumed by the window on the first layout pass in which the
    /// search box actually exists (on a cold start it lives inside the groups branch and may not be
    /// rendered yet). Deliberately NOT cleared when there are no groups, so a later build that does
    /// produce groups still focuses the box.</summary>
    public bool PendingFocus
    {
        get => _pendingFocus;
        set => Set(ref _pendingFocus, value);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Search / filter
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The raw text in the search box. Setting it filters which tiles are visible and
    /// reflows the grid within the window's existing bounds — it never touches the window's own size
    /// or position, which are fixed and user-owned now rather than derived from content.
    /// Nullable to match <c>TextBox.Text</c>, which pushes null for an emptied box.</summary>
    public string? Filter
    {
        get => _filter;
        set
        {
            var text = value ?? "";
            if (string.Equals(_filter, text, StringComparison.Ordinal))
                return;
            _filter = text;
            _filterTrimmed = text.Trim();
            RaisePropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>Matches on <see cref="MenuItemVm.Name"/> only — substring, ordinal, case-insensitive.
    /// No fuzzy matching, no pinyin, no accent folding, no word boundaries.</summary>
    /// <remarks>The filter genuinely removes non-matches and the grid reflows; sections that end up
    /// empty take their heading with them. Notifying <see cref="HasVisibleItems"/> here is what
    /// swaps the launcher between the section list and the "no matches" state.</remarks>
    private void ApplyFilter()
    {
        foreach (var group in Groups)
            group.ApplyFilter(_filterTrimmed);

        RaisePropertyChanged(nameof(HasVisibleItems));
        RaisePropertyChanged(nameof(ShowNoMatches));
    }

    /// <summary>True when at least one section still has something to show. Distinct from
    /// <see cref="HasGroups"/>, which asks whether any groups were BUILT — the difference is exactly
    /// the case where a filter matched nothing.</summary>
    public bool HasVisibleItems
    {
        get
        {
            foreach (var group in Groups)
            {
                if (group.HasVisibleItems)
                    return true;
            }
            return false;
        }
    }

    /// <summary>There are shortcuts, but the current filter matched none of them.</summary>
    public bool ShowNoMatches => HasGroups && !HasVisibleItems;

    /// <summary>Clear the filter from the "no matches" state's button.</summary>
    public void ClearFilter() => Filter = "";

    /// <summary>The first ENABLED item whose name matches, in group-then-item order (type-and-Enter).</summary>
    private MenuItemVm? FirstMatch()
    {
        if (_filterTrimmed.Length == 0)
            return null;
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                if (!item.IsDisabled && item.Matches(_filterTrimmed))
                    return item;
            }
        }
        return null;
    }

    /// <summary>Enter in the search box: launch the first highlighted match; nothing at all when
    /// nothing matches (no beep, no message).</summary>
    public void HandleSearchEnter()
    {
        if (FirstMatch() is { } first)
            LaunchItem(first);
    }

    /// <summary>Escape in the search box, two-stage: a non-empty filter is cleared (the box keeps
    /// focus and the window stays up); a second press on an empty filter hides the launcher.</summary>
    public void HandleSearchEscape()
    {
        if (!string.IsNullOrEmpty(_filter))
            Filter = "";
        else
            _host.Hide();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Localized text (re-raised whenever the language may have changed)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private string L(string key, params object?[] args) =>
        AppLanguage.Format(key, _cfg.General.Language, args);

    public string MenuOpenRootText => L("MenuOpenRoot");
    public string MenuOptionsText => L("MenuOptions");
    public string MenuRefreshText => L("MenuRefresh");
    public string MenuAboutText => L("MenuAbout");
    public string MenuExitText => L("MenuExit");
    public string MenuMoreText => L("MenuMore");
    public string MenuHideText => L("MenuHide");
    public string SearchPlaceholderText => L("SearchPlaceholder");
    public string EmptyTitleText => L("EmptyTitle");
    public string EmptyHelpText => L("EmptyHelp");
    public string OpenOptionsText => L("OpenOptions");
    public string NoMatchTitleText => L("NoMatchTitle");
    public string NoMatchHelpText => L("NoMatchHelp");
    public string ClearFilterText => L("ClearFilter");
    public string ClickToDismissText => AppLanguage.Text("ClickToDismiss", _cfg.General.Language);

    public string VersionText => "TanMenu v" + AppVersion;

    /// <summary>Computed at render time, exactly as the old splash did — never a baked-in year.</summary>
    public string CopyrightText => $"© {DateTime.Now.Year} TanSoft. All rights reserved.";

    private void RaiseLocalizedTextChanged()
    {
        RaisePropertyChanged(nameof(MenuOpenRootText));
        RaisePropertyChanged(nameof(MenuOptionsText));
        RaisePropertyChanged(nameof(MenuRefreshText));
        RaisePropertyChanged(nameof(MenuAboutText));
        RaisePropertyChanged(nameof(MenuExitText));
        RaisePropertyChanged(nameof(MenuMoreText));
        RaisePropertyChanged(nameof(MenuHideText));
        RaisePropertyChanged(nameof(SearchPlaceholderText));
        RaisePropertyChanged(nameof(EmptyTitleText));
        RaisePropertyChanged(nameof(EmptyHelpText));
        RaisePropertyChanged(nameof(OpenOptionsText));
        RaisePropertyChanged(nameof(NoMatchTitleText));
        RaisePropertyChanged(nameof(NoMatchHelpText));
        RaisePropertyChanged(nameof(ClearFilterText));
        RaisePropertyChanged(nameof(ClickToDismissText));
        RaisePropertyChanged(nameof(CopyrightText));
        RaisePropertyChanged(nameof(UpdateNoticeText));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Appearance (font + button size class)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The configured font, applied to the whole window. In Avalonia the family is inherited by every
    /// text-bearing control, so unlike the CSS build there is no per-control override to fight — but
    /// the requirement is the same: the setting must reach the launcher BUTTON text, not just the
    /// chrome. A blank setting falls back to the theme's own chrome font; a garbage name degrades
    /// through the trailing fallbacks rather than rendering as tofu.
    /// </summary>
    public FontFamily WindowFontFamily => AppFonts.Resolve(_cfg.General.FontFamily, whenBlank: ThemeChromeFont);

    /// <summary>
    /// The face for Subtitle-sized text (20 px). Windows 11 switches Segoe UI Variable to its
    /// Display optical size at 18 px and up — but only when the user has NOT chosen a font of their
    /// own, because a font setting that stopped applying to headings would look like a bug.
    /// </summary>
    public FontFamily TitleFontFamily =>
        AppFonts.Resolve(_cfg.General.FontFamily, whenBlank: AppFonts.DefaultDisplay);

    // Button size class: anything unrecognized falls back to Small, exactly as the old size-* class did.
    public bool IsSmallButtons => SizeClass == "Small";
    public bool IsMediumButtons => SizeClass == "Medium";
    public bool IsLargeButtons => SizeClass == "Large";

    private string SizeClass => _cfg.General.ButtonSize switch
    {
        "Large" => "Large",
        "Medium" => "Medium",
        _ => "Small",
    };

    private void RaiseAppearanceChanged()
    {
        RaisePropertyChanged(nameof(WindowFontFamily));
        RaisePropertyChanged(nameof(TitleFontFamily));
        RaisePropertyChanged(nameof(IsSmallButtons));
        RaisePropertyChanged(nameof(IsMediumButtons));
        RaisePropertyChanged(nameof(IsLargeButtons));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Update notice
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Only these three statuses render the notice; Disabled / StoreManaged / Idle /
    /// Checking / UpToDate / Failed render nothing at all.</summary>
    public bool ShowUpdateNotice => _updateState.Status is AppUpdateStatus.Available
        or AppUpdateStatus.Downloading
        or AppUpdateStatus.ReadyToRestart;

    public bool IsUpdateDownloading => _updateState.Status == AppUpdateStatus.Downloading;

    public string UpdateNoticeText => _updateState.Status switch
    {
        AppUpdateStatus.Available => L("UpdateNoticeAvailable", _updateState.Version ?? ""),
        AppUpdateStatus.Downloading => L(
            "UpdateDownloading", _updateState.Version ?? "", Math.Clamp(_updateState.Progress, 0, 100)),
        AppUpdateStatus.ReadyToRestart => L("UpdateNoticeReady", _updateState.Version ?? ""),
        _ => "",
    };

    /// <summary>Fill width of the 84px progress track (82px inside its 1px border).</summary>
    public double UpdateProgressWidth => 82.0 * Math.Clamp(_updateState.Progress, 0, 100) / 100.0;

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        // Snapshot on the CALLING (background) thread and marshal the local — never read
        // Updates.State inside the marshaled callback, or a second change could be missed/duplicated.
        var state = _updates.State;
        if (Dispatcher.UIThread.CheckAccess())
            ApplyUpdateState(state);
        else
            Dispatcher.UIThread.Post(() => ApplyUpdateState(state));
    }

    private void ApplyUpdateState(AppUpdateState state)
    {
        // The notice is a whole extra row (and the progress track a whole extra column) when it
        // appears, but the window no longer re-measures or re-places itself for a content change: it
        // is a fixed, user-sized application window now, and the body simply scrolls further.
        _updateState = state;
        RaisePropertyChanged(nameof(ShowUpdateNotice));
        RaisePropertyChanged(nameof(IsUpdateDownloading));
        RaisePropertyChanged(nameof(UpdateNoticeText));
        RaisePropertyChanged(nameof(UpdateProgressWidth));
    }

    /// <summary>Click on the update notice. Both actions require a native confirmation; declining
    /// changes nothing at all.</summary>
    public async Task HandleUpdateNoticeAsync()
    {
        try
        {
            // Re-read the status FRESH rather than trusting the rendered snapshot: the state can have
            // moved on between the render and the click.
            var state = _updates.State;
            var language = _cfg.General.Language;

            if (state.Status == AppUpdateStatus.Available)
            {
                if (!await _updatePrompts.ConfirmDownloadAsync(state.Version ?? "", language))
                    return;

                await _updates.DownloadUpdateAsync();
                if (_updates.State.Status == AppUpdateStatus.Failed)
                    ShowToast(L("UpdateFailed", _updates.State.Error ?? L("UnknownError")));
            }
            else if (state.Status == AppUpdateStatus.ReadyToRestart &&
                     await _updatePrompts.ConfirmRestartAsync(state.Version ?? "", language))
            {
                _updates.ApplyAndRestart(); // the process restarts from here
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Update notice action failed");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // About overlay + toast
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The About panel covers the window and is dismissed by clicking anywhere on it. There
    /// is no close button and no Escape handler — that was true of the original too.</summary>
    public bool ShowAbout
    {
        get => _showAbout;
        private set => Set(ref _showAbout, value);
    }

    public void OpenAbout() => ShowAbout = true;

    public void CloseAbout() => ShowAbout = false;

    public bool HasToast => !string.IsNullOrEmpty(_toastText);

    public string? ToastText
    {
        get => _toastText;
        private set
        {
            if (!Set(ref _toastText, value))
                return;
            RaisePropertyChanged(nameof(HasToast));
        }
    }

    public void DismissToast() => ToastText = null;

    /// <summary>Show a transient error; auto-clears after 4s. The version guard is what makes it
    /// latest-wins — without it an older toast's timer would erase a newer message.</summary>
    private void ShowToast(string message)
    {
        ToastText = message;
        var version = ++_toastVersion;
        _ = ClearToastLaterAsync(version);
    }

    private async Task ClearToastLaterAsync(int version)
    {
        // Fully guarded: the returned Task is discarded, so a throw here would otherwise surface as
        // an unobserved task exception at GC time.
        try
        {
            await Task.Delay(4000);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_toastVersion == version)
                    ToastText = null;
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Toast auto-clear failed");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Commands from the window
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The header's own hide glyph (not the system title bar — Minimize and the system Close
    /// button are real OS operations now; the system Close hides too, via MainWindow.OnClosing). There
    /// is still no application exit from either.</summary>
    public void HideWindow() => _host.Hide();

    public void OpenSettings() => _settings.Open();

    public void ExitApp() => _shell.Exit();

    /// <summary>"打开主目录". A blank root folder is a deliberate silent no-op.</summary>
    public void OpenRoot()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.RootFolder))
            _launch.OpenFolder(_cfg.RootFolder);
    }

    /// <summary>Group title click. Failure is silent — <c>OpenFolder</c> swallows its exceptions and
    /// there was never a toast for a failed folder open.</summary>
    public void OpenGroupFolder(MenuGroupVm group)
    {
        if (group.IsDirectoryClickable)
            _launch.OpenFolder(group.Directory);
    }

    public void PlayClickSound() => _ = _sounds.PlayClickSoundAsync();

    public void PlayHoverSound() => _ = _sounds.PlayHoverSoundAsync();

    /// <summary>Launch an item. On SUCCESS the window hides; on FAILURE it deliberately stays up and
    /// shows a toast — the menu used to silently vanish when a target had been deleted or moved.</summary>
    public void LaunchItem(MenuItemVm item)
    {
        if (item.IsDisabled)
            return;

        if (_launch.Launch(item.FullPath))
        {
            _log.LogInformation("Launched {Path}", item.FullPath);
            _host.Hide();
        }
        else
        {
            _log.LogWarning("Launch failed for {Path}", item.FullPath);
            ShowToast(L("LaunchFailed", item.Name));
        }
    }

    /// <summary>The 刷新 menu action: an unconditional rescan.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            await ReloadAsync(force: true); // the user explicitly asked to rescan
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Refresh failed");
            ShowToast(L("RefreshFailed"));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Lifecycle: initial build, summon, settings change
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The startup build. Runs after the window is already shown — <c>App.ShowLauncherWindow</c>
    /// is what put it on screen, and there is no reveal to wait for any more. <see cref="LoadedOnce"/>
    /// flipping just swaps the splash for the real content or the empty state, wherever this build
    /// lands.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync(force: true);
        }
        catch (Exception ex)
        {
            // Never hang on the splash: fall through to the empty state (which carries the two
            // recovery buttons) so the reveal can proceed.
            _log.LogError(ex, "Initial menu build failed");
            LoadedOnce = true;
        }
    }

    private void OnLauncherShown()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _ = HandleLauncherShownAsync();
        else
            Dispatcher.UIThread.Post(() => _ = HandleLauncherShownAsync());
    }

    private async Task HandleLauncherShownAsync()
    {
        // Wholly guarded: the Task is discarded, so nothing may escape.
        try
        {
            // Cleared + focused each summon so you can type-to-filter immediately. The focus itself is
            // deferred to the next layout: on a cold start the search box lives inside the groups
            // branch and may not exist yet.
            Filter = "";
            PendingFocus = true;

            // Startup just built the menu, so skip the redundant rescan on the very first summon — but
            // ONLY if that build actually succeeded (fingerprint set). If it FAILED, fall through and
            // rebuild now, so a transient startup fault doesn't strand the user in the empty state
            // until a second summon.
            if (!_firstShownHandled)
            {
                _firstShownHandled = true;
                if (_menuFingerprint != null)
                    return;
            }

            // Pick up shortcuts added/removed/renamed since the last build. The fingerprint gate makes
            // this nearly free when nothing changed; only a real change costs a re-render.
            await ReloadAsync(force: false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reload on launcher shown failed");
        }
    }

    private void OnSettingsChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _ = HandleSettingsChangedAsync();
        else
            Dispatcher.UIThread.Post(() => _ = HandleSettingsChangedAsync());
    }

    private async Task HandleSettingsChangedAsync()
    {
        try
        {
            // force:false — picks up the new config and rebuilds the groups only if the folders or
            // tools actually changed; a size/column/font-only change skips the folder rescan but still
            // re-renders.
            await ReloadAsync(force: false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reload after settings change failed");
        }
    }

    /// <summary>
    /// (Re)build the menu. <paramref name="force"/> rebuilds unconditionally (startup, settings,
    /// manual refresh); otherwise the build is skipped when the folder/tools fingerprint is unchanged.
    /// Returns true when the groups were actually rebuilt. Always re-reads the config and refreshes
    /// everything derived from it, so a settings-only change still re-renders.
    /// </summary>
    private async Task<bool> ReloadAsync(bool force)
    {
        _cfg = _configService.Config; // re-read every time: the settings window replaces General wholesale

        var result = await _menu.BuildMenuAsync(
            _cfg.RootFolder, _cfg.General, force ? null : _menuFingerprint);

        if (result.Changed)
        {
            _menuFingerprint = result.Fingerprint;
            ReplaceGroups(result.Groups);
            LoadedOnce = true;
            StartIconFill(result.Pending);
            _log.LogInformation(
                "Launcher built {Count} groups from root '{Root}', {Pending} icons pending",
                Groups.Count, _cfg.RootFolder, result.Pending.Count);
        }
        else
        {
            // The folder scan was skipped, not the render: a settings-only change still has to
            // relabel the tooltips on the groups we already hold.
            ApplyGroupSettings();
        }

        RaiseLocalizedTextChanged();
        RaiseAppearanceChanged();

        return result.Changed;
    }

    private void ReplaceGroups(List<MenuGroupVm> groups)
    {
        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);

        ApplyGroupSettings();
        // The filter survives a rebuild, so the fresh items have to be filtered before they render.
        // The groups are new objects whose memoized filter is unset, but an existing group reused by
        // a partial rebuild would otherwise think it had already applied this text.
        foreach (var group in Groups)
            group.InvalidateFilter();
        ApplyFilter();

        RaisePropertyChanged(nameof(HasGroups));
        RaisePropertyChanged(nameof(ShowSplash));
        RaisePropertyChanged(nameof(ShowEmpty));
    }

    private void ApplyGroupSettings()
    {
        var tooltip = L("OpenFolderTooltip");
        foreach (var group in Groups)
            group.TitleTooltip = group.IsDirectoryClickable ? tooltip : null;
    }

    /// <summary>
    /// Kick off the async icon fill for a build's uncached icons: the STA worker extracts them and each
    /// batch is marshaled back to replace the placeholder. Icon slots are fixed-size, so this never
    /// reflows the layout — which is exactly why there is no resize in this path.
    /// </summary>
    private void StartIconFill(IReadOnlyList<MenuService.PendingIcon> pending)
    {
        // Bumped BEFORE the empty-list early return, so even a build with zero pending icons
        // invalidates an older in-flight fill.
        var epoch = ++_iconFillEpoch;
        if (pending.Count == 0)
            return;

        _ = _menu.ExtractIconsAsync(
                pending,
                batch => Dispatcher.UIThread.Post(() =>
                {
                    if (epoch != _iconFillEpoch)
                        return; // a newer rebuild superseded this fill
                    foreach (var (item, base64) in batch)
                        item.IconBase64 = base64;
                }),
                () => epoch != _iconFillEpoch)
            // Observe the outer task: a re-queued STA job faults if its enqueue races CompleteAdding
            // during Dispose, and an unobserved Task exception would crash the process at GC time.
            .ContinueWith(static t => { _ = t.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Mandatory: the services are singletons and this view model lives as long as the
    /// tray-resident window, so a missed unsubscribe is a leak plus a notification into a torn-down
    /// view.</summary>
    public void Dispose()
    {
        _events.SettingsChanged -= OnSettingsChanged;
        _events.LauncherShown -= OnLauncherShown;
        _updates.StateChanged -= OnUpdateStateChanged;
    }
}
