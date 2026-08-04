using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;
using TanMenu.Wpf.Views;

namespace TanMenu.Wpf;

/// <summary>
/// The shell: the hand-drawn Aero caption (MainWindow.axaml's replaced
/// <c>Window.WindowDecorationsTheme</c>), the classic menu band and the page host. All launcher STATE
/// lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c>. This class owns what genuinely belongs to
/// the window — the backdrop and its opaque fallback, the caption's active/inactive glass
/// (<see cref="ApplyCaptionGlass"/>), close-to-tray, labelling the band's five items
/// (<see cref="ApplyMenuText"/>, ten writes — a header with its access-key marker and an accessible
/// name without it), dispatching all five commands (设置/主目录/刷新/退出/关于), the search box, and
/// kicking off the startup build + the fit-to-content resize that follows it, on EVERY summon
/// (<see cref="InitializeAndFitAsync"/>) — the actual geometry math, and the rule for when a manual
/// resize should win over a fresh fit, lives in <c>WindowHost</c>, which owns every other piece of
/// window placement too.
/// <para>
/// <b>ROUND 8, TASK 3 DELETED THE NAVIGATION PANE AND BROUGHT THE SEARCH BOX BACK HERE.</b> The pane
/// was a 160 DIP vertical strip with a <c>ListBox</c> and four command <c>Button</c>s; it is a
/// horizontal <c>Menu</c> now, so <c>ApplyRailText</c> became <see cref="ApplyMenuText"/> (same
/// subscription, same ten writes, different targets) and the two fit constants the pane owned —
/// <c>RailWidthDip</c> and <c>PaneBorderDip</c> — are deleted outright: the band is ABOVE the page, not
/// beside it, so <see cref="RefitToContent"/> hands back a chrome WIDTH of zero and the height term
/// moved into <c>WindowHost.ChromeHeightDip</c> as <c>TitleStripHeightDip + MenuBandHeightDip</c>.
/// </para>
/// <para>
/// <b>The search box is this class's again, and this is the second time it has moved.</b> Rounds 2-6
/// had it in the window's TITLE STRIP, which Round 7, Task 1 removed because <c>win-window-frames</c>
/// forbids a control in a window frame in as many words; it went to the 应用 page. Task 3 brings the
/// control — not the strip — into the menu band, which is inside the CLIENT area and is therefore the
/// "within the window instead" that same sentence asks for. Four members came with it unchanged:
/// <see cref="OnSearchFocusChanged"/>, <see cref="OnSearchClearPressed"/>,
/// <see cref="OnSearchKeyDown"/> and <see cref="FocusSearchBox"/>, which
/// <see cref="ConsumePendingFocus"/> now calls directly instead of forwarding to the page.
/// </para>
/// <para>
/// <b>ROUND 8, TASK 2 TOOK THE PAGE MODEL DOWN, and this class is where most of it lived.</b> 设置 was
/// the shell's second PAGE and is a modal dialog now (<c>Dialogs/OptionsWindow</c>), so there is nothing
/// left to navigate between: <c>ShowPage</c>, <c>ShowPageCore</c>, the <c>_inShowPage</c> reentrancy
/// flag and its 19-line remark, <c>OnNavSelectionChanged</c>, the <c>_navReady</c> guard, the
/// <c>_settingsPage</c> field and its lazy construction, and the pane's second <c>ListBox</c> all went
/// with it. <see cref="PageHost"/><c>.Content</c> is written exactly once, in this constructor, and
/// never again — which is what <see cref="RefitToContent"/>'s retained guard now asserts rather than
/// tests.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWindowHost _host;
    private readonly AppEvents _events;
    private readonly AppPage _appPage = new();

    /// <summary>The live subscription <see cref="ApplyBackdrop"/> creates when the backdrop was
    /// refused, kept so the repeated <c>OnOpened</c> of every summon reuses the one it already made
    /// instead of stacking a second, third and n-th observer on the same resource.</summary>
    private IDisposable? _backdropFallback;

    /// <summary>The live subscription <see cref="ApplyCaptionGlass"/> holds on whichever of the two
    /// caption gradients matches the window's current active state. Replaced (not stacked) on every
    /// activation change.</summary>
    private IDisposable? _captionGlass;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();

        // THE ONE AND ONLY WRITE TO PageHost.Content, anywhere, ever. It used to be assigned by
        // ShowPageCore on every navigation between 应用 and 设置; Round 8, Task 2 deleted that method
        // with the rest of the page model. Eager, not lazy, and not deferred to Opened: _appPage is a
        // field initializer (it holds the launcher's real state and is built before this constructor
        // body runs), and hosting it here is what makes RefitToContent's attachment guard true for the
        // whole life of the window instead of only between navigations.
        PageHost.Content = _appPage;

        // THE MENU BAND'S DataContext, and the only assignment of it. The search field moved into the
        // band in Round 8, Task 3 and its three property bindings (Filter, SearchPlaceholderText,
        // ClearFilterText) resolve against LauncherViewModel, which the WINDOW's own DataContext is
        // not — the window has none. MenuBand carries the matching x:DataType (MainWindow.axaml); the
        // two are a pair, and the compiled-binding build breaks loudly if either is missing.
        MenuBand.DataContext = _appPage.ViewModel;

        // AppLanguage/AppFonts have no change notification of their own, so both the menu band's five
        // item labels (their headers and their accessible names — see ApplyMenuText; this subscription
        // is what keeps the two in step through a 语言 change) and the window font are re-applied on
        // every SettingsChanged — the same imperative approach every settings card group uses for its
        // own labels (AppLanguage has no change notification there either).
        _events = App.Services.GetRequiredService<AppEvents>();
        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyMenuText;
        ApplyWindowFont();
        ApplyMenuText();

        // Round 7, Task 1: the Aero caption's glass has an ACTIVE and an INACTIVE gradient, and
        // WindowDrawnDecorations has no pseudo-class for either — its published set is :normal,
        // :maximized, :fullscreen, :has-shadow, :has-border, :has-titlebar, :has-maximize,
        // :has-fullscreen, :has-minimize (decompiled [PseudoClasses] attribute, Avalonia.Controls
        // 12.1.0) and there is no public path from the decorations to WindowBase.IsActive. So the
        // window publishes the chosen brush itself, into its own Resources, and the replaced template
        // reads it with {DynamicResource CaptionGlassBrush} — the same mechanism Services/AppTheme.cs
        // already uses for five accent keys. It reaches the decorations because they are a real
        // LOGICAL descendant of this window: TopLevel's constructor does
        // ((ISetLogicalParent)topLevelHost).SetParent(this) and LogicalChildren.Add(topLevelHost),
        // and WindowDrawnDecorations is added to TopLevelHost's own LogicalChildren.
        //
        // Wrapped in Avalonia.Reactive.AnonymousObserver rather than a fluent `.Subscribe(lambda)`:
        // IObservable<T> itself already declares Subscribe(IObserver<T>), and C# member lookup finds
        // that instance method first (an interface's own method of a given name is never shadowed by
        // an extension of the same name, so the extension overload some other Avalonia internals use
        // for this exact call shape — Avalonia.Reactive.Observable.Subscribe(IObservable<T>,Action<T>) —
        // is never even considered, and turned out to be internal to Avalonia.Base besides). This is
        // Subscribe(IObserver<T>) called directly, with AnonymousObserver adapting a plain Action<T> to
        // that interface — the one PUBLIC building block for exactly this.
        this.GetObservable(IsActiveProperty).Subscribe(new AnonymousObserver<bool>(_ => ApplyCaptionGlass()));

        _appPage.ViewModel.PropertyChanged += OnLauncherViewModelPropertyChanged;

        // A docked notice (the taskbar-pin InfoBar, the update banner) can open seconds after the
        // initial fit already ran — its own async check (a COM call, an HTTP request) can outlast it —
        // and Body's vertical scrolling is disabled, so a late notice would otherwise silently steal
        // height from it and re-wrap a category that used to fit in one column. Re-fit whenever either
        // one's visibility might have changed; RefitToContent's own guards (maximized, already
        // manually resized) apply here exactly as they do to every other call.
        _appPage.NoticeVisibilityChanged += OnNoticeVisibilityChanged;

        // The 每列个数 column count, 字体 and 语言 all change what the page measures too — this used to be
        // the 小/中/大 tile size before Round 3, Task 4 replaced it — and none of them used to re-fit the
        // window (review finding 2). The view model raises this once every rebuild has actually applied
        // them — see OnLauncherContentMetricsChanged for why this, and not AppEvents.SettingsChanged, is
        // the correct seam.
        _appPage.ViewModel.ContentMetricsChanged += OnLauncherContentMetricsChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyBackdrop();

        // The host owns show/hide/placement and needs the window once its HWND exists. Idempotent, and
        // App.ShowLauncherWindow has already attached on the cold-start path; kept so a window opened
        // by any other route is still owned. The DI registration deliberately hands out the same
        // instance for IWindowHost and WindowHost.
        //
        // RestorePlacement is deliberately NOT called here any more. It ran here until 0.12, which
        // meant the window was painted at the OS cascade position and then jumped ~30 ms later to its
        // restored geometry. App.ShowLauncherWindow now applies the placement between Attach and
        // Show(), i.e. before the window is ever on screen.
        (_host as WindowHost)?.Attach(this);

        // Must run AFTER the HWND exists and after Avalonia has applied the extended client area — see
        // RestoreSystemCloseCommand's own remarks. OnOpened re-fires on every un-hide, which is also
        // what keeps this correct if Avalonia re-runs ExtendClientArea between summons.
        RestoreSystemCloseCommand();

        // The startup build begins here, after the window is actually shown — never from AppPage's
        // own constructor, which runs earlier (as MainWindow's field initializer, before Show()) and
        // would make the first-summon gate in LauncherViewModel.InitializeAsync/HandleLauncherShownAsync
        // timing-dependent on whether the build finishes before Show() instead of strictly ordered
        // after it, as it was before this class existed. It also has to precede the cold start's
        // LauncherShown, which App raises once Show() has returned — see ShowLauncherWindow's remarks.
        // Fire-and-forget (as BeginInitialize's own call always was): InitializeAndFitAsync's inner
        // await is on LauncherViewModel.InitializeAsync's memoized Task, which swallows its own
        // exceptions (see InitializeCoreAsync's catch), so there is nothing here that can escape unobserved.
        _ = InitializeAndFitAsync();
    }

    /// <summary>
    /// Await the startup build, THEN fit the window to what it actually built — never the other way
    /// around. AppPage's natural size is meaningless before its categories exist: InitializeAsync's
    /// ReloadAsync is an async disk scan (see AppPage.BeginInitialize's own remarks for why the build
    /// cannot start any earlier than OnOpened), so a fit attempted before this would measure the splash,
    /// not the menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs the fit on EVERY entry, not only the process's first one — review fix: <c>OnOpened</c>
    /// re-enters on every later un-hide (<c>WindowHost.ShowAndActivate</c> calls <c>Show()</c> again
    /// whenever the window was hidden, and <c>Hide()</c>/<c>ShowCore</c> make that re-raise
    /// <c>Opened</c>), and a one-shot guard here used to skip every summon after the first — bringing
    /// back the very horizontal scrollbar this whole feature exists to remove, since
    /// <c>WindowHost.RestorePlacement</c> resets to a narrower placeholder on every un-hide regardless.
    /// <c>WindowHost.FitToContent</c> is what actually decides whether to skip a re-fit now (while
    /// maximized, or once the user has manually resized this process — see its own remarks), so this
    /// method can simply call it unconditionally every time. <c>BeginInitialize</c> itself is memoized,
    /// so every call after the first just awaits the already-completed Task.
    /// </para>
    /// <para>
    /// Round 3, Task 4 finding: the call used to be a direct, undeferred <c>RefitToContent()</c> here.
    /// On a genuine COLD START that measured the WRONG size — confirmed live via the shipped log, which
    /// showed this first fit reporting the pre-Task-4 page height (e.g. 2703 DIP for a 47-item category
    /// at the default ColButtonCount=10, the exact uncapped number) even though
    /// <c>LauncherViewModel.ReloadAsync</c> had already replaced <c>Groups</c> and recomputed every
    /// group's <c>Columns</c> (capped at ColButtonCount) synchronously before this method's own `await`
    /// resumed. A LATER re-fit — e.g. the one <c>OnLauncherContentMetricsChanged</c> queues at
    /// <see cref="DispatcherPriority.Loaded"/> from the SAME rebuild's own <c>ContentMetricsChanged</c> —
    /// measured correctly every time; only the direct, same-tick call here raced ahead of Avalonia
    /// actually re-templating the per-column <c>ItemsControl</c>s (<c>AppPage.axaml</c>'s
    /// `{Binding Columns}`) with their newly-capped item lists. Posting at the SAME
    /// <see cref="DispatcherPriority.Loaded"/> that <c>OnLauncherContentMetricsChanged</c> already uses
    /// closes the race: Avalonia processes pending layout invalidations before running Loaded-priority work
    /// regardless of how many such callbacks are queued, so both this call and the
    /// <c>ContentMetricsChanged</c>-triggered one now measure the SAME, already-current visual tree —
    /// and a fit that lands on the geometry already applied by an earlier one in the same batch is a
    /// free no-op (<see cref="WindowPlacement.IsSamePlacement"/>).
    /// </para>
    /// <para>
    /// That last claim — "a free no-op" — was FALSE for exactly the low-content-height regime this same
    /// Task 4 created (a small ColButtonCount, or the default's own natural height, both landing at or
    /// below the then-declared <c>MinHeight="568"</c>). <c>WindowHost.FitToContent</c> used to compare a
    /// fresh <c>ResolveContentFit</c> result against the window's LIVE Width/Height, and the window
    /// could not then render below 908 physical / 605.3 DIP outer (897 physical / 598 DIP client, at
    /// 150% scale) regardless of what was requested, so
    /// the live size Windows actually applied and the 568 DIP the fit had asked for never matched — the
    /// "free no-op" fired the whole Width/Height/Position/log/debounced-write path a second time on every
    /// cold start instead of skipping it (proof: the pre-fix log shows two <c>[INF] Window fit to
    /// content: 1724x568 DIP</c> lines 3 ms apart). Fixed by comparing against the PREVIOUS fit's own
    /// request instead of the live window size (post-fix: one <c>[INF]</c> fit plus one
    /// <c>[DBG] … nothing to apply</c>, 4 ms apart); the claim in this paragraph is accurate again as of
    /// that fix. Full log evidence:
    /// docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md.
    /// <em>The floor itself is gone as of Round 4, item 4</em>, which deleted <c>MinHeight</c> from
    /// <c>MainWindow.axaml</c> — it was an arithmetic consequence of that attribute, not a platform
    /// constant; see <c>WindowHost.MinWindowHeightDip</c>. So a request is no longer clamped and the
    /// live size would agree with it again. The request-based basis stays regardless: it is what "the
    /// same placement" has to mean, and Round 4 widened it from a size to the whole rectangle.
    /// </para>
    /// </remarks>
    private async Task InitializeAndFitAsync()
    {
        await _appPage.BeginInitialize();
        Dispatcher.UIThread.Post(RefitToContent, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Re-run <c>WindowHost.FitToContent</c> against the launcher's current natural size — unless
    /// <see cref="_appPage"/> is not what <see cref="PageHost"/> is hosting, in which case this does
    /// NOTHING AT ALL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called after the startup build finishes (<see cref="InitializeAndFitAsync"/>, on every summon)
    /// and again from every other event that changes what the page measures: a docked notice
    /// opening/closing (<see cref="OnNoticeVisibilityChanged"/>), a 每列个数 / 字体 / 语言 change
    /// (<see cref="OnLauncherContentMetricsChanged"/>), and the Maximized→Normal transition
    /// (<see cref="OnPropertyChanged"/>). All of them are the exact same operation, so there is one place
    /// that composes the shell's chrome width and calls through. (Round 4, item 1 removed a trigger —
    /// the rail's expand/collapse toggle — by removing the state it compensated for; Round 8, Task 2
    /// removed another, the 设置 → 应用 switch, by removing the switch. Four remain.)
    /// </para>
    /// <para>
    /// <b>THE GUARD IS RETAINED VERBATIM, AND WHAT IT MEANS HAS CHANGED.</b> It was a PAGE TEST — "is
    /// 设置 on screen" — and it is now an ATTACHMENT INVARIANT: "<see cref="_appPage"/> is hosted,
    /// therefore styled, therefore measurable". After Round 8, Task 2 <see cref="PageHost"/><c>.Content</c>
    /// has exactly ONE writer, this window's constructor, so the comparison is a constant true and the
    /// defect it was added for is unreachable.
    /// </para>
    /// <para>
    /// <b>It is kept anyway, and that is not defensive tidiness.</b> It costs one reference comparison,
    /// and it is the literal line whose removal produced a REPRODUCED failure: with this guard
    /// temporarily deleted, measuring <see cref="_appPage"/> while 设置 was the hosted page latched the
    /// window at a wrong 850x568 DIP for the rest of the session — 568 being the app's own minimum
    /// height, declared on <c>MainWindow.axaml</c> at the time and moved to
    /// <c>WindowHost.MinWindowHeightDip</c> by Round 4, item 4, i.e. a measurement that had collapsed
    /// onto the floor. Deleting the line now converts a documented, reproduced failure into a latent one
    /// that returns the instant anything ever reassigns <c>PageHost.Content</c> — and Round 8 adds a
    /// second window that owns page-like content (<c>Dialogs/OptionsWindow</c>), which is precisely when
    /// that becomes plausible again. The durable record of the reproduction is
    /// docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md (this paragraph previously
    /// cited "final-fix-report.md, finding 1," a file that did not exist at the time it was written).
    /// </para>
    /// <para>
    /// <b>Rejected alternative:</b> replacing it with <c>((Visual)_appPage).GetVisualRoot() is null</c>.
    /// It is a truer attachment test, but it changes behaviour in a case nobody has measured — whether a
    /// hidden Avalonia window's content still reports a visual root — and the fit is the one subsystem
    /// in this app with a reproduced regression history. Not worth it for zero functional gain.
    /// </para>
    /// <para>
    /// The mechanism paragraphs below are UNCHANGED and still apply: they are what makes a measurement
    /// taken on a detached page wrong, and therefore what the invariant is protecting.
    /// </para>
    /// <para>
    /// The MECHANISM, restated in Round 3's residual review after the previous wording was found to
    /// contradict the file it cited, and this time traced end-to-end through decompiled Avalonia
    /// 12.1.0 rather than inferred. <c>ContentControl</c> removes the outgoing content from
    /// <c>LogicalChildren</c> synchronously when <see cref="PageHost"/>'s Content is reassigned;
    /// <c>StyledElement.OnDetachedFromLogicalTreeCore</c> then calls <c>InvalidateStyles(recurse:
    /// false)</c> on each element and recurses the subtree itself. <c>InvalidateStyles</c> does exactly
    /// two things: <c>ValueStore.RemoveFrames(FrameType.Style)</c> and <c>_stylesApplied = false</c>.
    /// <c>FrameType</c> is a three-value enum (<c>Style</c>, <c>TemplatedParentTheme</c>,
    /// <c>Theme</c>) and <c>RemoveFrames</c> filters on <c>FramePriority.IsType</c>, i.e.
    /// <c>(int)priority % 3 == (int)type</c> — so it removes STYLE frames only. A ControlTheme applied
    /// through an element-local <c>Theme</c> value is installed as a <c>FrameType.Theme</c> frame
    /// (<c>ApplyControlTheme(theme, FrameType.Theme)</c>, reached because <c>GetEffectiveTheme</c>
    /// returns the local <c>Theme</c> before it ever consults the implicit one), and nothing on the
    /// detach path removes it: theme frames are dropped only by <c>OnControlThemeChanged</c> (the
    /// <c>Theme</c> property itself changing) and by <c>ReevaluateImplicitTheme</c>, which runs on
    /// ATTACH and short-circuits for an element that has a local <c>Theme</c>. <c>_themeApplied</c> is
    /// likewise not cleared.
    /// </para>
    /// <para>
    /// So the previous wording here was wrong in both halves and is corrected. What survives a detach
    /// is the tile's ControlTheme: <c>TmTileButton</c> (<c>Themes/Shell/Launcher.axaml</c>) is applied
    /// element-locally as <c>Theme="{StaticResource TmTileButton}"</c> in <c>Views/AppPage.axaml</c>, so
    /// its Setters are a theme frame and the detach does not touch them. What IS lost is every
    /// APP-LEVEL, class-scoped style frame the page depends on, which is plenty to corrupt a
    /// measurement: <c>Image.tm-tile-icon</c>'s 20x20 icon box, <c>TextBlock.tm-section</c>'s size,
    /// ink and alignment, <c>Border.tm-section-head</c>'s padding, <c>StackPanel.tm-group</c>'s
    /// alignment, the search field's whole geometry, and so on. (That enumeration used to read
    /// "<c>TextBlock.tm-section</c>'s size/line-height/weight". Neither survives: Round 7, Task 7
    /// deleted the LineHeight keys with Windows 11's ramp, and Round 8, Task 8 deleted the SemiBold.
    /// Re-derived against the tree this session — the Style sets exactly FontSize, Foreground and
    /// VerticalAlignment.) Which of those produced 850x568
    /// specifically was not isolated, and is not claimed here — the guard is justified by the
    /// reproduction, and the mechanism above is stated only as far as the decompile actually supports
    /// it.
    /// </para>
    /// <para>
    /// Round 4, item 3 made this guard MORE load-bearing, not less, and inverted the reassurance the
    /// paragraph above used to end on. Until then the surviving theme frame carried a fixed
    /// <c>Width</c> Setter (168 DIP), so the page's WIDTH was the one dimension a detached measurement
    /// could still get right. That Setter is deleted: a tile is now as wide as its own content, and one
    /// of that content's two terms is the very icon box listed above as lost —
    /// <c>Image.tm-tile-icon</c>'s 20x20 is class-scoped, so a detached tile no longer has a 20 DIP icon
    /// slot in its width at all. The other term is the label's own advance, and the face it is measured
    /// in is not set on the label either: <see cref="ApplyWindowFont"/> sets it on the WINDOW, and a
    /// page sitting outside the window's logical tree is not below that window to take it from. A fit
    /// taken while the page is detached would therefore now corrupt width as well as height.
    /// </para>
    /// <para>
    /// A SECOND, worse-sounding claim does NOT belong here as established fact: that the launcher then
    /// renders UNSTYLED for the rest of the session. On paper that is mechanistically plausible —
    /// <c>Layoutable.MeasureCore</c> calls <c>ApplyStyling()</c>, and <c>StyledElement.ApplyStyling</c>
    /// sets <c>_stylesApplied = true</c> UNCONDITIONALLY even though nothing can match while
    /// <c>StylingParent</c> is null, and the page's own later <c>OnAttachedToLogicalTreeCore</c> gates its
    /// re-styling on <c>_logicalRoot == null</c>, so its own <c>ApplyStyling()</c> call there sees the
    /// flag already set and no-ops — traced in the same decompile. But it was NOT observed as such: in
    /// both live pre-fix reproductions the launcher looked normal again once it was reattached to 应用.
    /// The evidence recorded for that at the time was "the tiles came back at their correct width (192
    /// DIP)". Round 3's residual review retired that piece of evidence on the ground that tile width
    /// came from <c>TmTileButton</c>'s <c>Width</c> Setter — a theme frame the detach never removed —
    /// so a correct width proved nothing about STYLE frames. Round 4, item 3 had to re-check that
    /// ground and found it wrong twice over, so it is withdrawn rather than repeated: 192 DIP was
    /// <c>Panel.size-medium Button.tm-tile</c>, a CLASS-SCOPED style (verified against the tree at
    /// <c>921d7f7</c>, before <c>2af2401</c> collapsed the three size classes into one Setter), so the
    /// reason did not describe the build the observation was made on; and item 3 deleted the Setter, so
    /// it does not describe today's build either. What remains true is the narrow point the paragraph
    /// rests on: the launcher LOOKED right on reattach in both reproductions. Nothing here converts
    /// that into a measurement of whether style frames were re-applied, and no one has re-observed it
    /// on a build whose tile width is style-dependent again.
    /// A candidate explanation also recorded at the time —
    /// <see cref="TanMenu.Wpf.ViewModels.LauncherViewModel.ReloadAsync"/> replaces the tile containers
    /// outright when a rebuild reports a change and only relabels the existing ones otherwise, so some
    /// runs hand back fresh, correctly-styled containers — remains a candidate and was never confirmed.
    /// Treat the sizing damage as confirmed and the un-styling as a live risk this same guard happens
    /// to close, not as an observed fact and not as a ruled-out one.
    /// </para>
    /// <para>
    /// A paragraph is DELETED here rather than left standing, because Round 8, Task 2 falsified it: it
    /// said the tray's 选项 entry reached the sizing defect above, through
    /// <c>SettingsLauncher.OpenCore</c> navigating to 设置 and THEN calling <c>ShowAndActivate</c>,
    /// whose <c>Show()</c> re-raises <c>Opened</c> and continues into this method with the launcher
    /// detached. <c>OpenCore</c> no longer navigates — it opens a modal dialog — and a modal
    /// <c>ShowDialog</c> does not reparent its owner's content, so that route to the defect does not
    /// exist any more.
    /// </para>
    /// <para>
    /// Same test, and for the same underlying reason, as <see cref="ConsumePendingFocus"/>'s own guard.
    /// </para>
    /// </remarks>
    private void RefitToContent()
    {
        if (!ReferenceEquals(PageHost.Content, _appPage))
            return;

        (_host as WindowHost)?.FitToContent(_appPage, ChromeWidthDip);
    }

    /// <summary>
    /// Everything BESIDE the page that still counts toward the window's own Width. It is <b>zero</b>,
    /// and it has been since Round 8, Task 3: the shell's chrome runs horizontally now — a menu band
    /// ABOVE the page — so nothing is laid out to the page's left or right and the window's width IS
    /// the content pane's width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was <c>RailWidthDip + PaneBorderDip</c> = 160 + 1 = 161, two constants that are DELETED with
    /// their subjects: the 160 DIP navigation pane, and the 1 DIP LEFT edge of the 应用 pane's
    /// <c>BorderThickness="1,1,0,0"</c>, which existed to separate the page from that pane and which
    /// <c>Border.tm-page-pane</c> (Themes/Shell/Launcher.axaml) no longer sets. The vertical twin of
    /// that same border, the <c>+1</c> inside <c>WindowHost.ChromeHeightDip</c>, went in the same
    /// commit for the same reason.
    /// </para>
    /// <para>
    /// <b>Kept as a named constant rather than written as a literal <c>0</c> at the call site.</b> The
    /// term is a real part of the fit contract — <c>WindowHost.FitToContent</c> takes it as a parameter
    /// and every one of the 23 <c>WindowPlacementTests</c> parameterises it — and a bare zero there
    /// would read as "no chrome exists" rather than as "this shell's chrome is above the page, and the
    /// number that says so lives here". It is also the one place a future horizontal element would
    /// have to be added back.
    /// </para>
    /// </remarks>
    private const double ChromeWidthDip = 0;

    /// <summary>See the constructor's own remarks on subscribing to this — WindowHost.FitToContent's
    /// guards (maximized, already manually resized) decide whether this re-fit actually does anything.</summary>
    private void OnNoticeVisibilityChanged() => RefitToContent();

    /// <summary>
    /// The launcher finished a (re)build, so anything derived from settings that changes what it
    /// MEASURES has already been applied to it: each group's column layout (每列个数,
    /// <c>GeneralConfig.ColButtonCount</c> — Round 3, Task 4 replaced the 小/中/大 tile-size class this
    /// used to name, and Round 4, item 3 then removed the fixed width that replaced it, so no setting
    /// names a tile width any more: a tile is as wide as its own label), the 字体 face, and every
    /// localized string on the page.
    /// <para>
    /// The 字体 face is doubly relevant since item 3: it no longer only re-flows text inside a tile of
    /// known width, it decides the tile's WIDTH, and through it the whole page's. That is another
    /// reason this signal — raised after the rebuild, not off <c>SettingsChanged</c> — is the only
    /// valid moment to re-measure.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Review finding 2: none of those re-fitted the window, so a settings change could re-render the
    /// page without the window ever catching up to its new size — originally observed as choosing 大
    /// leaving a horizontal scrollbar and 小 leaving dead space, back when 小/中/大 was the setting in
    /// question; the same risk applies to 每列个数 today. Hooking the view model's own post-rebuild signal
    /// rather than <c>AppEvents.SettingsChanged</c> directly is what makes the measurement valid:
    /// <c>LauncherViewModel.HandleSettingsChangedAsync</c> is asynchronous (its rebuild is a background
    /// disk scan), so a re-fit driven straight off SettingsChanged would measure the PREVIOUS layout.
    /// Posted at <see cref="DispatcherPriority.Loaded"/> — one below layout — so the column layout has
    /// actually been re-measured with the new count before the window is sized to it.
    /// </remarks>
    private void OnLauncherContentMetricsChanged() =>
        Dispatcher.UIThread.Post(RefitToContent, DispatcherPriority.Loaded);

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyMenuText;
        _appPage.ViewModel.PropertyChanged -= OnLauncherViewModelPropertyChanged;
        _appPage.ViewModel.ContentMetricsChanged -= OnLauncherContentMetricsChanged;
        _appPage.NoticeVisibilityChanged -= OnNoticeVisibilityChanged;
        _captionGlass?.Dispose();
        _backdropFallback?.Dispose();

        // AppPage's view model is disposed here, not on the page's own Unloaded: OnClosed only fires
        // at real application exit (OnClosing below cancels every other close), while the app page
        // itself is a field hosted for the window's whole life.
        //
        // Round 8, Task 2 deleted the line that used to follow — _settingsPage?.Dispose(). 设置 is a
        // modal dialog now and it unsubscribes itself in its own OnClosed, per open.
        _appPage.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// The window's two keys: Ctrl+E (Round 7, Task 9 — see the comment on the branch itself) and
    /// Escape.
    /// <para>
    /// Escape reaches here only as a FALL-THROUGH, from focus that is neither in the search box nor
    /// anywhere inside <see cref="AppPage"/>: <see cref="OnSearchKeyDown"/> and
    /// <c>AppPage.OnKeyDown</c> — the latter for the whole page, not only its tile grid — both call
    /// <c>HandleSearchEscape</c> and mark the key handled first, matching the clear-then-hide behaviour
    /// the launcher always had. Before the rail
    /// existed, this window's own OnKeyDown handled Escape identically regardless of where focus was —
    /// calling the SAME view-model method here (rather than an unconditional Hide) keeps that behaviour
    /// rather than merely approximating it.
    /// </para>
    /// <para>
    /// <b>A menu-band <c>MenuItem</c> is NOT one of the cases that fall through, and this sentence
    /// replaces one that said it was.</b> The example named here until the Round 8, Task 3 review was
    /// "a navigation-pane row", a control Task 3 deleted; the band is not a drop-in replacement for it,
    /// because Avalonia closes the menu on Escape itself. Decompiled, Avalonia.Controls 12.1.0,
    /// <c>DefaultMenuInteractionHandler.KeyDown</c>: the <c>Key.Escape</c> case closes the parent and
    /// sets <c>e.Handled = true</c> unconditionally, and the handler is subscribed to the
    /// <c>Menu</c>'s own KeyDown, which the event reaches while bubbling out of the item and before it
    /// reaches this window. Ctrl+E is the opposite case and the branch below says why.
    /// <b>MEASURED on the live app in the Task 3 review, not only decompiled</b>: synthetic Alt put
    /// focus on <c>MenuOptions</c>, a synthetic Escape then left focus on the band AND LEFT THE
    /// LAUNCHER VISIBLE. That is the decisive half — the filter was empty, so had this branch run,
    /// <c>HandleSearchEscape</c> would have taken the <c>_host.Hide()</c> arm and the window would
    /// have gone away.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>ROUND 8, TASK 2 COLLAPSED BOTH BRANCHES TO THE SINGLE-PAGE CASE.</b> Escape had an
    /// <c>else _host.Hide()</c> for 设置, on the ground that <c>HandleSearchEscape</c> is two-stage (a
    /// non-empty filter is cleared and the window stays up) and on 设置 the filter belonged to a page the
    /// user could not see, so the first Escape would have been spent clearing something invisible.
    /// Ctrl+E had a matching page guard, for the mirror-image reason. There is one page now, so
    /// <b>Escape always routes to <c>HandleSearchEscape</c></b> — which is what the launcher did before
    /// the rail existed — and Ctrl+E always focuses the search box.
    /// <para>
    /// <b>A SENTENCE HERE USED TO SAY "Escape inside the Options DIALOG is not this window's business
    /// at all: that dialog is modal and its own 取消 button carries <c>IsCancel</c>." THAT IS FALSE
    /// AND THE ROUND 8 REVIEW MEASURED IT FALSE.</b> Avalonia routes a key to the FOCUSED ELEMENT, not
    /// to the window whose HWND the message arrived on, and the dialog opened from THIS band by
    /// keyboard can leave the focus on the band — so an Escape aimed at the dialog ran this method,
    /// took the <c>_host.Hide()</c> arm of <c>HandleSearchEscape</c> and hid the launcher out from
    /// under its own modal, which does not close the modal but strands it invisible. The correction is
    /// NOT here: <c>WindowHost.Hide</c> is now a no-op while a <c>SuppressHide</c> is in force, which
    /// covers this route and the other two callers that could take it. That method's remarks carry the
    /// measurement.
    /// </para>
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        // Ctrl+E — the search accelerator. PUBLISHED,
        // https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-search-boxes: "Assign the
        // keyboard shortcut for the Search box to be Ctrl+E." (Round 7, Task 9.)
        //
        // It lives HERE rather than in AppPage.OnKeyDown because an accelerator that only works when
        // focus is already inside the page it belongs to is not an accelerator. The live case for that
        // is the menu band, which is a SIBLING of the page rather than a descendant — UIA shows
        // MainMenu, SearchBox and the page's own element as three children of this window — so Ctrl+E
        // pressed on a band MenuItem bubbles to this window and never to AppPage. (It was "a
        // navigation-pane row" until the Round 8, Task 3 review: same argument, deleted subject.)
        //
        // The band lets this key through where it swallows Escape, and that asymmetry is REAL but is
        // asserted from the DECOMPILE ALONE — say so rather than imply it was driven. In
        // DefaultMenuInteractionHandler.KeyDown, Key.E reaches the default arm, where
        // NavigationDirectionExtensions.ToNavigationDirection maps only Tab/arrows/Home/End/PageUp/
        // PageDown and returns null for a letter, so e.Handled is left alone; Key.Escape has its own
        // arm and sets e.Handled unconditionally. The Escape half of that WAS confirmed on the live
        // app (see the summary above). THE CTRL+E HALF COULD NOT BE, and not for want of trying: this
        // machine has no real keyboard, and the PostMessage instrument every round uses cannot express
        // a MODIFIER at all, because Avalonia's Win32 backend derives KeyModifiers from the live key
        // state that PostMessage never updates. Driven from a band item AND, as a control, from inside
        // the page where this branch is not in dispute, synthetic Ctrl+E moved focus in neither case —
        // which measures the instrument, not this code.
        //
        // The request goes through
        // PendingFocus like every other one, so ConsumePendingFocus keeps owning the timing.
        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            _appPage.ViewModel.PendingFocus = true;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        _appPage.ViewModel.HandleSearchEscape();
        e.Handled = true;
    }

    /// <summary>
    /// Re-fit the window once when the user un-maximizes it, and re-assert the system menu's Close
    /// command whenever the window state changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Review finding 3: <c>WindowHost.FitToContent</c> returns early unless the window is in the Normal
    /// state — correctly, since there is no restored geometry on screen to measure against while
    /// maximized — but NOTHING re-triggered a fit when the user came back out of it, so a window that
    /// started maximized (<c>GeneralConfig.WindowMaximized</c>) restored to the 1000x688 placeholder and
    /// stayed there for the session. Round 2's plan asked for the opposite: "a maximized window should
    /// still restore maximized, and fit-to-content applies to the restored (normal) size." This is the
    /// one transition where that becomes possible, and it needs no Maximized → Normal → Maximized state
    /// toggle — the very flicker the early return was avoiding — because the window is already Normal by
    /// the time this runs. Posted at <see cref="DispatcherPriority.Loaded"/> so the restored bounds have
    /// been laid out before they are measured and replaced.
    /// </para>
    /// <para>
    /// The Close re-enable rides along here because Avalonia's Win32 backend re-runs
    /// <c>ExtendClientArea()</c> — which is what disables <c>SC_CLOSE</c> — from its own window-state
    /// handling, so a state change is exactly when the grey-out can come back. See
    /// <see cref="RestoreSystemCloseCommand"/>.
    /// </para>
    /// </remarks>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty)
            return;

        RestoreSystemCloseCommand();

        if (change.GetOldValue<WindowState>() == WindowState.Maximized &&
            change.GetNewValue<WindowState>() == WindowState.Normal)
            Dispatcher.UIThread.Post(RefitToContent, DispatcherPriority.Loaded);
    }

    /// <summary>Acrylic is a request, not a guarantee: Windows 10 before 10.0.15063, "show
    /// transparency" turned off and remote desktop all refuse it, and the window would then be a hole
    /// rather than a surface. Paint the opaque surface colour when it was not granted. (Same guard
    /// SettingsWindow used.)</summary>
    /// <remarks>
    /// <para>
    /// Round 7, Task 1 moved the requested level from <c>Mica</c> to <c>AcrylicBlur</c>
    /// (MainWindow.axaml) — Aero's glass was a live blur of what is behind the window, and Mica is a
    /// wallpaper tint that never blurs — and THIS GUARD HAD TO MOVE WITH IT. Left testing for
    /// <c>Mica</c>, a perfectly successful acrylic window would fall through, subscribe the fallback,
    /// and paint the opaque <c>SurfaceBaseBrush</c> over a working backdrop with no error anywhere:
    /// the conversion would fail silently and look like the acrylic was refused.
    /// </para>
    /// <para>
    /// The log line is deliberately unconditional and at Information. Whether this machine grants
    /// acrylic could not be established read-only before the conversion, and every later task that
    /// touches the window surface depends on the answer; a log fact can be taken from a shipped run
    /// by anyone, an in-process read cannot.
    /// </para>
    /// <para>
    /// Round 4, item 5 replaced a one-shot <c>FindResource</c> + assignment with this subscription. The
    /// old form resolved the brush OBJECT once and assigned it, which made the fallback a snapshot of
    /// whichever <see cref="Avalonia.Styling.ThemeVariant"/> was live at the first
    /// <see cref="OnOpened"/>: <see cref="AppTheme.Apply"/> — which the settings window calls on every
    /// 外观 change — only writes <c>Application.RequestedThemeVariant</c>, and nothing re-ran this, so a
    /// backdrop-refused window kept the previous variant's surface for the rest of the process.
    /// Subscribing to the resource instead makes it follow the variant.
    /// </para>
    /// <para>
    /// The <c>?? Brushes.White</c> is carried over verbatim from the old form: a missing key would
    /// otherwise leave <c>Background</c> null, i.e. a window that paints nothing at all — the exact
    /// failure the fallback exists to prevent.
    /// </para>
    /// </remarks>
    private void ApplyBackdrop()
    {
        Serilog.Log.Information(
            "Backdrop: requested {Requested}, granted {Granted}",
            string.Join(", ", TransparencyLevelHint), ActualTransparencyLevel);

        if (ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur)
            return;

        _backdropFallback ??= this.GetResourceObservable("SurfaceBaseBrush")
            .Subscribe(new AnonymousObserver<object?>(v => Background = v as IBrush ?? Brushes.White));
    }

    /// <summary>
    /// Publish the caption's glass gradient for the window's CURRENT active state, into this window's
    /// own <see cref="Avalonia.Controls.Window.Resources"/> under the key the replaced
    /// <c>WindowDrawnDecorations</c> template reads with <c>{DynamicResource CaptionGlassBrush}</c>.
    /// </summary>
    /// <remarks>
    /// A resource OBSERVABLE rather than a one-shot <c>FindResource</c>, for exactly the reason
    /// <see cref="ApplyBackdrop"/>'s own remarks give: the active and inactive gradients are themed, so
    /// resolving one object once would freeze the caption on whichever variant happened to be live at
    /// the time and a later 外观 change would not reach it. Re-subscribing on every activation change is
    /// what switches WHICH key is being followed; the subscription itself then keeps following that key
    /// across variant changes. The previous subscription is disposed first, so a session's worth of
    /// focus changes leaves one observer, not n.
    /// </remarks>
    private void ApplyCaptionGlass()
    {
        var key = IsActive ? "CaptionGlassActiveBrush" : "CaptionGlassInactiveBrush";

        // Information, not Debug, and for the same reason ApplyBackdrop's line is: the two gradients
        // differ only in alpha, so which one is on screen is not readable from a screenshot with any
        // confidence, and on a machine with no keyboard or mouse the window can never be activated to
        // put the active one there at all. The log is the only instrument that can say which key was
        // published. It fires once per activation change, which is not chatty.
        Serilog.Log.Information("Caption glass: IsActive {IsActive}, publishing {Key}", IsActive, key);

        _captionGlass?.Dispose();
        _captionGlass = this.GetResourceObservable(key)
            .Subscribe(new AnonymousObserver<object?>(PublishCaptionGlass));
    }

    /// <summary>
    /// Write one resolved gradient into <c>Resources["CaptionGlassBrush"]</c> — but only when it is not
    /// already the value sitting there.
    /// </summary>
    /// <remarks>
    /// THE IDENTITY CHECK IS LOAD-BEARING AND IS NOT DEFENSIVE TIDINESS. Assigning into
    /// <see cref="Avalonia.Controls.Window.Resources"/> raises <c>ResourcesChanged</c>, which
    /// re-evaluates every live resource observable on this window — including the one this method is
    /// the observer for. An unconditional write is therefore an unbounded feedback loop, and it really
    /// was one: the first build of this task died on launch with <c>Stack overflow</c> whose frames read
    /// <c>ResourceNodeExtensions.TryFindResource → Styles.TryGetResource → ResourceDictionary
    /// .TryGetResource</c> under <c>AnonymousObserver&lt;bool&gt;.OnNext</c> inside
    /// <c>MainWindow..ctor</c>, before the window ever reached the screen. With the check, the second
    /// pass is handed the same brush INSTANCE, finds nothing to do, and the cascade stops after one
    /// round trip.
    /// <para>
    /// A null is ignored rather than written: blanking the key would leave the band unpainted, and
    /// Tokens.axaml's own <c>CaptionGlassBrush</c> — the inactive gradient — is the right thing to keep
    /// on screen if a lookup ever fails.
    /// </para>
    /// </remarks>
    private void PublishCaptionGlass(object? value)
    {
        if (value is null)
            return;

        if (Resources.TryGetValue("CaptionGlassBrush", out var current) && ReferenceEquals(current, value))
            return;

        Resources["CaptionGlassBrush"] = value;
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
    // ROUND 8, TASK 2 DELETED THE PAGE MODEL HERE — 167 lines: ShowPage, ShowPageCore,
    // OnNavSelectionChanged, and the _inShowPage / _navReady flags with their remarks.
    //
    // The shell had two PAGES, 应用 and 设置, and every one of those members existed to swap between
    // them and to keep the pane's two ListBoxes acting like one selection group. 设置 is a modal
    // dialog now (Dialogs/OptionsWindow, opened by ISettingsLauncher), so there is nothing to swap:
    // PageHost.Content is assigned once, in the constructor, and the pane's second ListBox
    // (NavFooterRail) is gone with the 设置 row's promotion to a plain command Button.
    //
    // Three facts those deleted members recorded are kept here rather than lost with them, because
    // each is a reason a future edit must not re-introduce the shape:
    //   · SelectedIndex assignment raises SelectionChanged SYNCHRONOUSLY (decompiled
    //     Avalonia.Controls.Primitives.SelectingItemsControl), which is why a navigation that arrived
    //     without a preceding rail click used to re-enter the swap while the outer call was still on
    //     the stack — the defect _inShowPage existed to close.
    //   · A plain ListBox enforces "at most one selected item" only WITHIN itself, never across a
    //     second ListBox instance, which is why keeping 应用 and 设置 mutually exclusive took an
    //     explicit write to both.
    //   · The "app" class on PagePane was flipped from ShowPageCore. Round 8, Task 3 deleted the
    //     class itself along with the pane, so Launcher.axaml has ONE Border.tm-page-pane rule and
    //     the markup states no class at all.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Label the menu band's five items: five <c>Header</c>s and five <c>AutomationProperties.Name</c>s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is <c>ApplyRailText</c> renamed and re-aimed by Round 8, Task 3</b> — same subscription
    /// (<c>AppEvents.SettingsChanged</c>, hooked in the constructor and released in
    /// <see cref="OnClosed"/>), same ten writes, and the same reason it is imperative at all:
    /// <c>AppLanguage</c> has no change notification, so nothing can bind to it. What changed is the
    /// targets — five <c>MenuItem</c>s where five navigation-pane rows used to be — and the strings,
    /// which now come in PAIRS.
    /// </para>
    /// <para>
    /// <b>THE HEADER CARRIES THE ACCESS-KEY MARKER AND THE ACCESSIBLE NAME DOES NOT, and that split is
    /// measured rather than assumed.</b> The vendored menu template opts into markers with
    /// <c>RecognizesAccessKey="True"</c> on <c>PART_HeaderPresenter</c>
    /// (Themes/Aero/ControlStyles/Menu.axaml), and Avalonia's <c>AccessText</c> strips the <c>_</c>
    /// before it draws — but a UIA name derived from the raw header string keeps it. Round 8, Task 2
    /// measured exactly that on the Options dialog's 应用 button, whose UIA <c>Name</c> read literally
    /// "应用(_A)" until it was given an explicit name, and this method writes the same explicit pair for
    /// the same reason. The two keys per item are separate <c>AppLanguage</c> entries rather than one
    /// string with the marker spliced in by code, because WHERE the marker goes is a localisation
    /// decision: English underlines a letter inside the word and Chinese appends the Latin letter in
    /// parentheses, which is what Windows' own Chinese UI does.
    /// </para>
    /// <para>
    /// Access keys: <b>O</b> = Options, <b>x</b> = Exit and <b>A</b> = About are PUBLISHED,
    /// <c>inter-keyboard</c>'s standard table. <b>M</b> for 主目录 and <b>R</b> for 刷新 are
    /// app-specific — the guide assigns neither — with M taken on its own stated preference for wide
    /// letters "such as w, m". All five are distinct, which
    /// <c>MenuBarTests.AccessKeysAreDistinct</c> holds.
    /// </para>
    /// </remarks>
    private void ApplyMenuText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;

        MenuOptions.Header = AppLanguage.Text("MenuOptionsWithAccessKey", lang);
        MenuOpenRoot.Header = AppLanguage.Text("MenuOpenRootWithAccessKey", lang);
        MenuRefresh.Header = AppLanguage.Text("MenuRefreshWithAccessKey", lang);
        MenuExit.Header = AppLanguage.Text("MenuExitWithAccessKey", lang);
        MenuAbout.Header = AppLanguage.Text("MenuAboutWithAccessKey", lang);

        MenuOptions.SetValue(AutomationProperties.NameProperty, AppLanguage.Text("MenuOptions", lang));
        MenuOpenRoot.SetValue(AutomationProperties.NameProperty, AppLanguage.Text("MenuOpenRoot", lang));
        MenuRefresh.SetValue(AutomationProperties.NameProperty, AppLanguage.Text("MenuRefresh", lang));
        MenuExit.SetValue(AutomationProperties.NameProperty, AppLanguage.Text("MenuExit", lang));
        MenuAbout.SetValue(AutomationProperties.NameProperty, AppLanguage.Text("About", lang));
    }

    /// <summary>The 字体 setting used to reach the window through a DataContext binding. The shell has
    /// no view model of its own now, so it is applied imperatively — and re-applied on SettingsChanged,
    /// because AppLanguage/AppFonts have no change notification.</summary>
    private void ApplyWindowFont()
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        FontFamily = AppFonts.Resolve(cfg.Config.General.FontFamily);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The search box, and the deferred focus that reaches it
    //
    // THE CONTROL IS BACK IN THIS CLASS AS OF ROUND 8, TASK 3, and that is not a revert of Round 7,
    // Task 1. Task 1 removed the search box from the window's TITLE STRIP, because Microsoft's rule
    // for a window frame is verbatim "Don't add controls to a window frame. Put the controls within
    // the window instead." (learn.microsoft.com/en-us/windows/win32/uxguide/win-window-frames) and
    // the Aero caption IS that frame. Task 3 puts the field in the MENU BAND, which is below the
    // caption, inside the client area — i.e. exactly the "within the window" half of that same
    // sentence. Item 5 asked for it there, immediately after 关于.
    //
    // What did NOT come back is the strip's Task-Manager-proportional width: ApplySearchFieldWidth,
    // its three constants and the ClientSize subscription that drove it stay deleted, and the field
    // keeps the one authored 320 DIP width Task 1 gave it. A proportional width would need a
    // ClientSize subscription and therefore a re-fit trigger, and Round 8 has four, not five.
    //
    // Four members moved here verbatim with the markup — OnSearchFocusChanged, OnSearchClearPressed,
    // OnSearchKeyDown and FocusSearchBox — plus the PendingFocus contract this class already owned:
    // the view model raises "please focus the search box" from a fresh summon, from 清除筛选 and from
    // Up off the top of the tile grid, and ConsumePendingFocus owns the timing. It calls
    // FocusSearchBox() directly now instead of forwarding to AppPage.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Deferred focus: <see cref="LauncherViewModel.PendingFocus"/> is set on every summon (and
    /// by AppPage's own "清除筛选"/Up-off-the-grid actions) and consumed here, on the next Loaded pass —
    /// one priority below layout, so the search box has already been measured and arranged by the time
    /// this runs.</summary>
    private void OnLauncherViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LauncherViewModel.PendingFocus))
            return;

        Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Consume a deferred focus request, if the page whose view model raised it is actually hosted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard was added as a PAGE test (Task 1 review, Important 3): a focus request arriving while
    /// 设置 was the active page must not steal focus into a search box filtering a page the user cannot
    /// see — the tray's 选项 entry navigated to 设置 and then called <c>WindowHost.ShowAndActivate()</c>,
    /// which raises <c>LauncherShown</c> and sets <c>PendingFocus</c> unconditionally.
    /// </para>
    /// <para>
    /// <b>Round 8, Task 2 left the line alone and changed what it means</b>, exactly as it did for
    /// <see cref="RefitToContent"/>'s twin: there is one page, so this is an ATTACHMENT test now. It
    /// is kept for the same reason as the other — one reference comparison against a reproduced defect
    /// class.
    /// </para>
    /// <para>
    /// <b>Round 8, Task 3 narrowed what it protects, and it is kept anyway.</b> The search box is a
    /// child of this window's menu band now, not of <see cref="_appPage"/>, so it can take focus
    /// whether or not the page is hosted, and the original argument (Round 7, Task 1's: a detached
    /// page's TextBox fails <c>FocusManager.CanFocus</c>, which is <c>Focusable &amp;&amp;
    /// IsEffectivelyEnabled &amp;&amp; IsEffectivelyVisible</c>, decompiled) no longer applies to the
    /// BOX. It still applies to the REQUEST: every producer of <c>PendingFocus</c> is the page's own
    /// view model, and focusing a filter box over a page that is not on screen is the same wrong thing
    /// it always was. The guard is a statement about the requester now rather than about the target.
    /// </para>
    /// </remarks>
    private void ConsumePendingFocus()
    {
        if (!_appPage.ViewModel.PendingFocus)
            return;

        if (!ReferenceEquals(PageHost.Content, _appPage))
            return;

        _appPage.ViewModel.PendingFocus = false;
        FocusSearchBox();
    }

    /// <summary>Focus the search box. Called by <see cref="ConsumePendingFocus"/>, which owns the
    /// decision of WHEN. Swallowing is deliberate and is carried over verbatim from both previous
    /// homes: focus is a nicety and must never take startup down.</summary>
    private void FocusSearchBox()
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
    /// clicked." Clearing and then re-focusing is the same pair the empty state's 清除筛选 button does
    /// (<c>AppPage.OnClearFilterClick</c>), through the same <c>PendingFocus</c> channel, so there is
    /// one path for every "focus the box" request and <see cref="ConsumePendingFocus"/> still owns the
    /// timing.</summary>
    /// <remarks>Primary button only, for the same reason <c>AppPage.OnGroupTitlePressed</c> guards:
    /// <c>PointerPressed</c> fires for every button, and a right-click here would otherwise wipe the
    /// filter. The affordance is a Border rather than a Button because every Button theme in this app
    /// carries a height floor that would hold the 23 DIP field open — see the
    /// <c>Border.tm-search-affordance</c> rule.</remarks>
    private void OnSearchClearPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _appPage.ViewModel.ClearFilter();
        _appPage.ViewModel.PendingFocus = true;
        e.Handled = true;
    }

    /// <summary>Enter launches the first match, Escape clears then hides, Down enters the tile grid.
    /// The Down case is the one line that still reaches across into the page, because the grid is the
    /// page's.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _appPage.ViewModel.HandleSearchEnter();
                e.Handled = true;
                break;
            case Key.Escape:
                _appPage.ViewModel.HandleSearchEscape();
                e.Handled = true;
                break;
            case Key.Down:
                e.Handled = _appPage.FocusFirstTile();
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The menu band's FIVE commands: 设置 · 主目录 · 刷新 · 退出 · 关于
    //
    // Every one is a direct command with no submenu. Three of them (主目录/刷新/退出) have been the
    // same three handlers since Round 3, Task 2 moved them out of the title strip's ⋯ overflow
    // MenuFlyout; 设置 joined them in Round 8, Task 2 when it stopped being a page; 关于 came back in
    // Round 8, Task 3 after Round 5, item 2 had folded it into the settings surface as a card, and
    // Round 8, Task 4 gave it a window of its own again (Dialogs/AboutWindow).
    // MainWindow.axaml's band block carries the published sentences for and against a menu bar of
    // direct commands, and the ruling.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _appPage.ViewModel.RefreshAsync();

    /// <summary>设置. Goes through <see cref="ISettingsLauncher"/> like the tray menu's own entry and the
    /// 应用 page's empty-state 打开设置… button, rather than constructing the dialog here: that
    /// service owns the single-instance guard, the SuppressHide bracket and the hidden-owner
    /// fallback, and having three call sites reproduce them would be three chances to get one
    /// wrong. (There were four until Round 8, Task 4 sent 关于 to its own window — see
    /// <see cref="OnAboutClick"/> for why that one does not want this service.)</summary>
    private void OnOptionsClick(object? sender, RoutedEventArgs e) =>
        App.Services.GetRequiredService<ISettingsLauncher>().Open();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.ExitApp();

    /// <summary>
    /// 关于 — the About box, <c>Dialogs/AboutWindow</c>. PUBLISHED, <c>win-property-win</c>: "Is the
    /// content solely an application's attributes? If so, use an About box." Round 8, Task 4 built it;
    /// between Task 3 and Task 4 this handler opened the Options dialog and selected its 关于 tab, which
    /// that task's own remark called provisional.
    /// </summary>
    /// <remarks>
    /// <b>Not through <see cref="ISettingsLauncher"/>, and the asymmetry with
    /// <see cref="OnOptionsClick"/> is the point.</b> That service exists for a dialog with three call
    /// sites, one of them the tray menu, which is reachable while the launcher is hidden — hence its
    /// single-instance guard and its <c>ShowAndActivate</c>. The About box has one call site, this one,
    /// on a band that belongs to the window <c>ShowDialog</c> is about to disable.
    /// <c>AboutWindow.ShowAsync</c> keeps the two brackets that are not optional — <c>SuppressHide</c>
    /// and the hidden-owner fallback — and its remarks say why each.
    /// <para>
    /// Fire-and-forget but NOT fault-and-forget, exactly as <c>SettingsLauncher.Open</c> is: a bare
    /// <c>_ = ShowAsync(…)</c> drops a XAML-load or DI fault into an unobserved Task and leaves a menu
    /// item that silently does nothing.
    /// </para>
    /// </remarks>
    private void OnAboutClick(object? sender, RoutedEventArgs e) =>
        _ = AboutWindow.ShowAsync(_host).ContinueWith(
            t => Serilog.Log.Error(t.Exception, "打开关于对话框失败"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Alt+F4 (the system menu's Close command)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_ENABLED = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;

    /// <summary>
    /// Put <c>SC_CLOSE</c> back in the window's system menu, which is what makes Alt+F4 work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Review finding 5. <c>ExtendClientAreaToDecorationsHint="True"</c> (MainWindow.axaml) has a side
    /// effect nothing documents: decompiled <c>Avalonia.Win32.WindowImpl.ExtendClientArea()</c> ends with
    /// <c>if (!_isClientAreaExtended) EnableCloseButton(_hwnd); else DisableCloseButton(_hwnd);</c>, and
    /// <c>DisableCloseButton</c> greys <c>SC_CLOSE</c> in the system menu with
    /// <c>EnableMenuItem(..., MF_BYCOMMAND | MF_GRAYED)</c>. Greying that one menu item is ALSO how
    /// Windows disables the Alt+F4 gesture, so extending the client area silently took Alt+F4 away.
    /// Avalonia's own drawn Close caption button was unaffected — it calls <c>Window.Close()</c> directly
    /// rather than posting <c>WM_SYSCOMMAND</c> — which is exactly why the loss was invisible.
    /// </para>
    /// <para>
    /// Undoing it is one call and needs no interception: <c>Window.Close()</c> and <c>WM_SYSCOMMAND</c>/
    /// <c>SC_CLOSE</c> converge on the same <c>WM_CLOSE</c>, which Avalonia turns into the Closing event,
    /// so Alt+F4 lands in <see cref="OnClosing"/> and hides to the tray exactly like the caption button.
    /// </para>
    /// <para>
    /// <c>GetSystemMenu(hwnd, false)</c> returns the window's own copy of the menu (passing true would
    /// RESET it, discarding the change). Called from <see cref="OnOpened"/> — every Show, cold start and
    /// un-hide alike — and again on every window-state change, because Avalonia re-runs
    /// <c>ExtendClientArea()</c> from its own state handling and would re-grey the item.
    /// </para>
    /// </remarks>
    private void RestoreSystemCloseCommand()
    {
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            var menu = GetSystemMenu(hwnd, false);
            if (menu == IntPtr.Zero)
                return;

            var before = GetMenuState(menu, SC_CLOSE, MF_BYCOMMAND);
            if (before == -1)
                return; // no SC_CLOSE item on this menu at all; nothing to re-enable

            if (((uint)before & (MF_GRAYED | MF_DISABLED)) == 0)
                return; // already enabled — nothing to do, and nothing worth logging

            EnableMenuItem(menu, SC_CLOSE, MF_BYCOMMAND | MF_ENABLED);

            // Debug rather than Information: this is expected to fire on every Show for as long as
            // Avalonia keeps disabling the item, so it is diagnostic detail, not an event.
            Serilog.Log.Debug(
                "Re-enabled the system menu's Close command (Alt+F4): state {Before} -> {After}",
                before, GetMenuState(menu, SC_CLOSE, MF_BYCOMMAND));
        }
        catch (Exception ex)
        {
            // A window that cannot re-enable Alt+F4 still closes from its caption button and the tray.
            Serilog.Log.Warning(ex, "Failed to re-enable the system menu's Close command");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetSystemMenu(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [LibraryImport("user32.dll")]
    private static partial int EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    [LibraryImport("user32.dll")]
    private static partial int GetMenuState(IntPtr hMenu, uint uId, uint uFlags);
}
