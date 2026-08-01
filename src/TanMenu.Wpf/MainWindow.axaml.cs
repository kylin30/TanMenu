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
/// The shell: the title strip (app icon/name, search), the collapsible navigation rail and the page
/// host. All launcher STATE still lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c> — the
/// search box here binds and dispatches to that SAME instance (exposed as <see cref="AppPage.ViewModel"/>)
/// rather than a second one, since AppPage still owns the keyboard model (Escape, Down-into-the-grid)
/// that the search box's key handling has to reach into. This class owns what genuinely belongs to the
/// window — the Mica fallback, close-to-tray, the caption-button reservation, the rail's
/// collapsed/expanded choice, swapping the page host's content between the two PAGE entries (应用/设置;
/// see <see cref="ShowPage"/>), dispatching the four command rows the title strip's old ⋯ overflow used
/// to hold (打开主目录/刷新/关于/退出 — Round 3, Task 2), and kicking off the startup build + the
/// fit-to-content resize that follows it, on EVERY summon (<see cref="InitializeAndFitAsync"/>) — the
/// actual geometry math, and the rule for when a manual resize should win over a fresh fit, lives in
/// <c>WindowHost</c>, which owns every other piece of window placement too.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWindowHost _host;
    private readonly AppEvents _events;
    private readonly AppPage _appPage = new();
    private SettingsPage? _settingsPage;

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

        // AppLanguage/AppFonts have no change notification of their own, so both the rail labels and
        // the window font are re-applied on every SettingsChanged — the same imperative approach every
        // settings card group uses for its own labels (AppLanguage has no change notification there
        // either).
        _events = App.Services.GetRequiredService<AppEvents>();
        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyRailText;
        // NavRailExpanded has no settings-card counterpart to raise SettingsChanged for it specifically
        // (see the field's remarks in GeneralConfig), but the hamburger's tooltip/automation name is
        // still language-dependent, so this re-applies alongside the other two for the same reason they
        // do: AppLanguage/AppFonts carry no change notification of their own.
        _events.SettingsChanged += ApplyNavExpandedState;
        ApplyWindowFont();
        ApplyRailText();
        ApplyNavExpandedState();

        // Round 3, Task 1 review fix: the search field's width must track the window's own size —
        // see ApplySearchFieldWidth's own remarks for the measured basis. ClientSize is a DirectProperty
        // (TopLevel), so GetObservable is the same mechanism Window's own base class uses internally for
        // its extended-client-area bookkeeping (Avalonia.Controls.Window, decompiled), not a bespoke
        // one. It fires once immediately on subscription with whatever ClientSize already is — before
        // the window has ever been measured that is Size.Empty (0,0), which ApplySearchFieldWidth
        // guards against, and XAML's own Width="280" is what is on screen for that instant.
        //
        // Wrapped in Avalonia.Reactive.AnonymousObserver rather than a fluent `.Subscribe(lambda)`:
        // IObservable<T> itself already declares Subscribe(IObserver<T>), and C# member lookup finds
        // that instance method first (an interface's own method of a given name is never shadowed by
        // an extension of the same name, so the extension overload some other Avalonia internals use
        // for this exact call shape — Avalonia.Reactive.Observable.Subscribe(IObservable<T>,Action<T>) —
        // is never even considered, and turned out to be internal to Avalonia.Base besides). This is
        // Subscribe(IObserver<T>) called directly, with AnonymousObserver adapting a plain Action<T> to
        // that interface — the one PUBLIC building block for exactly this.
        this.GetObservable(ClientSizeProperty).Subscribe(new AnonymousObserver<Size>(_ => ApplySearchFieldWidth()));

        // The search field and overflow menu bind straight to AppPage's own LauncherViewModel — see
        // the class remarks above for why this is not a second view model.
        TitleStripContent.DataContext = _appPage.ViewModel;
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
    /// Round 3 final-fix report, finding 4: that last claim — "a free no-op" — was FALSE for exactly the
    /// low-content-height regime this same Task 4 created (a small ColButtonCount, or the default's own
    /// natural height, both landing at or below MinHeight="568"). <c>WindowHost.FitToContent</c> used to
    /// compare a fresh <c>ResolveContentFit</c> result against the window's LIVE Width/Height, but this
    /// window cannot physically render below roughly 605 DIP outer regardless of what is requested (see
    /// <c>GeneralConfig.ColButtonCount</c>'s own remarks), so the live size Windows actually applied and
    /// the 568 DIP the fit had asked for never matched — the "free no-op" fired the whole
    /// Width/Height/Position/log/debounced-write path a second time on every cold start instead of
    /// skipping it. Fixed by comparing against the PREVIOUS fit's own requested size instead of the live
    /// window size; the claim in this paragraph is accurate again as of that fix.
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
    /// opening/closing (<see cref="OnNoticeVisibilityChanged"/>), the rail expanding/collapsing
    /// (<see cref="OnNavToggleClick"/>), a 每列个数 / 字体 / 语言 change
    /// (<see cref="OnLauncherContentMetricsChanged"/>), a switch back to 应用
    /// (<see cref="ShowPage"/>), and the Maximized→Normal transition
    /// (<see cref="OnPropertyChanged"/>). All of them are the exact same operation, so there is one place
    /// that reads the rail's width and calls through.
    /// </para>
    /// <para>
    /// THE GUARD IS LOAD-BEARING, not defensive tidiness (review finding 1, CRITICAL). Measuring
    /// <see cref="_appPage"/> while 设置 is the hosted page does REAL, REPRODUCED damage to the window's
    /// SIZE: <c>ContentControl</c> removes the outgoing content from <c>LogicalChildren</c> SYNCHRONOUSLY
    /// when <see cref="PageHost"/>'s Content is reassigned, and <c>StyledElement.OnDetachedFromLogicalTreeCore</c>
    /// then recurses the whole subtree calling <c>InvalidateStyles</c>, which drops every style frame from
    /// the value store and clears <c>_stylesApplied</c> — both established by decompiling Avalonia 12.1.0.
    /// A page measured in that state has lost <c>Button.tm-tile</c>'s fixed 168 DIP width — a plain
    /// <c>Width</c> Setter on <c>TmTileButton</c> (Launcher.axaml); Round 3, Task 4 removed the older
    /// <c>Panel.size-* Button.tm-tile</c> class-scoped 168/192/216 variants this comment used to cite —
    /// and every other app-level style, so the window is sized from a ragged, text-width measurement and
    /// lands on its MinWidth/MinHeight floor — reproduced live by temporarily removing this guard: the
    /// window latched at a wrong 850x568 DIP for the rest of the session. (This paragraph previously
    /// cited "final-fix-report.md, finding 1" for that reproduction; no such write-up exists under that
    /// name — the citation was a forward reference to a report that had not been written yet.)
    /// </para>
    /// <para>
    /// A SECOND, worse-sounding claim does NOT belong here as established fact: that the launcher then
    /// renders UNSTYLED for the rest of the session. On paper that is mechanistically plausible —
    /// <c>Layoutable.MeasureCore</c> calls <c>ApplyStyling()</c>, and <c>StyledElement.ApplyStyling</c>
    /// sets <c>_stylesApplied = true</c> UNCONDITIONALLY even though nothing can match while
    /// <c>StylingParent</c> is null, and the page's own later <c>OnAttachedToLogicalTreeCore</c> gates its
    /// re-styling on <c>_logicalRoot == null</c>, so its own <c>ApplyStyling()</c> call there sees the
    /// flag already set and no-ops — traced in the same decompile. But it was NOT observed: in both live
    /// pre-fix reproductions the tiles came back at their correct width (192 DIP — this predates Round 3,
    /// Task 4's later change to a fixed 168 DIP regardless of any setting) once the launcher was
    /// reattached to 应用, i.e. the styles WERE re-applied despite the latch. The most likely reason is
    /// <see cref="TanMenu.Wpf.ViewModels.LauncherViewModel.ReloadAsync"/> — it replaces the tile
    /// containers outright when the rebuild result reports a change, and only relabels the EXISTING ones
    /// otherwise, so a reload that finds nothing changed after a 设置→应用 round trip would keep whatever
    /// stale containers the mechanism above predicts, while one that does rebuild hands back fresh,
    /// correctly-styled ones — this does not fire on every run, so it does not rule the mechanism out, it
    /// only explains why it was not seen. Treat the sizing damage as confirmed and the un-styling as a
    /// live risk this same guard happens to close, not as an observed fact.
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

        // Read straight from config, not NavRailPane's own live Width/Bounds — see
        // WindowHost.FitToContent's own <param> remarks on railWidth for why: a config value can never
        // be mid-animation.
        var expanded = App.Services.GetRequiredService<ConfigService>().Config.General.NavRailExpanded;
        (_host as WindowHost)?.FitToContent(_appPage, expanded ? 200.0 : 48.0);
    }

    /// <summary>See the constructor's own remarks on subscribing to this — WindowHost.FitToContent's
    /// guards (maximized, already manually resized) decide whether this re-fit actually does anything.</summary>
    private void OnNoticeVisibilityChanged() => RefitToContent();

    /// <summary>
    /// The launcher finished a (re)build, so anything derived from settings that changes what it
    /// MEASURES has already been applied to it: each group's column layout (每列个数,
    /// <c>GeneralConfig.ColButtonCount</c> — Round 3, Task 4 replaced the 小/中/大 tile-size class this
    /// used to name; every tile is now a fixed 168 DIP regardless of any setting), the 字体 face, and
    /// every localized string on the page.
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
        _events.SettingsChanged -= ApplyNavExpandedState;
        _appPage.ViewModel.PropertyChanged -= OnLauncherViewModelPropertyChanged;
        _appPage.ViewModel.ContentMetricsChanged -= OnLauncherContentMetricsChanged;
        _appPage.NoticeVisibilityChanged -= OnNoticeVisibilityChanged;

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
    /// (review finding 4: it can no longer be TYPED there, since <see cref="ShowPage"/> disables the box,
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

    /// <summary>Mica is a request, not a guarantee: Windows 10, "show transparency" turned off and
    /// remote desktop all refuse it, and the window would then be a hole rather than a surface. Paint
    /// the opaque Fluent surface colour when it was not granted. (Same guard SettingsWindow used.)</summary>
    private void ApplyBackdrop()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
            Background = this.FindResource("SurfaceBaseBrush") as IBrush ?? Brushes.White;
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

        // Review finding 4: the title strip's search box is always attached and used to stay live on
        // 设置, where typing silently filtered an invisible 应用 page and Enter reached
        // LauncherViewModel.HandleSearchEnter — which LAUNCHES the first match. Task Manager disables
        // its own search box on pages that do not support search; so does this. Neither write is
        // redundant: SearchBox blocks the actual typing/Enter and (via its own :disabled style in
        // Launcher.axaml) dims its text and placeholder; SearchField disables and dims the bordered
        // box around it — a plain Border has no disabled behaviour of its own, so without this line a
        // disabled SearchBox would sit inside a field that still looked fully live.
        var searchable = page == ShellPage.App;
        SearchField.IsEnabled = searchable;
        SearchBox.IsEnabled = searchable;

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
            // ShellNavigator.Navigate. See task-2-report.md's fix-report section for the log capture.
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
    /// approach the settings UI uses. Round 3, Task 2: also labels the four command rows that used to be
    /// the title strip's ⋯ overflow MenuItems — MenuOpenRoot/MenuRefresh/MenuAbout/MenuExit are the SAME
    /// language keys those MenuItems already used (AppLanguage.cs), unchanged by the move.</summary>
    private void ApplyRailText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        var appsText = AppLanguage.Text("NavApps", lang);
        var settingsText = AppLanguage.Text("NavSettings", lang);
        var openRootText = AppLanguage.Text("MenuOpenRoot", lang);
        var refreshText = AppLanguage.Text("MenuRefresh", lang);
        var aboutText = AppLanguage.Text("MenuAbout", lang);
        var exitText = AppLanguage.Text("MenuExit", lang);

        NavAppsLabel.Text = appsText;
        NavSettingsLabel.Text = settingsText;
        NavOpenRootLabel.Text = openRootText;
        NavRefreshLabel.Text = refreshText;
        NavAboutLabel.Text = aboutText;
        NavExitLabel.Text = exitText;

        // AutomationProperties.Name/ToolTip.Tip live on the ROW (the ListBoxItem or, for the four
        // command rows, the Button), not on the label TextBlock: Launcher.axaml's ".collapsed" rule
        // hides every *-label TextBlock entirely (IsVisible=False) when the rail is collapsed, so an
        // icon-only row needs an accessible name and a tooltip that do not depend on that now-invisible
        // label being there to read.
        NavAppsItem.SetValue(AutomationProperties.NameProperty, appsText);
        NavSettingsItem.SetValue(AutomationProperties.NameProperty, settingsText);
        NavOpenRootItem.SetValue(AutomationProperties.NameProperty, openRootText);
        NavRefreshItem.SetValue(AutomationProperties.NameProperty, refreshText);
        NavAboutItem.SetValue(AutomationProperties.NameProperty, aboutText);
        NavExitItem.SetValue(AutomationProperties.NameProperty, exitText);
        ToolTip.SetTip(NavAppsItem, appsText);
        ToolTip.SetTip(NavSettingsItem, settingsText);
        ToolTip.SetTip(NavOpenRootItem, openRootText);
        ToolTip.SetTip(NavRefreshItem, refreshText);
        ToolTip.SetTip(NavAboutItem, aboutText);
        ToolTip.SetTip(NavExitItem, exitText);
    }

    /// <summary>
    /// Apply the rail's current collapsed/expanded choice: the pane's width (animated by the
    /// <c>DoubleTransition</c> on <c>NavRailPane.Width</c> in MainWindow.axaml), the "collapsed" class
    /// that drives Launcher.axaml's icon-only layout, and the hamburger's own tooltip/automation name —
    /// which names the action pressing it performs NEXT (展开/Expand vs. 折叠/Collapse), so it must flip
    /// every time the state does; a static XAML value could only ever be half right. Reads live from
    /// config rather than a cached field, since <see cref="OnNavToggleClick"/> and this method are the
    /// only two places that ever need the value and neither benefits from a second copy that could drift.
    /// Called once from the constructor and again on every <see cref="AppEvents.SettingsChanged"/> (the
    /// hamburger's tooltip text is language-dependent, and AppLanguage has no change notification).
    /// </summary>
    /// <remarks>
    /// Round 3, Task 2: the "collapsed" class now goes on <c>NavRailContent</c> (the Grid wrapping the
    /// page list, the four command rows and the settings footer), not on <c>NavRail</c> itself — the
    /// rail is no longer one ListBox, so no single element under the old name could still reach every
    /// row that needs to react. Launcher.axaml's selectors were updated to match
    /// (<c>Grid.tm-nav-rail.collapsed …</c>).
    /// </remarks>
    private void ApplyNavExpandedState()
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        var expanded = cfg.Config.General.NavRailExpanded;

        NavRailPane.Width = expanded ? 200 : 48;
        NavRailContent.Classes.Set("collapsed", !expanded);

        var toggleText = AppLanguage.Text(expanded ? "NavCollapse" : "NavExpand", cfg.Config.General.Language);
        NavToggleButton.SetValue(AutomationProperties.NameProperty, toggleText);
        ToolTip.SetTip(NavToggleButton, toggleText);
    }

    /// <summary>
    /// The hamburger toggle. Persists straight through <see cref="ConfigService.SaveAsync"/> rather than
    /// <see cref="SettingsWrite.CommitAsync"/> — see <see cref="TanMenu.Core.Models.GeneralConfig.NavRailExpanded"/>'s
    /// remarks for the full justification: this is shell chrome the rail applies live and owns outright
    /// (the same category as <c>WindowMaximized</c>/<c>WindowLeft</c>/<c>WindowTop</c>, which
    /// <see cref="WindowHost.SavePlacement"/> already persists the same way), not a setting a
    /// 设置 card exposes. There is therefore no card to roll back or reload on a failed save, and no
    /// other subscriber that needs telling, so a failed save is logged and otherwise ignored — exactly
    /// like a failed placement flush — rather than surfaced as a blocking error dialog for what is a
    /// purely cosmetic layout choice.
    /// </summary>
    private async void OnNavToggleClick(object? sender, RoutedEventArgs e)
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        cfg.Config.General.NavRailExpanded = !cfg.Config.General.NavRailExpanded;
        ApplyNavExpandedState();

        // Review finding 2: expanding takes 152 DIP away from the content pane and Body's horizontal
        // scrollbar (AppPage.axaml) came straight back; collapsing handed those 152 DIP back as dead
        // space. The rail's width is a term in the window's own content fit, so a change to it is a
        // re-fit trigger exactly like a notice opening. RefitToContent reads the CONFIG value this
        // handler has already flipped, not the pane's live Width, so it measures the animation's stable
        // target rather than an interpolated frame — see WindowHost.FitToContent's railWidth remarks.
        RefitToContent();

        try
        {
            await cfg.SaveAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to persist the nav rail expand/collapse choice");
        }
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
    // Title strip: search box
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Task Manager's own measured behaviour: its search box is NOT a fixed width — it is
    /// proportional to the window's client width, and clamped at a maximum. Ratio 0.34x/max 600 DIP
    /// are that measured behaviour (Round 3, Task 1 review), not a guess: <c>SetWindowPos</c>-resized
    /// Task Manager to 5 client widths from 828 to 3378 physical px at this machine's 150% scale and
    /// re-measured its search box (<c>AutomationId="TextBox"</c>) with UI Automation each time —
    /// box-width / client-width held at 33.6%-34.3% up to a client width of roughly 2600 DIP, where the
    /// box stopped growing at a measured ceiling of 899-900 physical px (600 DIP): 828->284 (34.3%),
    /// 1078->366 (34.0%), 1478->500 (33.8%), 1920->647 (33.7%), 2378->800 (33.6%), 2878->900 (clamped),
    /// 3378->899 (clamped). 0.34 is the average of the five UNCLAMPED ratios, not any single one of
    /// them. MinWidth 200 is NOT an observed Task Manager value — its own window cannot shrink far
    /// enough on this machine to exercise a floor — it exists only so the field cannot collapse to
    /// something unusable at this app's own MinWidth (760 DIP, MainWindow.axaml), and was chosen close
    /// to the OLD fixed 280 DIP this replaces.</summary>
    private const double SearchFieldWidthRatio = 0.34;
    private const double SearchFieldMinWidth = 200;
    private const double SearchFieldMaxWidth = 600;

    /// <summary>Called once immediately (constructor, via the <see cref="TopLevel.ClientSizeProperty"/>
    /// observable firing on subscription) and again on every resize. Guards on a non-positive width
    /// because the very first call happens before the window has ever been measured, when ClientSize is
    /// still <see cref="Size.Empty"/> — XAML's own <c>Width="280"</c> (MainWindow.axaml) is what stays on
    /// screen for that one instant, which is why it is still there as a literal rather than removed.</summary>
    private void ApplySearchFieldWidth()
    {
        var clientWidth = ClientSize.Width;
        if (clientWidth <= 0)
            return;

        SearchField.Width = Math.Clamp(clientWidth * SearchFieldWidthRatio, SearchFieldMinWidth, SearchFieldMaxWidth);
    }

    /// <summary>Focus is drawn on the FIELD (the bordered box), not on the bare TextBox inside it, so
    /// the accent outline frames the glyph too. The TextBox has no border of its own to light up.</summary>
    private void OnSearchFocusChanged(object? sender, RoutedEventArgs e) =>
        SearchField.Classes.Set("focused", SearchBox.IsFocused);

    /// <summary>Enter launches the first match, Escape clears then hides, Down enters the tile grid —
    /// exactly AppPage's old header behaviour. Enter/Escape are one-line calls straight into the shared
    /// LauncherViewModel; Down has to reach AppPage's own tile grid, which only AppPage's code-behind
    /// can walk, so it is forwarded through <see cref="AppPage.FocusFirstTile"/> rather than duplicated
    /// here.</summary>
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
    /// Before the search box moved into this always-attached title strip, <c>AppPage</c> guarded the
    /// equivalent case with <c>IsAttachedToVisualTree()</c> — the whole page, search box included, was
    /// detached whenever 设置 was on screen, so a pending request there was simply invisible. The
    /// search box has no such attachment signal to reuse now, so the guard is asking the one thing that
    /// actually determines whether the user can see it: whether <see cref="_appPage"/> is the page
    /// <see cref="PageHost"/> is currently showing. Left SET otherwise; <see cref="ShowPage"/> retries
    /// this the next time the rail switches back to 应用, exactly the "re-check what needs the visual
    /// tree on re-attach" rule AppPage's own pre-move mechanism followed.
    /// </summary>
    private void ConsumePendingFocus()
    {
        if (!_appPage.ViewModel.PendingFocus)
            return;

        if (!ReferenceEquals(PageHost.Content, _appPage))
            return;

        _appPage.ViewModel.PendingFocus = false;
        try
        {
            SearchBox.Focus();
        }
        catch
        {
            /* focus is a nicety; never let it take startup down */
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Navigation rail: the four relocated commands (Round 3, Task 2 — used to be the title strip's
    // ⋯ overflow MenuFlyout; MainWindow.axaml's rail comment explains why they are Buttons, not
    // ListBoxItems, and why each is its own row rather than one collapsed entry). Method bodies are
    // unchanged from the overflow-menu era: only what invokes them moved.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _appPage.ViewModel.RefreshAsync();

    private void OnAboutClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenAbout();

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
