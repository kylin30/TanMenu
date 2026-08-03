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
/// <c>Window.WindowDecorationsTheme</c>), the icon-only navigation rail and the page host. All launcher
/// STATE lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c>. This class owns what genuinely
/// belongs to the window — the backdrop and its opaque fallback, the caption's active/inactive glass
/// (<see cref="ApplyCaptionGlass"/>), close-to-tray, labelling the rail's five rows
/// (<see cref="ApplyRailText"/>, ten writes — a name and a tooltip each), swapping the page host's
/// content between the two PAGE entries (应用/设置; see <see cref="ShowPage"/>), dispatching the three
/// command rows the old ⋯ overflow used to hold (打开主目录/刷新/退出 — Round 3, Task 2 moved four, 关于
/// among them, and Round 5, item 2 took 关于 back out of the rail and into the 设置 page as a card), and
/// kicking off the startup build + the fit-to-content resize that follows it, on EVERY summon
/// (<see cref="InitializeAndFitAsync"/>) — the actual geometry math, and the rule for when a manual
/// resize should win over a fresh fit, lives in <c>WindowHost</c>, which owns every other piece of
/// window placement too.
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
    private SettingsPage? _settingsPage;

    /// <summary>The live subscription <see cref="ApplyBackdrop"/> creates when the backdrop was
    /// refused, kept so the repeated <c>OnOpened</c> of every summon reuses the one it already made
    /// instead of stacking a second, third and n-th observer on the same resource.</summary>
    private IDisposable? _backdropFallback;

    /// <summary>The live subscription <see cref="ApplyCaptionGlass"/> holds on whichever of the two
    /// caption gradients matches the window's current active state. Replaced (not stacked) on every
    /// activation change.</summary>
    private IDisposable? _captionGlass;

    // Guards OnNavSelectionChanged against the SelectionChanged events XAML's own SelectedIndex="0"
    // (and Attach's own ShowPage call, if a navigation arrived before the window existed) raise
    // synchronously WHILE this constructor is still running — see OnNavSelectionChanged.
    private bool _navReady;

    /// <summary>
    /// Round 3, Task 2 review fix (Important 1). Set for the whole duration of <see cref="ShowPage"/>
    /// and checked at the top of <see cref="OnNavSelectionChanged"/>. <c>SelectedIndex</c> assignment
    /// fires <c>SelectionChanged</c> SYNCHRONOUSLY (confirmed by decompiling
    /// <c>Avalonia.Controls.Primitives.SelectingItemsControl</c>), so a <see cref="ShowPage"/> call that
    /// arrives WITHOUT a rail click having already set <c>NavRail</c>/<c>NavFooterRail</c>'s indices to
    /// their target values — the tray/empty-state settings entry
    /// (<c>ISettingsLauncher.Open</c> → <c>SettingsLauncher.OpenCore</c> → <c>ShellNavigator.Navigate</c>
    /// straight into this method) and <c>WindowHost.Hide</c>'s reset to 应用 are the two real callers —
    /// would otherwise let <see cref="OnNavSelectionChanged"/> call straight back into
    /// <c>ShellNavigator.Navigate</c> → <see cref="ShowPage"/> BEFORE the outer call ever reaches its own
    /// <c>PageHost.Content = …</c> line: a genuine re-entrant call performing the real page swap while
    /// the outer one is still on the stack. Nothing broke VISIBLY before this flag existed — the outer
    /// call's own later writes became no-ops (Avalonia does not re-raise a property-changed notification
    /// for an unchanged value) and <see cref="RefitToContent"/>/<see cref="ConsumePendingFocus"/> both
    /// already guard themselves idempotently — but every one of those two transitions was silently
    /// double-posting both of them regardless, which is exactly the structural risk the review flagged.
    /// </summary>
    private bool _inShowPage;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        App.Services.GetRequiredService<ShellNavigator>().Attach(this);

        // AppLanguage/AppFonts have no change notification of their own, so both the rail's row labels
        // (its tooltips and accessible names — see ApplyRailText; since Round 4, item 1 the rail has no
        // VISIBLE label to update) and the window font are re-applied on every SettingsChanged — the
        // same imperative approach every settings card group uses for its own labels (AppLanguage has
        // no change notification there either).
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

        _navReady = true;
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
    /// Re-run <c>WindowHost.FitToContent</c> against the launcher's current natural size — unless the
    /// launcher is not the page on screen, in which case this does NOTHING AT ALL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called after the startup build finishes (<see cref="InitializeAndFitAsync"/>, on every summon)
    /// and again from every other event that changes what the active page measures: a docked notice
    /// opening/closing (<see cref="OnNoticeVisibilityChanged"/>), a 每列个数 / 字体 / 语言 change
    /// (<see cref="OnLauncherContentMetricsChanged"/>), a switch back to 应用
    /// (<see cref="ShowPage"/>), and the Maximized→Normal transition
    /// (<see cref="OnPropertyChanged"/>). All of them are the exact same operation, so there is one place
    /// that composes the shell's chrome width and calls through. (Round 4, item 1 removed a sixth
    /// trigger — the rail's expand/collapse toggle — by removing the state it compensated for: the rail
    /// is one fixed width now, so it can no longer change what the shell takes from the page.)
    /// </para>
    /// <para>
    /// THE GUARD IS LOAD-BEARING, not defensive tidiness (review finding 1, CRITICAL). Measuring
    /// <see cref="_appPage"/> while 设置 is the hosted page does REAL, REPRODUCED damage to the window's
    /// SIZE: reproduced live by temporarily removing this guard, the window latched at a wrong
    /// 850x568 DIP for the rest of the session — 568 being the app's own minimum height, declared on
    /// <c>MainWindow.axaml</c> at the time and moved to <c>WindowHost.MinWindowHeightDip</c> by Round 4,
    /// item 4, i.e. a measurement that had collapsed onto the floor. That reproduction is the
    /// observed fact this guard rests on; see
    /// docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md for the durable record (this
    /// paragraph previously cited "final-fix-report.md, finding 1," a file that did not exist at the
    /// time it was written).
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
    /// is the tile's ControlTheme: <c>TmTileButton</c> (<c>Themes/Fluent2/Launcher.axaml</c>) is applied
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
    /// The tray's 选项 entry reaches exactly the sizing defect above: <c>SettingsLauncher.OpenCore</c>
    /// navigates to 设置 and THEN calls <c>ShowAndActivate</c>, whose <c>Show()</c> re-raises
    /// <c>Opened</c>, whose <see cref="InitializeAndFitAsync"/> awaits an already-completed memoized Task
    /// and continues synchronously into this method with the launcher detached.
    /// </para>
    /// <para>
    /// Same test, and for the same underlying reason, as <see cref="ConsumePendingFocus"/>'s own guard;
    /// and like that one, the deferred work is not lost — <see cref="ShowPage"/> re-invokes this the next
    /// time the rail switches back to 应用.
    /// </para>
    /// </remarks>
    private void RefitToContent()
    {
        if (!ReferenceEquals(PageHost.Content, _appPage))
            return;

        (_host as WindowHost)?.FitToContent(_appPage, RailWidthDip + PaneBorderDip);
    }

    /// <summary>
    /// The navigation rail's width, mirroring <c>MainWindow.axaml</c>'s literal <c>Width="48"</c> on
    /// <c>NavRailPane</c> — the single source of truth; this constant has to be kept in step with it.
    /// A literal here (rather than a read of the pane's live <c>Width</c>/<c>Bounds</c>) is what the
    /// design asked for, and the reason it gives is that the fit term then depends on nothing but the
    /// XAML it mirrors — no layout state, no visual-tree lookup. Round 4, item 1 is what made this a
    /// constant at all — until then the rail had two widths and this term was read out of the config on
    /// every call.
    /// <para>
    /// An earlier draft of this remark justified the literal by claiming a fit can run before the pane
    /// has ever been arranged. That is withdrawn: it was never verified, and the call sites say
    /// otherwise — all four indirect ones POST <see cref="RefitToContent"/> at
    /// <see cref="DispatcherPriority.Loaded"/>, which sits strictly below the layout job's Render
    /// priority, and the one direct caller (<see cref="OnNoticeVisibilityChanged"/>) is a runtime
    /// event long after the first layout pass. It would also not have argued against reading
    /// <c>NavRailPane.Width</c>, which is a local styled-property value set by the XAML and reads back
    /// 48 whether or not a layout pass has run — only <c>Bounds</c> needs an arrange.
    /// </para>
    /// </summary>
    private const double RailWidthDip = 48;

    /// <summary>
    /// The content pane's own left border, in DIP. The invariant this pair encodes: the chrome width
    /// handed to <see cref="WindowHost.FitToContent"/> must equal the rail's width PLUS whatever left
    /// border the 应用 pane carries — a <c>Border</c> draws its <c>BorderThickness</c> inside its own
    /// bounds and arranges its child inside that, so a left border takes exactly that much away from
    /// the page and the fit has to hand it back or <c>AppPage</c>'s horizontal scrollbar returns.
    /// <para>
    /// Round 4, item 5 landed the border this term was reserved for: <c>Border.tm-page-pane</c>
    /// (Launcher.axaml) gives the 应用 pane <c>BorderThickness="1,1,0,0"</c>, so the 1 DIP here is now
    /// live rather than provisional. Its vertical twin is <c>WindowHost.ChromeHeightDip</c>, which adds
    /// the same 1 DIP for the pane's TOP border. The 设置 pane's border is 0 and the fit never runs on
    /// 设置 anyway (see the guard at the top of <see cref="RefitToContent"/>), so one constant covers
    /// every case the fit can actually see.
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
        // itself is a field kept alive and reattached every time the rail switches back to 应用.
        _appPage.Dispose();
        // _settingsPage may be null: it is built lazily, the first time 设置 is shown, so a session
        // that never visited it has nothing to unsubscribe.
        _settingsPage?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Escape reaches here only when focus was NOT inside the search box or <see cref="AppPage"/>'s own
    /// tile grid — e.g. a nav-rail row, or anywhere at all on 设置. Both of those already handle Escape
    /// (and mark it handled) themselves, matching the clear-then-hide behaviour the launcher always had.
    /// Before the rail existed, this window's own OnKeyDown handled Escape identically regardless of
    /// where focus was — calling the SAME view-model method here (rather than an unconditional Hide)
    /// keeps that behaviour rather than merely approximating it, ON 应用.
    /// </summary>
    /// <remarks>
    /// On 设置 it dismisses outright instead. <c>HandleSearchEscape</c> is two-stage — a non-empty filter
    /// is cleared and the window stays up — and on 设置 the filter belongs to a page the user cannot see
    /// (review finding 4: it can no longer be TYPED there, since <see cref="ShowPage"/> hides the box,
    /// but a filter carried over from 应用 is still non-empty), so routing through it would spend the
    /// user's first Escape clearing something invisible and only dismiss on the second.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape)
            return;

        if (ReferenceEquals(PageHost.Content, _appPage))
            _appPage.ViewModel.HandleSearchEscape();
        else
            _host.Hide();

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

    /// <summary>Swap the page host's content. <see cref="_appPage"/> is built eagerly (a field
    /// initializer, before this constructor even runs) since it holds the launcher's real state;
    /// <see cref="_settingsPage"/> is built lazily, the first time 设置 is shown. Both are then kept
    /// alive rather than rebuilt on every rail click, since each card group owns its own live control
    /// state (e.g. the font combo's selection guard).</summary>
    /// <remarks>
    /// Round 3, Task 2: 应用 and 设置 now live in TWO separate single-item ListBoxes (<c>NavRail</c>,
    /// the page list; <c>NavFooterRail</c>, the bottom-pinned footer — see MainWindow.axaml's rail
    /// comment for why) rather than one two-item list, so keeping them mutually exclusive needs an
    /// explicit SelectedIndex write on BOTH lists here — a plain ListBox only enforces "at most one
    /// selected item" WITHIN itself, not across a second, unrelated ListBox instance. Setting the
    /// inactive list's index to -1 rather than leaving it alone is what actually clears a stale
    /// selected look on the page just navigated away from; setting it to 0 rather than leaving it
    /// unchanged is what makes returning to a page re-select its row even if <c>OnNavSelectionChanged</c>
    /// never ran for it (e.g. the tray's settings entry, which calls this through <c>ShellNavigator</c>
    /// without any rail click at all).
    /// </remarks>
    /// <remarks>
    /// Round 3, Task 2 review fix (Important 1): <see cref="_inShowPage"/> is set for this whole method
    /// (not only around the two <c>SelectedIndex</c> writes below) — see that field's own remarks for
    /// the full reentrancy this closes: the writes are where the synchronous <c>SelectionChanged</c> can
    /// actually fire, but wrapping the whole method costs nothing extra, since nothing later in this
    /// method can itself trigger another synchronous call back into <see cref="OnNavSelectionChanged"/>.
    /// </remarks>
    public void ShowPage(ShellPage page)
    {
        _inShowPage = true;
        try
        {
            ShowPageCore(page);
        }
        finally
        {
            _inShowPage = false;
        }
    }

    private void ShowPageCore(ShellPage page)
    {
        // Read BEFORE the swap: "was 设置 the page being replaced" is what distinguishes a real
        // 设置 → 应用 switch (which owes the launcher a re-fit, since nothing measured it while it was
        // detached) from the shell's very first ShowPage, where PageHost.Content is still null and
        // InitializeAndFitAsync owns the first fit. _settingsPage is null until 设置 is first visited,
        // so this is false for the whole of a session that never goes there.
        var wasSettings = _settingsPage is not null && ReferenceEquals(PageHost.Content, _settingsPage);

        NavRail.SelectedIndex = page == ShellPage.App ? 0 : -1;
        NavFooterRail.SelectedIndex = page == ShellPage.Settings ? 0 : -1;
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new SettingsPage()
            : _appPage;

        // Round 4, item 5: the two pages get two different content-pane looks and this method is
        // already the single authority for which page is on screen, so the class toggle belongs here
        // rather than in either page. Both looks are styles in Launcher.axaml ("Border.tm-page-pane"),
        // which is also where the reason they cannot be local attributes on the Border is written down.
        // The class lands on PagePane, the always-attached host, never on anything inside _appPage — so
        // RefitToContent's detach guard and the measurement it protects are untouched by it.
        //
        // Round 6, item 2: the class is "app", NOT "settings", and the flip is the point rather than a
        // rename. 应用 keeps the white pane with the hairline and the rounded top-left corner; every
        // OTHER page now paints nothing and lets the window's own surface (the acrylic backdrop, or
        // ApplyBackdrop's SurfaceBaseBrush when the platform refuses it) run straight through, the way
        // Windows' own Settings page does — measured: it has no content pane at all. Encoding it as
        // "app" makes "on the window material" the DEFAULT, so a third ShellPage member would come up on
        // the material instead of silently coming up white, which is the opposite of what was asked.
        // This overrules Round 4 §5.1(d) deliberately: that ruling kept 设置 on SurfaceBaseBrush because
        // our cards composite to the exact colour of a WHITE page, and the backdrop is not white — Mica
        // measured 238..247 Light / 30..34 Dark under this window, straddling SurfaceBaseBrush, so the
        // card separation held. THAT STRADDLE WAS MEASURED AGAINST MICA and Round 7, Task 1 requested
        // AcrylicBlur instead; the conclusion has not been re-measured on acrylic and Round 7's Task 7
        // owns re-taking it. The Mica measurements are in the Launcher.axaml block. Do not
        // "restore" the grey.
        PagePane.Classes.Set("app", page == ShellPage.App);

        // Round 6, item 1 hid the title strip's search box on 设置 — a page with nothing to search —
        // with a "SearchField.IsVisible = page == ShellPage.App" write here. Round 7, Task 1 DELETED
        // that write rather than porting it: the field is IN the 应用 page now (Views/AppPage.axaml),
        // so it is detached along with the rest of that page whenever 设置 is on screen and there is
        // nothing left to hide. The behaviour item 1 asked for is unchanged; the mechanism is
        // structural instead of imperative.

        if (page != ShellPage.App)
            return;

        // Retry a focus request ConsumePendingFocus deferred because 设置 was showing when it arrived
        // (see that method's remarks) — the same "re-check what needs the visual tree on re-attach"
        // rule AppPage's pre-move version of this mechanism followed, just relocated to the one place
        // that now knows which page is active.
        Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);

        // …and the re-fit that was skipped for the same reason, on the same deferred slot: the launcher
        // has just been re-attached to the logical tree, so its styles are re-applied and it can be
        // measured meaningfully again. See RefitToContent's own remarks for what happens if it is
        // measured any earlier than this.
        if (wasSettings)
            Dispatcher.UIThread.Post(RefitToContent, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Round 3, Task 2: 应用 and 设置 moved into two SEPARATE single-item ListBoxes (<c>NavRail</c>,
    /// <c>NavFooterRail</c> — see MainWindow.axaml's rail comment for why), so this handler now fires
    /// for either one and has to keep them acting like one logical selection group itself; a plain
    /// ListBox only ever enforces "at most one selected item" within its OWN items.
    /// </summary>
    /// <remarks>
    /// Two separate guards, for two separate kinds of call this same handler receives that must NOT
    /// navigate: <see cref="_inShowPage"/> (Round 3, Task 2 review fix, Important 1) discards a
    /// SelectionChanged raised by <see cref="ShowPageCore"/>'s OWN <c>SelectedIndex</c> writes — see
    /// that field's own remarks for why reacting to those used to make <see cref="ShowPage"/>
    /// re-entrant. <c>rail.SelectedIndex &lt; 0</c> discards the re-entrant-LOOKING but harmless
    /// SelectionChanged this method's OWN <c>otherRail.SelectedIndex = -1</c> line below raises on the
    /// OTHER list (its <c>AddedItems</c> is empty and its own <c>SelectedIndex</c> reads -1 by the time
    /// this runs) — that one is not <see cref="_inShowPage"/>'s concern, since it happens BEFORE
    /// <see cref="ShowPage"/> is ever called for this navigation. Only a real "the user (or UI
    /// Automation) picked a row" call ever reaches the bottom of this method with SelectedIndex at 0
    /// and <see cref="_inShowPage"/> false.
    /// </remarks>
    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Ignored until the constructor finishes (_navReady): XAML's SelectedIndex="0" — and,
        // once a real navigation can arrive before the window exists (Task 13's tray path),
        // Attach's own ShowPage call — both raise this event synchronously during construction.
        // ShowPage already applies NavRail.SelectedIndex/NavFooterRail.SelectedIndex and
        // PageHost.Content directly, so nothing is lost by discarding a SelectionChanged that fires
        // before the shell is ready; letting it through would instead let Navigate(App) (this event's
        // own read of a freshly-defaulted SelectedIndex="0") clobber a Current a pre-construction
        // Navigate(Settings) had already set.
        //
        // MUST read SelectedIndex off the sender, not a NavRail/NavFooterRail field: this event can
        // fire before the generated populate code has assigned those fields — reading a field there
        // null-refs.
        if (!_navReady || sender is not ListBox rail)
            return;

        if (_inShowPage)
        {
            // Round 3, Task 2 review fix (Important 1): logged at Debug, not silently discarded, so the
            // two real callers that can reach ShowPage WITHOUT a preceding rail click — the settings
            // entry (ISettingsLauncher.Open -> SettingsLauncher.OpenCore) and WindowHost.Hide's reset to
            // 应用 — can be confirmed LIVE to actually hit this suppression instead of re-entering
            // ShellNavigator.Navigate.
            Serilog.Log.Debug(
                "OnNavSelectionChanged suppressed a write from ShowPage itself ({Rail}, SelectedIndex={Index})",
                rail.Name, rail.SelectedIndex);
            return;
        }

        if (rail.SelectedIndex < 0)
            return;

        var otherRail = ReferenceEquals(rail, NavRail) ? NavFooterRail : NavRail;
        otherRail.SelectedIndex = -1;

        App.Services.GetRequiredService<ShellNavigator>()
            .Navigate(ReferenceEquals(rail, NavFooterRail) ? ShellPage.Settings : ShellPage.App);
    }

    /// <summary>AppLanguage has no change notification, so the rail is labelled imperatively — the same
    /// approach the settings UI uses. Round 3, Task 2: also labels the command rows that used to be the
    /// title strip's ⋯ overflow MenuItems — MenuOpenRoot/MenuRefresh/MenuExit are the SAME language keys
    /// those MenuItems already used (AppLanguage.cs), unchanged by the move.
    /// <para>
    /// Round 4, item 1 removed the rail's expanded state and with it every visible row label, so what
    /// this method writes is now the ONLY name each row has, in any state. Round 5, item 2 took the 关于
    /// row out of the rail altogether, so there are five rows and ten writes; MenuAbout went with it and
    /// the key it left behind was renamed to "About" for the settings card that replaced the row.
    /// </para></summary>
    private void ApplyRailText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        var appsText = AppLanguage.Text("NavApps", lang);
        var settingsText = AppLanguage.Text("NavSettings", lang);
        var openRootText = AppLanguage.Text("MenuOpenRoot", lang);
        var refreshText = AppLanguage.Text("MenuRefresh", lang);
        var exitText = AppLanguage.Text("MenuExit", lang);

        // AutomationProperties.Name/ToolTip.Tip live on the ROW (the ListBoxItem or, for the three
        // command rows, the Button), not on anything inside it: a row's whole content is a 16 DIP icon
        // glyph (MainWindow.axaml), which names nothing to a screen reader and nothing to a user who
        // does not already know the glyph. These ten writes are the rail's entire labelling.
        NavAppsItem.SetValue(AutomationProperties.NameProperty, appsText);
        NavSettingsItem.SetValue(AutomationProperties.NameProperty, settingsText);
        NavOpenRootItem.SetValue(AutomationProperties.NameProperty, openRootText);
        NavRefreshItem.SetValue(AutomationProperties.NameProperty, refreshText);
        NavExitItem.SetValue(AutomationProperties.NameProperty, exitText);
        ToolTip.SetTip(NavAppsItem, appsText);
        ToolTip.SetTip(NavSettingsItem, settingsText);
        ToolTip.SetTip(NavOpenRootItem, openRootText);
        ToolTip.SetTip(NavRefreshItem, refreshText);
        ToolTip.SetTip(NavExitItem, exitText);
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
    // is the one place that knows which page is on screen. The focus call itself is forwarded to
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
    /// Fix (Task 1 review, Important 3): a focus request that arrives while 设置 is the active page
    /// must NOT steal focus into a search box that filters a page the user cannot see —
    /// <c>ISettingsLauncher.OpenCore</c> navigates to 设置 and THEN calls
    /// <c>WindowHost.ShowAndActivate()</c>, which unconditionally raises <c>LauncherShown</c> and sets
    /// <c>PendingFocus</c> regardless of which page is showing (the tray's 选项 entry hits this path).
    /// The guard asks the one thing that actually determines whether the user can see the box: whether
    /// <see cref="_appPage"/> is the page <see cref="PageHost"/> is currently showing. Left SET
    /// otherwise; <see cref="ShowPage"/> retries this the next time the rail switches back to 应用,
    /// exactly the "re-check what needs the visual tree on re-attach" rule AppPage's own pre-move
    /// mechanism followed.
    /// <para>
    /// Round 7, Task 1 put the search box back inside <see cref="AppPage"/>, so the guard is now also
    /// literally an attachment test — a detached page's TextBox cannot take focus at all
    /// (<c>FocusManager.CanFocus</c> is <c>Focusable &amp;&amp; IsEffectivelyEnabled &amp;&amp;
    /// IsEffectivelyVisible</c>, decompiled). It is kept as an explicit page test rather than reduced
    /// to that, because it is also what keeps the deferred request alive for the next switch back.
    /// </para>
    /// </summary>
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
    // Navigation rail: the three relocated commands (Round 3, Task 2 — used to be the title strip's
    // ⋯ overflow MenuFlyout; MainWindow.axaml's rail comment explains why they are Buttons, not
    // ListBoxItems, and why each is its own row rather than one row that opens a flyout).
    // There were four until Round 5, item 2 moved 关于 into the 设置 page as a card and deleted the
    // overlay it raised; OnAboutClick — the one body Round 4 had touched, to navigate to 应用 before
    // raising a panel that lived on that page — went with it. The three below are still unchanged from
    // the overflow-menu era.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _appPage.ViewModel.RefreshAsync();

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
