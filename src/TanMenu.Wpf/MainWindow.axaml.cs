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
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;
using TanMenu.Wpf.Views;

namespace TanMenu.Wpf;

/// <summary>
/// The shell: the hand-drawn Aero caption (MainWindow.axaml's replaced
/// <c>Window.WindowDecorationsTheme</c>), the wide labelled navigation pane and the page host. All
/// launcher STATE lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c>. This class owns what
/// genuinely belongs to the window — the backdrop and its opaque fallback, the caption's active/inactive
/// glass (<see cref="ApplyCaptionGlass"/>), close-to-tray, labelling the pane's five rows
/// (<see cref="ApplyRailText"/>, ten writes — an automation name and a visible label each), dispatching
/// the four command rows the pane now holds (打开主目录/刷新/设置/退出), and kicking off the startup
/// build + the fit-to-content resize that follows it, on EVERY summon
/// (<see cref="InitializeAndFitAsync"/>) — the actual geometry math, and the rule for when a manual
/// resize should win over a fresh fit, lives in <c>WindowHost</c>, which owns every other piece of
/// window placement too.
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
/// <para>
/// Round 7, Task 1 took the search box back OUT of this class: it was a control in the window frame,
/// which <c>win-window-frames</c> forbids in as many words, so it returned to the 应用 page. What is
/// left here of it is the <c>PendingFocus</c> hand-off — see <see cref="ConsumePendingFocus"/>.
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

        // AppLanguage/AppFonts have no change notification of their own, so both the navigation pane's
        // row labels (their visible text and their accessible names — see ApplyRailText; since Round 7,
        // Task 8 there IS a visible label again, and this subscription is what keeps it and the
        // automation name in step through a 语言 change) and the window font are re-applied on every
        // SettingsChanged — the same imperative approach every settings card group uses for its own
        // labels (AppLanguage has no change notification there either).
        _events = App.Services.GetRequiredService<AppEvents>();
        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyRailText;
        ApplyWindowFont();
        ApplyRailText();

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
    /// measurement: <c>Image.tm-tile-icon</c>'s 20x20 icon box, <c>TextBlock.tm-section</c>'s
    /// size/line-height/weight, <c>Border.tm-section-head</c>'s padding, <c>StackPanel.tm-group</c>'s
    /// alignment, the search field's whole geometry, and so on. Which of those produced 850x568
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

        (_host as WindowHost)?.FitToContent(_appPage, RailWidthDip + PaneBorderDip);
    }

    /// <summary>
    /// The navigation pane's width, mirroring <c>MainWindow.axaml</c>'s literal <c>Width="160"</c> on
    /// <c>NavRailPane</c> — the single source of truth; this constant has to be kept in step with it.
    /// A literal here (rather than a read of the pane's live <c>Width</c>/<c>Bounds</c>) is what the
    /// design asked for, and the reason it gives is that the fit term then depends on nothing but the
    /// XAML it mirrors — no layout state, no visual-tree lookup. Round 4, item 1 is what made this a
    /// constant at all — until then the rail had two widths and this term was read out of the config on
    /// every call.
    /// <para>
    /// Round 7, Task 8 moved it from 48 to 160: the icon-only rail became a wide labelled Windows 7
    /// pane. It is still ONE width — Task 8 restored the row labels and none of the other three things
    /// Round 4 deleted with them (the 200 DIP second width, the <c>DoubleTransition</c>, the
    /// <c>ClipToBounds</c>) — so this stays a constant.
    /// <c>ShellConstantTests.RailWidth_MatchesTheRailPaneWidth</c> is what catches a drift between the
    /// two copies; nothing else in the tree can see one. Round 8, Task 2 left both the pane and this
    /// term alone — it changed what the pane's 设置 ROW is (a Button that opens a dialog, not a
    /// ListBoxItem the shell navigates to), not how wide the pane is — so no fit arithmetic moves in
    /// that task. Round 8, Task 3 deletes the pane and this constant with it.
    /// </para>
    /// <para>
    /// An earlier draft of this remark justified the literal by claiming a fit can run before the pane
    /// has ever been arranged. That is withdrawn: it was never verified, and the call sites say
    /// otherwise — all four indirect ones POST <see cref="RefitToContent"/> at
    /// <see cref="DispatcherPriority.Loaded"/>, which sits strictly below the layout job's Render
    /// priority, and the one direct caller (<see cref="OnNoticeVisibilityChanged"/>) is a runtime
    /// event long after the first layout pass. It would also not have argued against reading
    /// <c>NavRailPane.Width</c>, which is a local styled-property value set by the XAML and reads back
    /// its declared value whether or not a layout pass has run — only <c>Bounds</c> needs an arrange.
    /// </para>
    /// </summary>
    private const double RailWidthDip = 160;

    /// <summary>
    /// The content pane's own left border, in DIP. The invariant this pair encodes: the chrome width
    /// handed to <see cref="WindowHost.FitToContent"/> must equal the pane's width PLUS whatever left
    /// border the 应用 pane carries — a <c>Border</c> draws its <c>BorderThickness</c> inside its own
    /// bounds and arranges its child inside that, so a left border takes exactly that much away from
    /// the page and the fit has to hand it back or <c>AppPage</c>'s horizontal scrollbar returns.
    /// <para>
    /// Round 4, item 5 landed the border this term was reserved for: <c>Border.tm-page-pane.app</c>
    /// (Launcher.axaml) gives the 应用 pane <c>BorderThickness="1,1,0,0"</c>, so the 1 DIP here is now
    /// live rather than provisional. Its vertical twin is <c>WindowHost.ChromeHeightDip</c>, which adds
    /// the same 1 DIP for the pane's TOP border.
    /// </para>
    /// <para>
    /// Round 8, Task 2 deleted the <c>:not(.app)</c> branch this remark used to have to reason about —
    /// 设置 was its only subject and 设置 is a dialog now — and pinned the <c>app</c> class in the
    /// markup, so the pane has exactly one look and this term describes it unconditionally. Round 8,
    /// Task 3 deletes both the class and this constant, when the pane itself goes.
    /// </para>
    /// </summary>
    private const double PaneBorderDip = 1;

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
        _events.SettingsChanged -= ApplyRailText;
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
    /// Escape reaches here only when focus was NOT inside the search box or <see cref="AppPage"/>'s own
    /// tile grid — e.g. a navigation-pane row. Both of those already handle Escape (and mark it handled)
    /// themselves, matching the clear-then-hide behaviour the launcher always had. Before the rail
    /// existed, this window's own OnKeyDown handled Escape identically regardless of where focus was —
    /// calling the SAME view-model method here (rather than an unconditional Hide) keeps that behaviour
    /// rather than merely approximating it.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>ROUND 8, TASK 2 COLLAPSED BOTH BRANCHES TO THE SINGLE-PAGE CASE.</b> Escape had an
    /// <c>else _host.Hide()</c> for 设置, on the ground that <c>HandleSearchEscape</c> is two-stage (a
    /// non-empty filter is cleared and the window stays up) and on 设置 the filter belonged to a page the
    /// user could not see, so the first Escape would have been spent clearing something invisible.
    /// Ctrl+E had a matching page guard, for the mirror-image reason. There is one page now, so
    /// <b>Escape always routes to <c>HandleSearchEscape</c></b> — which is what the launcher did before
    /// the rail existed — and Ctrl+E always focuses the search box. Escape inside the Options DIALOG is
    /// not this window's business at all: that dialog is modal and its own 取消 button carries
    /// <c>IsCancel</c>.
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
        // focus is already inside the page it belongs to is not an accelerator: a key pressed on a
        // navigation-pane row bubbles to this window, never to AppPage. The request goes through
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
    //   · The "app" class on PagePane was flipped from ShowPageCore. It is a STATIC class in the
    //     markup now (MainWindow.axaml), and Launcher.axaml's :not(.app) branch — whose only subject
    //     was 设置 — is deleted. Task 3 collapses the remaining pair into one rule when the pane goes.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>AppLanguage has no change notification, so the navigation pane is labelled imperatively
    /// — the same approach the settings UI uses. Round 3, Task 2: also labels the command rows that used
    /// to be the title strip's ⋯ overflow MenuItems — MenuOpenRoot/MenuRefresh/MenuExit are the SAME
    /// language keys those MenuItems already used (AppLanguage.cs), unchanged by the move. Round 5,
    /// item 2 took the 关于 row out of the pane altogether, so there are five rows and ten writes;
    /// MenuAbout went with it and the key it left behind was renamed to "About" for the settings card
    /// that replaced the row.
    /// <para>
    /// ROUND 7, TASK 8 KEPT THE COUNT AND CHANGED THE SHAPE: ten writes still, but five
    /// <c>TextBlock.Text</c> where five <c>ToolTip.SetTip</c> used to be. The pane's rows carry visible
    /// labels now, and Microsoft's own glossary
    /// (https://learn.microsoft.com/en-us/windows/win32/uxguide/glossary) defines a tooltip as "a small
    /// pop-up window that labels the UNLABELED control being pointed to" — a tooltip that repeats a
    /// visible label is by that definition the wrong control. The five
    /// <c>AutomationProperties.Name</c> writes STAY, and every row still has one: it is the
    /// accessibility contract, and stating it costs less than reasoning about what Avalonia's automation
    /// peer derives from a DockPanel of two TextBlocks.
    /// </para></summary>
    private void ApplyRailText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        var appsText = AppLanguage.Text("NavApps", lang);
        var settingsText = AppLanguage.Text("NavSettings", lang);
        var openRootText = AppLanguage.Text("MenuOpenRoot", lang);
        var refreshText = AppLanguage.Text("MenuRefresh", lang);
        var exitText = AppLanguage.Text("MenuExit", lang);

        // AutomationProperties.Name lives on the ROW (the ListBoxItem or, for the three command rows,
        // the Button); the visible label lives on the TextBlock inside it. Both are written from the
        // same string, so the two can never disagree — which is what Task 8's verification asserts.
        NavAppsItem.SetValue(AutomationProperties.NameProperty, appsText);
        NavSettingsItem.SetValue(AutomationProperties.NameProperty, settingsText);
        NavOpenRootItem.SetValue(AutomationProperties.NameProperty, openRootText);
        NavRefreshItem.SetValue(AutomationProperties.NameProperty, refreshText);
        NavExitItem.SetValue(AutomationProperties.NameProperty, exitText);
        NavAppsLabel.Text = appsText;
        NavSettingsLabel.Text = settingsText;
        NavOpenRootLabel.Text = openRootText;
        NavRefreshLabel.Text = refreshText;
        NavExitLabel.Text = exitText;
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
    // Deferred search-box focus
    //
    // Round 7, Task 1 moved the search CONTROL back into the 应用 page (Views/AppPage.axaml), where it
    // lived before commit 6d00060, because Microsoft's rule for a window frame is verbatim "Don't add
    // controls to a window frame. Put the controls within the window instead."
    // (learn.microsoft.com/en-us/windows/win32/uxguide/win-window-frames) and Task 1's Aero caption IS
    // that frame. Its width no longer tracks the window either — the Task-Manager-proportional
    // ApplySearchFieldWidth, its three constants and the ClientSize subscription that drove it are all
    // deleted with the strip, and AppPage gives the field one authored width instead.
    //
    // What stays HERE is only the PendingFocus contract: the view model raises "please focus the search
    // box" from a fresh summon, from 清除筛选 and from Up off the top of the tile grid, and this class
    // is the one place that owns the timing. The focus call itself is forwarded to
    // AppPage.FocusSearchBox().
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
    /// Consume a deferred focus request, if the page that owns the search box is actually hosted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard was added as a PAGE test (Task 1 review, Important 3): a focus request arriving while
    /// 设置 was the active page must not steal focus into a search box filtering a page the user cannot
    /// see — the tray's 选项 entry navigated to 设置 and then called <c>WindowHost.ShowAndActivate()</c>,
    /// which raises <c>LauncherShown</c> and sets <c>PendingFocus</c> unconditionally.
    /// </para>
    /// <para>
    /// <b>Round 8, Task 2 leaves the line alone and changes what it means</b>, exactly as it does for
    /// <see cref="RefitToContent"/>'s twin: there is one page, so this is an ATTACHMENT test now.
    /// Round 7, Task 1 had already made it literally one — a detached page's TextBox cannot take focus
    /// at all (<c>FocusManager.CanFocus</c> is <c>Focusable &amp;&amp; IsEffectivelyEnabled &amp;&amp;
    /// IsEffectivelyVisible</c>, decompiled) — and it is kept for the same reason as the other: one
    /// reference comparison against a reproduced defect class. What is NO LONGER true, and is corrected
    /// rather than left standing, is the sentence that said a deferred request would be retried "the
    /// next time the rail switches back to 应用". There is no switch, so nothing retries it; the request
    /// simply stays set until the next Loaded pass posts this again, which every summon does.
    /// </para>
    /// </remarks>
    private void ConsumePendingFocus()
    {
        if (!_appPage.ViewModel.PendingFocus)
            return;

        if (!ReferenceEquals(PageHost.Content, _appPage))
            return;

        _appPage.ViewModel.PendingFocus = false;
        _appPage.FocusSearchBox();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Navigation pane: FOUR commands. Three were relocated in Round 3, Task 2 — they used to be the
    // title strip's ⋯ overflow MenuFlyout; MainWindow.axaml's pane comment explains why they are
    // Buttons, not ListBoxItems, and why each is its own row rather than one row that opens a flyout.
    // There were four then too until Round 5, item 2 moved 关于 into the 设置 page as a card and
    // deleted the overlay it raised.
    //
    // ROUND 8, TASK 2 MADE 设置 THE FOURTH. It was a PAGE — a ListBoxItem in a second ListBox that the
    // shell navigated to — and it is a modal dialog now, i.e. exactly what the other three already
    // were: a one-shot action that leaves the page on screen alone. That is what the pane's own
    // comment says a command row is, and a Button has no :selected pseudo-class, so "设置 must never
    // look selected" holds by construction rather than by a rule a later edit could defeat.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _appPage.ViewModel.RefreshAsync();

    /// <summary>设置. Goes through <see cref="ISettingsLauncher"/> like the tray menu's own entry and
    /// the 应用 page's empty-state 打开设置… button, rather than constructing the dialog here: that
    /// service owns the single-instance guard, the SuppressHide bracket and the hidden-owner
    /// fallback, and having three call sites reproduce them would be three chances to get one
    /// wrong.</summary>
    private void OnOptionsClick(object? sender, RoutedEventArgs e) =>
        App.Services.GetRequiredService<ISettingsLauncher>().Open();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.ExitApp();

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
