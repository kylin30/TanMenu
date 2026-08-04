using System.Collections.ObjectModel;
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
    private Task? _initialBuild;     // the startup build, so the first summon can await it, not race it
    private int? _menuFingerprint;   // last build's folder/tools fingerprint; null = none yet
    private int _iconFillEpoch;      // bumped per build so a superseded async icon-fill is ignored
    private int _toastVersion;       // latest-wins guard for the auto-clearing toast

    private string? _toastText;

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

    /// <summary>The raw text in the search box. Setting it filters which tiles are visible and reflows
    /// the grid within the window's existing bounds — filtering deliberately does NOT re-fit the window,
    /// even though Round 2 made its size content-derived again (<c>WindowHost.FitToContent</c>): a
    /// window that resized on every keystroke as matches narrowed would be far worse than a page that
    /// briefly has room to spare, and the re-fit triggers <c>MainWindow.RefitToContent</c> is wired to
    /// are all deliberate, discrete user actions rather than continuous ones.
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

    // MenuOpenRootText/MenuAboutText/MenuExitText/MenuMoreText used to back the title strip's ⋯
    // overflow MenuFlyout's Header bindings and the button's own tooltip. Round 3, Task 2 moved those
    // four commands into rail rows whose labels/tooltips/automation names MainWindow.ApplyRailText sets
    // imperatively (via AppLanguage.Text directly, the same way NavAppsLabel/NavSettingsLabel already
    // were) rather than through a XAML {Binding} — see that method's own remarks — so those four
    // properties have no reader left and were removed. MenuRefreshText survives: AppPage's own
    // empty-state "刷新" button (AppPage.axaml) still binds it, unrelated to the rail move.
    public string MenuRefreshText => L("MenuRefresh");
    public string SearchPlaceholderText => L("SearchPlaceholder");
    public string EmptyTitleText => L("EmptyTitle");
    public string EmptyHelpText => L("EmptyHelp");
    public string OpenOptionsText => L("OpenOptions");
    public string NoMatchTitleText => L("NoMatchTitle");
    public string NoMatchHelpText => L("NoMatchHelp");
    public string ClearFilterText => L("ClearFilter");

    // Both footer lines come from AppIdentity (Services/AppIdentity.cs), which Round 5, item 2 created
    // when 关于 became a settings card: the version expression and the copyright string would otherwise
    // have had a third and a second copy. VersionText is deliberately still built here rather than on
    // AppIdentity — "TanMenu v0.12.0" is this splash's own footer format, not the app's identity.
    public string VersionText => AppIdentity.ProductName + " v" + AppIdentity.Version;

    public string CopyrightText => AppIdentity.Copyright;

    private void RaiseLocalizedTextChanged()
    {
        RaisePropertyChanged(nameof(MenuRefreshText));
        RaisePropertyChanged(nameof(SearchPlaceholderText));
        RaisePropertyChanged(nameof(EmptyTitleText));
        RaisePropertyChanged(nameof(EmptyHelpText));
        RaisePropertyChanged(nameof(OpenOptionsText));
        RaisePropertyChanged(nameof(NoMatchTitleText));
        RaisePropertyChanged(nameof(NoMatchHelpText));
        RaisePropertyChanged(nameof(ClearFilterText));
        RaisePropertyChanged(nameof(CopyrightText));
        RaisePropertyChanged(nameof(UpdateNoticeText));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Appearance (font)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The face for Subtitle-sized text — 16 DIP since Round 7, Task 7 re-valued
    /// <c>FontSizeSubtitle</c> to Aero's published main-instruction size, 20 before it. It resolves
    /// to <see cref="AppFonts.DefaultDisplay"/> only when the user has NOT chosen a font of their
    /// own, because a font setting that stopped applying to headings would look like a bug.
    /// </summary>
    /// <remarks>
    /// The property earned its own existence because Windows 11 switched Segoe UI Variable to its
    /// Display optical size at 18 px and up. Aero has no optical sizes, so <c>DefaultDisplay</c> and
    /// the ordinary UI stack are now the same string — see that constant, which says why it is kept
    /// separate anyway. Nothing about this property's contract changed.
    /// </remarks>
    public FontFamily TitleFontFamily =>
        AppFonts.Resolve(_cfg.General.FontFamily, whenBlank: AppFonts.DefaultDisplay);

    // The 小/中/大 button-size setting (IsSmallButtons/IsMediumButtons/IsLargeButtons + the
    // Classes.size-* bindings AppPage.axaml used to set) went away in Round 3, Task 4, which left
    // every tile at the one width that setting had called "小" (a 168 DIP Width Setter on
    // TmTileButton). Round 4, item 3 deleted that Setter too, so Themes/Shell/Launcher.axaml's
    // TmTileButton sets no Width at all and a tile is as wide as its own icon-plus-label content —
    // see GeneralConfig.ButtonSize's own remarks. Either way nothing on this view model has named a
    // tile size since Task 4. GeneralConfig.ColButtonCount took its place as the live 外观 setting;
    // see ApplyGroupSettings below for how it reaches each MenuGroupVm.

    private void RaiseAppearanceChanged()
    {
        RaisePropertyChanged(nameof(TitleFontFamily));
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

    /// <summary>The download percentage, 0..100, for the update banner's <c>ProgressBar.Value</c>.
    /// It used to be <c>UpdateProgressWidth</c>, a DIP width for the inner Border of the hand-drawn
    /// bar AppPage.axaml carried until Round 7, Task 5 (<c>82.0 * pct / 100</c> — 82 being the 84 DIP
    /// track less its 1 DIP border on each side). A real ProgressBar does that arithmetic itself
    /// against its own Minimum/Maximum, so the view model hands over the percentage and stops
    /// knowing the track's width.</summary>
    public double UpdateProgressPercent => Math.Clamp(_updateState.Progress, 0, 100);

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
        // The notice is a whole extra row (and the progress track a whole extra column) when it appears,
        // and Round 2 made the window's size content-derived again, so that DOES re-measure the window:
        // AppPage watches ShowUpdateNotice and raises NoticeVisibilityChanged, which MainWindow turns
        // into a WindowHost.FitToContent call. Nothing to do about it from here — the property change
        // raised below is the whole mechanism.
        _updateState = state;
        RaisePropertyChanged(nameof(ShowUpdateNotice));
        RaisePropertyChanged(nameof(IsUpdateDownloading));
        RaisePropertyChanged(nameof(UpdateNoticeText));
        RaisePropertyChanged(nameof(UpdateProgressPercent));
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
    // Toast
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // ShowAbout/OpenAbout/CloseAbout used to head this section: the flag AppPage's 关于 overlay bound
    // its IsVisible to, and the two methods the rail's 关于 row and the overlay's own PointerPressed
    // called. Round 5, item 2 ("关于集成到设置页中") deleted the overlay outright — 关于 is a card at the
    // bottom of the 设置 page now (Views/Settings/AboutCard.axaml) — so all three went with it, along
    // with ClickToDismissText above. VersionText and CopyrightText did NOT: the startup splash still
    // binds both.

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
    /// <remarks>
    /// Memoized, and the task is kept: <see cref="HandleLauncherShownAsync"/> awaits it rather than
    /// racing it. The cold start's LauncherShown arrives while this build is still in flight, and a
    /// guard that only tested the fingerprint therefore always saw null and started a SECOND full
    /// build one millisecond later.
    /// </remarks>
    public Task InitializeAsync() => _initialBuild ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
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
            //
            // AWAIT the startup build before testing the fingerprint. On a cold start this handler runs
            // while that build is still in flight (App raises LauncherShown as soon as Show() returns;
            // the build is several disk scans long), so testing the fingerprint immediately always saw
            // null and fell through to a second, concurrent, full rebuild of the same menu — visible in
            // the shipped log as two "Launcher built" lines a millisecond apart on every cold start.
            if (!_firstShownHandled)
            {
                _firstShownHandled = true;
                if (_initialBuild is { } startupBuild)
                {
                    await startupBuild; // InitializeCoreAsync swallows its own faults; this cannot throw
                    if (_menuFingerprint != null)
                        return;
                }
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
        ContentMetricsChanged?.Invoke();

        return result.Changed;
    }

    /// <summary>
    /// Raised at the end of every (re)build, once everything that decides how large this page MEASURES
    /// has been applied to it: the groups themselves, each group's column layout (每列个数,
    /// <see cref="TanMenu.Core.Models.GeneralConfig.ColButtonCount"/> — Round 3, Task 4 replaced the
    /// 小/中/大 tile-size class this used to name, and Round 4, item 3 removed the fixed 168 DIP width
    /// that replaced it, so a tile is as wide as its own label now and no setting names a width), the
    /// 字体 face — which since item 3 decides tile widths, not just the text inside them — and every
    /// localized string.
    /// </summary>
    /// <remarks>
    /// Round 2's review finding 2: the window is sized from this page's natural size again, but a
    /// settings change could re-render the page without ever re-fitting the window — originally observed
    /// as 大 leaving a horizontal scrollbar and 小 leaving dead space, back when 小/中/大 was the setting
    /// in question; the same risk applies to a 每列个数 change today. <c>MainWindow</c> re-fits from this.
    /// <para>
    /// An EVENT rather than another <c>PropertyChanged</c> notification, and raised HERE rather than
    /// subscribed from <c>AppEvents.SettingsChanged</c> directly, for one reason: this method is
    /// asynchronous — its menu build is a background disk scan — so SettingsChanged fires long before the
    /// page it changes has been re-rendered, and a re-fit driven off it would measure the previous
    /// layout. This is the first moment at which a measurement is meaningful.
    /// </para>
    /// <para>
    /// It therefore also fires on a plain summon (<see cref="HandleLauncherShownAsync"/> reloads) and on
    /// a manual 刷新, which is correct rather than merely tolerable: both can genuinely change the menu.
    /// A re-fit that lands on the geometry the window already has costs nothing and writes nothing —
    /// see <c>WindowPlacement.IsSamePlacement</c>.
    /// </para>
    /// </remarks>
    public event Action? ContentMetricsChanged;

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

    /// <summary>Re-label every group's tooltip and re-chunk its columns from the live config. Called
    /// after every (re)build and — via <see cref="ReloadAsync"/>'s "unchanged folders" branch — after
    /// every settings change too, which is what makes editing 每列个数 (GeneralConfig.ColButtonCount)
    /// re-chunk immediately without waiting for a folder rescan: <see cref="MenuGroupVm.SetColumnCapacity"/>
    /// is a no-op unless the value actually changed, so calling it unconditionally here costs nothing
    /// on every OTHER settings change.</summary>
    private void ApplyGroupSettings()
    {
        var tooltip = L("OpenFolderTooltip");
        var colButtonCount = _cfg.General.ColButtonCount;
        foreach (var group in Groups)
        {
            group.TitleTooltip = group.IsDirectoryClickable ? tooltip : null;
            group.SetColumnCapacity(colButtonCount);
        }
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
