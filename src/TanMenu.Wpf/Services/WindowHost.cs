using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the main window: restores its saved placement, persists changes, fits it to its content, and
/// handles show/hide/toggle plus opt-in hide-on-blur.
/// </summary>
/// <remarks>
/// Until 0.11 this class measured the rendered content, clamped it to the work area and placed a
/// content-sized popup bottom-center on every reveal; from 0.12 through Round 2's Task 1-3 the window
/// was an ordinary, fixed-then-user-resized application window and none of that ran. Round 2's Task 4
/// brought content measurement back, and its review round fixed it to run on every SUMMON, not only the
/// process's first one: <see cref="RestorePlacement"/> restores POSITION always, and the best size it
/// knows — the user's own last manually-resized size for this process, else the last size a fit landed
/// on, else the PLACEHOLDER (never <c>GeneralConfig.WindowWidth</c>/<c>WindowHeight</c> — see those
/// properties' own remarks for why); <see cref="FitToContent"/> then measures the actual page and
/// resizes the window to it, clamped to the work area exactly like a restored size always was — UNLESS
/// the user has already taken control of the size by resizing it by hand, in which case it steps aside
/// for the rest of the process. See <see cref="_userResized"/>'s own remarks for the full rule.
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    /// <summary>MainWindow.axaml's own <c>ExtendClientAreaTitleBarHeightHint</c> — the title strip's
    /// fixed height, which sits ABOVE the page host and is therefore not part of what a page's own
    /// content measures. Duplicated here as a constant rather than read off the live window because
    /// nothing on <c>Window</c> exposes it as a public property; <see cref="FitToContent"/> adds it back
    /// on top of the measured page size to get the window's own full content-driven height.</summary>
    private const double TitleStripHeightDip = 48;

    /// <summary>Placeholder Width/Height <see cref="RestorePlacement"/> applies on the very FIRST show
    /// of the process, before the launcher's startup build — an async disk scan — has produced anything
    /// for <see cref="FitToContent"/> to measure. Matches MainWindow.axaml's own declared Width/Height,
    /// so there is nothing visually jarring to reconcile once the real, content-driven size lands moments
    /// later. Fixed constants, NOT read from <c>GeneralConfig.WindowWidth</c>/<c>WindowHeight</c>, which
    /// Round 2 retired. Superseded on every LATER summon by <see cref="_lastFitWidthDip"/>/
    /// <see cref="_lastFitHeightDip"/> (the size the previous fit landed on) or, once the user has
    /// manually resized this process, by <see cref="_lastNormalWidthDip"/>/<see cref="_lastNormalHeightDip"/>
    /// — see those fields' own remarks.</summary>
    private const double PlaceholderWidthDip = 1000;
    private const double PlaceholderHeightDip = 688;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly IShellNavigator _navigator;
    private readonly DispatcherTimer _saveDebounce;
    private Window? _window;
    private int _suppressDepth;
    private bool _restoring;

    /// <summary>
    /// True once the user has genuinely resized the window by dragging — detected from
    /// <see cref="Window.Resized"/>'s own <see cref="WindowResizeReason"/> (see <see cref="OnWindowResized"/>),
    /// NOT from comparing Width/Height against a remembered baseline. Sticky for the rest of the
    /// PROCESS, never cleared: this is the fix for the review finding that a mid-session resize used to
    /// be silently discarded on the next summon (<see cref="RestorePlacement"/> reset to the placeholder,
    /// and a one-shot guard in <c>MainWindow</c> then skipped re-fitting away from it forever after,
    /// which was itself based on the false premise that RestorePlacement preserved a resize it was
    /// actually already destroying). Once true, <see cref="RestorePlacement"/> restores the REMEMBERED
    /// size instead of the placeholder, and <see cref="FitToContent"/> stops re-fitting entirely — the
    /// user's own explicit choice wins, the same rule <c>GeneralConfig.WindowMaximized</c> already gets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Review fix, round 2: an EARLIER version of this detected a manual resize by comparing the live
    /// Width/Height against a "what we last applied" baseline with <c>!=</c>. That heuristic can latch on
    /// its OWN output, permanently and silently, with no code path anyone dragged anything. Traced through
    /// decompiled Avalonia 12.1.0: <c>Win32.WindowImpl.Resize</c> truncates DIP to physical via
    /// <c>(int)(value.Width * RenderScaling)</c> before <c>SetWindowPlacement</c> — real precision loss,
    /// not a rounding no-op. <c>Window.HandleResized</c> then writes that truncated-and-converted-back
    /// client size straight back to <c>Width</c>/<c>Height</c> for ANY resize reason, including our OWN
    /// <c>Application</c>-reason writes from <see cref="RestorePlacement"/>/<see cref="FitToContent"/> —
    /// and that write-back is deferred to a LATER layout/render tick by <c>LayoutManager</c>, i.e. it
    /// lands after <see cref="_restoring"/> has already been reset to false in their own <c>finally</c>
    /// block. A truncation round-trip landing on a different <c>double</c> than the one we originally
    /// computed would therefore have reached <c>PropertyChanged</c> with <c>_restoring == false</c> and
    /// permanently mis-set this field — content-dependent and machine-dependent (whether
    /// <c>1239.333… × 1.5</c> lands on exactly <c>1859.0</c> or on <c>1858.999…</c> decides whether it
    /// trips), and with no log line to show it happened.
    /// </para>
    /// <para>
    /// Avalonia already computes the distinction we actually need and exposes it on the event itself:
    /// <see cref="WindowResizeReason.User"/> is set ONLY between the platform's <c>WM_ENTERSIZEMOVE</c>
    /// and <c>WM_EXITSIZEMOVE</c> — an actual interactive drag — confirmed in decompiled
    /// <c>Win32.WindowImpl</c>. Our own placement code never enters that interactive loop, so gating on
    /// the reason removes the truncation round-trip from the decision entirely instead of trying to
    /// detect its symptom after the fact.
    /// </para>
    /// </remarks>
    private bool _userResized;

    /// <summary>The size to restore to once <see cref="_userResized"/> is true — the window's own client
    /// size (DIP) at the moment <see cref="OnWindowResized"/> saw <see cref="WindowResizeReason.User"/>,
    /// taken straight from that event's own <c>ClientSize</c> rather than re-read off <c>Width</c>/
    /// <c>Height</c> afterward. Not written by <see cref="RestorePlacement"/>/<see cref="FitToContent"/>
    /// any more (an earlier version kept them in sync purely to give the old float-comparison detection a
    /// baseline — see <see cref="_userResized"/>'s own remarks for why that approach was replaced).
    /// Seeded to the placeholder size, which is irrelevant until <see cref="_userResized"/> is true —
    /// <see cref="RestorePlacement"/> never reads this pair at all before that. What it reads INSTEAD,
    /// before the first manual resize, is not always the placeholder any more: since the placeholder-flash
    /// fix it is <see cref="_lastFitWidthDip"/>/<see cref="_lastFitHeightDip"/> once a fit has run, falling
    /// back to <see cref="PlaceholderWidthDip"/>/<see cref="PlaceholderHeightDip"/> only on the process's
    /// genuinely first show (see <see cref="RestorePlacement"/>'s own remarks).</summary>
    private double _lastNormalWidthDip = PlaceholderWidthDip;
    private double _lastNormalHeightDip = PlaceholderHeightDip;

    /// <summary>The size the most recent successful <see cref="FitToContent"/> landed on, or null until
    /// one has run. <see cref="RestorePlacement"/> applies it as the PRE-SHOW size on every later summon,
    /// which is the whole point: the correction a fit makes only reaches the platform on the next layout
    /// pass, i.e. after <see cref="Window.Show"/> has already put the window on screen, so without this
    /// every summon painted the 1000x688 placeholder for a frame and then snapped to the real size —
    /// reintroducing, for SIZE, exactly the paint-then-jump this class already goes out of its way to
    /// avoid for POSITION (see <see cref="RestorePlacement"/>'s own remarks).</summary>
    /// <remarks>
    /// Deliberately a SEPARATE pair from <see cref="_lastNormalWidthDip"/>/<see cref="_lastNormalHeightDip"/>
    /// rather than a reuse of them. Those two belong to the <see cref="_userResized"/> contract and mean
    /// "the size the USER chose"; writing a fitted size into them would make a later
    /// <see cref="RestorePlacement"/> unable to tell an automatic size from a deliberate one, and would
    /// resurrect the very baseline-drift class of bug <see cref="_userResized"/>'s remarks describe.
    /// These two mean only "the last size we computed ourselves" and are read ONLY when
    /// <see cref="_userResized"/> is false.
    /// </remarks>
    private double? _lastFitWidthDip;
    private double? _lastFitHeightDip;

    /// <summary>True when something OTHER than this class resized the window, so
    /// <see cref="_lastFitWidthDip"/>/<see cref="_lastFitHeightDip"/> still describe the last size we
    /// requested but no longer describe the window on screen. While it is set,
    /// <see cref="FitToContent"/> refuses to short-circuit even when the geometry it computes matches
    /// that pair — otherwise the very next content-unchanged re-fit trigger (a rail toggle, a 每列个数
    /// change) would decide "nothing to do" and leave the foreign size in place forever.</summary>
    /// <remarks>
    /// <para>
    /// Round 3 residual review. Comparing against the previous fit's own REQUEST rather than the live
    /// window (the fix this class needed, for the reasons <see cref="FitToContent"/> documents) traded
    /// one bug for a smaller one: the live-size comparison self-healed after any foreign resize,
    /// because the live size no longer matched anything we had computed. A request-based basis does
    /// not, so the foreign resize has to be OBSERVED instead of inferred. This flag is that
    /// observation, kept deliberately separate from nulling the pair itself:
    /// <see cref="RestorePlacement"/> reads that pair as the PRE-SHOW size on the next summon, and
    /// clearing it would bring back the 1000x688 placeholder flash it exists to prevent.
    /// </para>
    /// <para>
    /// Set ONLY for <see cref="WindowResizeReason.Unspecified"/> (see <see cref="OnWindowResized"/> for
    /// which real gestures that covers and why the other reasons are excluded), and cleared as soon as
    /// a fit has actually applied a geometry — at which point the window and the basis agree again by
    /// construction. A spurious set therefore costs exactly one redundant re-apply of an
    /// already-correct geometry, never a cascade: our own re-apply carries
    /// <see cref="WindowResizeReason.Application"/>, which never sets this.
    /// </para>
    /// </remarks>
    private bool _fitBasisStale;

    public WindowHost(ConfigService config, AppEvents events, IShellNavigator navigator)
    {
        _config = config;
        _events = events;
        _navigator = navigator;

        // Coalesces the PositionChanged/size storm a single drag produces into one disk write.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) => FlushPlacement();
    }

    public Window? Window => _window;

    /// <summary>Bind the host to the main window. Idempotent — re-attaching the same window is a no-op,
    /// so it does not matter whether startup or the window's own code-behind calls it.</summary>
    public void Attach(Window window)
    {
        if (ReferenceEquals(_window, window))
            return;

        _window = window;

        window.Deactivated += (_, _) =>
        {
            // Config is read live on every deactivation (never cached) so toggling 失焦自动隐藏 in
            // settings takes effect immediately. Off by default since 0.12.
            if (_config.Config.General.AutoClose && _suppressDepth == 0)
                Hide();
        };

        // 窗口置顶 has to reach a window that is already on screen. Restore applies it too, but that
        // only runs on a cold start or an un-hidden window, so without this the checkbox would appear
        // to do nothing until the next hide/show cycle.
        _events.SettingsChanged += ApplyTopmost;
        ApplyTopmost();

        // Persist geometry as the user changes it rather than only on exit: the process can be ended
        // by an update handoff or a hard kill, and a lost window position is a visible annoyance.
        // _restoring guards against writing back the values we are in the middle of applying.
        window.PositionChanged += (_, _) => { if (!_restoring) SchedulePlacementSave(); };
        window.PropertyChanged += (_, e) =>
        {
            if (_restoring)
                return;
            if (e.Property == Window.WindowStateProperty ||
                e.Property == Window.WidthProperty ||
                e.Property == Window.HeightProperty)
                SchedulePlacementSave();
        };

        // The authoritative "did the user resize this" signal — see _userResized's own remarks for why
        // this replaced a float-comparison heuristic against Width/Height.
        window.Resized += OnWindowResized;
    }

    /// <summary>See <see cref="_userResized"/>'s own remarks for the full reasoning. Only
    /// <see cref="WindowResizeReason.User"/> — set by Avalonia's Win32 backend only between
    /// <c>WM_ENTERSIZEMOVE</c> and <c>WM_EXITSIZEMOVE</c>, i.e. an actual interactive drag — ever sets
    /// <see cref="_userResized"/>. <c>Unspecified</c>/<c>Application</c>/<c>Layout</c>/<c>DpiChange</c> are
    /// all explicitly NOT treated as a manual resize, including our own <see cref="RestorePlacement"/>/
    /// <see cref="FitToContent"/> writes (which additionally never reach here at all while
    /// <see cref="_restoring"/> is true, guarded the same as every other listener in <see cref="Attach"/>
    /// — real interactive drags never happen while that flag is set, so this is defence in depth, not
    /// load-bearing). <c>Unspecified</c> does, separately, mark <see cref="_fitBasisStale"/> — a weaker
    /// statement than "the user chose a size", meaning only "somebody other than us changed the size";
    /// see this method's last remark paragraph.</summary>
    /// <remarks>
    /// <para>
    /// The record is POSTED to the dispatcher rather than taken inline, and the window state is re-read
    /// there. Review finding: this method used to record <c>e.ClientSize</c> as the remembered NORMAL
    /// size with no <see cref="WindowState"/> filter at all, unlike every other writer in this class.
    /// Dragging a window to the top edge to snap-maximize happens INSIDE the interactive move loop, so
    /// the resulting <c>WM_SIZE(SIZE_MAXIMIZED)</c> arrives with <see cref="WindowResizeReason.User"/>
    /// and the screen-filling client size was stored as the size to restore to — and latched
    /// <see cref="_userResized"/> along with it, permanently disabling content-fitting.
    /// </para>
    /// <para>
    /// Reading <c>_window.WindowState</c> inline would not have caught it: in decompiled Avalonia 12.1.0's
    /// Win32 <c>WM_SIZE</c> handler, <c>Resized(...)</c> — which raises THIS event — is invoked BEFORE
    /// <c>WindowStateChanged?.Invoke(...)</c>, so the property still reports the stale, pre-transition
    /// value while this handler is on the stack. Posting defers the read until the whole synchronous
    /// transition has finished, exactly as <see cref="SchedulePlacementSave"/> already does for the same
    /// ordering hazard on the save side.
    /// </para>
    /// <para>
    /// A snap-maximize is therefore now ignored outright — not merely excluded from the size record.
    /// That is deliberate and consistent for the MAXIMIZE direction: maximizing is already remembered by
    /// <c>GeneralConfig.WindowMaximized</c>, restores maximized, and re-fits to content the moment the
    /// user un-maximizes (<c>MainWindow.OnPropertyChanged</c>), so treating it as "the user has chosen a
    /// size" would only strip the restored window of its content fit.
    /// </para>
    /// <para>
    /// The RESTORE direction is not so clean, and is not fixed here — this method's behaviour is
    /// unchanged, only documented. Dragging a MAXIMIZED window's title bar down far enough to restore it
    /// also happens inside <c>WM_ENTERSIZEMOVE</c>/<c>WM_EXITSIZEMOVE</c>, so the resulting
    /// Maximized→Normal transition also arrives with <see cref="WindowResizeReason.User"/>. The content
    /// re-fit itself survives that: <c>MainWindow.OnPropertyChanged</c> posts its <c>RefitToContent</c> at
    /// <see cref="DispatcherPriority.Loaded"/>, while this method posts <see cref="RecordUserResize"/>
    /// with no explicit priority at all, i.e. <see cref="DispatcherPriority.Default"/> — a LOWER priority
    /// than <c>Loaded</c>, so the re-fit is dequeued and runs first even though both are queued from the
    /// same synchronous transition. But <see cref="RecordUserResize"/> still runs after it, on the same
    /// drag, and by then <c>_window.WindowState</c> already reads <c>Normal</c>, so its guard does not
    /// reject it: this
    /// latches <see cref="_userResized"/> to true and records whatever client size Windows itself chose
    /// for the restore, not the fit's. The one re-fit that already ran keeps its result on screen, but
    /// every LATER re-fit trigger for the rest of the process — a rail toggle, a 每列个数 change, another
    /// un-maximize — is silently disabled from this point on, exactly as a genuine manual resize disables
    /// it. This is pre-existing behaviour, not introduced by this remark, and the user has not been asked
    /// whether a maximize-then-drag-to-restore gesture should count as "I chose a size" the same way an
    /// ordinary drag does — so it is left as is here rather than changed unasked.
    /// </para>
    /// <para>
    /// Round 3 residual review — the <see cref="WindowResizeReason.Unspecified"/> branch. It is NOT a
    /// manual resize (it must not latch <see cref="_userResized"/>) but it IS a size this class did not
    /// ask for, which matters now that <see cref="FitToContent"/>'s skip test compares against the
    /// previous fit's own request instead of the live window. Which gestures reach it was established by
    /// decompiling Avalonia 12.1.0 rather than guessed: <c>Win32.WindowImpl</c> keeps one
    /// <c>_resizeReason</c> field that <c>WM_SIZE</c> hands straight to <c>Resized</c>; it is
    /// <c>User</c> only between <c>WM_ENTERSIZEMOVE</c> and <c>WM_EXITSIZEMOVE</c>, <c>DpiChange</c>
    /// only inside <c>WM_DPICHANGED</c>'s own scope, and <c>Application</c>/<c>Layout</c> only inside
    /// the scope <c>WindowImpl.Resize</c> wraps its <c>SetWindowPlacement</c> in — which is the only way
    /// this class's own <c>Width</c>/<c>Height</c> writes reach the platform
    /// (<c>Window</c>'s <c>ClientSize</c> observable calls <c>ResizePlatformImpl(x, Application)</c>).
    /// Everything else — a Win+arrow snap, an external <c>SetWindowPos</c>, a caption-button
    /// un-maximize — falls through with the field at its default, <c>Unspecified</c>. So gating the
    /// stale-basis mark on <c>Unspecified</c> catches exactly the foreign resizes and cannot be tripped
    /// by our own, which is what makes it incapable of cascading (see <see cref="_fitBasisStale"/>).
    /// <c>DpiChange</c> is deliberately EXCLUDED: the OS rescales the window so its DIP size is
    /// preserved, so a later fit computing the same DIP figure genuinely has nothing to apply and
    /// skipping is the correct answer, not a missed correction.
    /// </para>
    /// </remarks>
    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (_restoring)
            return;

        if (e.Reason == WindowResizeReason.Unspecified)
        {
            // Not "the user chose a size" (that is _userResized's job, and only a real drag earns it) —
            // only "the window is no longer the size we last computed", so the next fit must not
            // short-circuit on a basis that has stopped describing the window.
            _fitBasisStale = true;
            return;
        }

        if (e.Reason != WindowResizeReason.User)
            return;

        var size = e.ClientSize;
        Dispatcher.UIThread.Post(() => RecordUserResize(size));
    }

    private void RecordUserResize(Size clientSize)
    {
        // Not a normal-state resize after all (snap-maximize, or a maximize begun inside the move loop):
        // there is no "size the user chose" to remember here.
        if (_window is null || _window.WindowState != WindowState.Normal)
            return;

        // Win32 sends WM_SIZE throughout the modal drag loop, so a single drag raises the source event
        // tens-to-hundreds of times. The line below is about the TRANSITION, not about each frame — it
        // was written unguarded and buried the log under one line per frame. The size assignment stays
        // unconditional: every frame's size is still the latest one to remember.
        if (!_userResized)
        {
            // _userResized is never cleared for the rest of the process (see its own remarks) — a WRONG
            // latch here silently disables content-fitting for the rest of the session with nothing else
            // to point at, which is exactly what the review round's Critical finding was about. Logged at
            // Information specifically so that transition is never silent again.
            Serilog.Log.Information(
                "User resize detected: {Width}x{Height} DIP; content-fit will not run again this process",
                clientSize.Width, clientSize.Height);
        }

        _userResized = true;
        _lastNormalWidthDip = clientSize.Width;
        _lastNormalHeightDip = clientSize.Height;
    }

    /// <summary>Push the current 窗口置顶 setting onto a live window.</summary>
    private void ApplyTopmost()
    {
        if (_window is not null)
            _window.Topmost = _config.Config.General.TopMost;
    }

    /// <summary>Last-resort work area when the platform reports no screens at all. Only reachable on a
    /// headless/remote session where <see cref="Screens"/> is empty.</summary>
    private static readonly PlacementRect UnknownWorkArea = new(0, 0, 1920, 1040);

    /// <summary>The screen the window currently sits on, falling back to the primary screen and then to
    /// the first enumerated one. Null when no screen information is available yet.</summary>
    /// <remarks>
    /// For SAVING and for <see cref="FitToContent"/> — anything that runs AFTER the window has a real
    /// position. Do NOT use this to restore: at restore time (<see cref="RestorePlacement"/>) the window
    /// has not been placed yet, so this reports wherever Win32 cascaded it (the primary) rather than the
    /// monitor the saved rectangle belongs to. <see cref="ResolveTargetScreen"/> is the restore-side
    /// answer; <see cref="FitToContent"/> can use this one instead because it only ever runs once the
    /// window has already been shown at a real, placed position.
    /// </remarks>
    private Screen? CurrentScreen()
    {
        var screens = _window?.Screens;
        return (_window is not null ? screens?.ScreenFromWindow(_window) : null)
               ?? screens?.Primary
               ?? screens?.All.FirstOrDefault();
    }

    /// <summary>Pixels-per-DIP of the screen the window is on RIGHT NOW. Deliberately NOT cached:
    /// dragging the window from a 100% monitor to a 150% one changes it, and converting a position with
    /// a stale scale records a placement wrong by the ratio between the two.</summary>
    private double CurrentScale()
    {
        var scaling = CurrentScreen()?.Scaling ?? 1.0;
        return scaling <= 0 ? 1.0 : scaling;
    }

    private static double SafeScale(Screen screen) => screen.Scaling <= 0 ? 1.0 : screen.Scaling;

    /// <summary>A screen's work area in DIPs, converted with THAT screen's own scaling — the exact
    /// inverse of what <see cref="SavePlacement"/> records.</summary>
    private static PlacementRect WorkAreaOf(Screen screen)
    {
        var scale = SafeScale(screen);
        var w = screen.WorkingArea;
        return new PlacementRect(w.X / scale, w.Y / scale, w.Width / scale, w.Height / scale);
    }

    /// <summary>
    /// Choose the monitor a SAVED placement should be restored onto, and hand back both its work area
    /// (DIPs) and its scaling. Falls back to a 1920×1040 guess at scale 1 when the platform reports no
    /// screens.
    /// </summary>
    /// <remarks>
    /// The choice is driven by the saved rectangle, never by the window's current position: the window
    /// has not been placed yet when this runs, so its position is whatever Win32 cascaded it to. The
    /// decision itself lives in <see cref="WindowPlacement.ChooseWorkArea"/> so it can be unit-tested
    /// without a display; this method only translates Avalonia's screen list into and out of it. The
    /// scaling returned is the TARGET screen's, not the current one's — converting a saved DIP position
    /// with the wrong monitor's scale lands the window off by the ratio between the two.
    /// </remarks>
    private (PlacementRect Work, double Scale) ResolveTargetScreen(PlacementRect? saved)
    {
        var all = _window?.Screens?.All;
        if (all is null || all.Count == 0)
            return (UnknownWorkArea, 1.0);

        var areas = new PlacementRect[all.Count];
        var primaryIndex = 0;
        for (var i = 0; i < all.Count; i++)
        {
            areas[i] = WorkAreaOf(all[i]);
            if (all[i].IsPrimary)
                primaryIndex = i;
        }

        var index = WindowPlacement.ChooseWorkArea(saved, areas, primaryIndex);
        return index < 0 ? (UnknownWorkArea, 1.0) : (areas[index], SafeScale(all[index]));
    }

    /// <summary>The remembered window RECTANGLE in DIPs, or null when there is no remembered position (a
    /// fresh config). Null is what tells <see cref="WindowPlacement.ChooseWorkArea"/> to centre on the
    /// primary rather than hunt for a monitor.</summary>
    /// <param name="width">The size <see cref="RestorePlacement"/> is ABOUT TO APPLY this same call —
    /// the placeholder, or the user's last manually-resized size — not a remembered size of its own
    /// (Round 2 retired persisting one; see <c>GeneralConfig.WindowWidth</c>'s own remarks).</param>
    /// <param name="height">As <paramref name="width"/>, vertically.</param>
    /// <remarks>
    /// Review fix: this used to hardcode (0, 0) here, which collapses <see cref="WindowPlacement.ChooseWorkArea"/>'s
    /// CENTRE test to exactly (Left, Top) — the saved rectangle's ORIGIN, not its centre.
    /// <c>WindowPlacement.cs</c>'s own remarks document why that inversion is wrong on purpose: "a window
    /// the user dragged so its left edge hangs slightly off a monitor still belongs to that monitor, and
    /// an origin test would hand it to the neighbour". Passing the real, about-to-be-applied footprint
    /// restores the invariant instead of silently inverting it — the saved rectangle's centre can now
    /// genuinely differ from its top-left corner.
    /// </remarks>
    private PlacementRect? SavedRect(double width, double height)
    {
        var g = _config.Config.General;
        return g.WindowLeft is { } left && g.WindowTop is { } top
            ? new PlacementRect(left, top, width, height)
            : null;
    }

    /// <summary>
    /// Defer to the next dispatcher cycle instead of calling <see cref="SavePlacement"/> inline from
    /// the property-changed handler above.
    /// </summary>
    /// <remarks>
    /// A maximize transition raises WidthProperty/HeightProperty-changed — carrying the new,
    /// screen-filling values — BEFORE WindowStateProperty flips to Maximized, all synchronously
    /// within the same WM_SIZE handling. Calling SavePlacement synchronously from the Width/Height
    /// handler therefore reads a still-stale WindowState of Normal, and its "leave the remembered
    /// normal position alone while maximized" guard sees Normal and wrongly persists the maximized,
    /// near-zero-origin position as the restore geometry — confirmed by launching the window, maximizing
    /// it, and closing (hiding) it while still maximized: WindowLeft/Top were overwritten with the
    /// maximized position instead of being left at the pre-maximize one (this was originally confirmed
    /// back when WindowWidth/Height were still recorded here too and showed the identical symptom; Round
    /// 2 retired both, but the WindowState-ordering hazard this guards against is exactly the same one
    /// for Left/Top). Posting defers the read until the whole synchronous transition has finished, so by
    /// the time SavePlacement runs, WindowState already reflects its final value.
    /// </remarks>
    private void SchedulePlacementSave() => Dispatcher.UIThread.Post(SavePlacement);

    /// <summary>Apply the saved POSITION, clamped to the work area of the monitor the SAVED rectangle
    /// belongs to, at either the placeholder SIZE or the user's own last manually-resized size. Never
    /// throws: a failure here must not prevent the window from being shown.</summary>
    /// <remarks>
    /// Called BEFORE <see cref="Window.Show"/> on a cold start (<c>App.ShowLauncherWindow</c>) and
    /// before every un-hide (<see cref="ShowAndActivate"/>), never afterwards: applying geometry to a
    /// window that is already on screen paints it once at the OS cascade position and then jumps it,
    /// which is exactly what deleting the flyout's <c>Opacity="0"</c> assumed could not happen.
    /// <para>
    /// The SIZE this applies is the best one known BEFORE the window is on screen, in descending order
    /// of authority: the user's own last manual resize this process (<see cref="_userResized"/>) —
    /// restoring anything else would only be overwritten by their choice again, and applying it directly
    /// is what makes a mid-session resize actually survive a hide/summon cycle (review finding: it did
    /// not, before that fix); else the size the last <see cref="FitToContent"/> landed on
    /// (<see cref="_lastFitWidthDip"/>); else <see cref="PlaceholderWidthDip"/>/<see cref="PlaceholderHeightDip"/>,
    /// which is only ever reached on the process's FIRST show, when the launcher's content genuinely is
    /// not known yet (its startup build is an async disk scan that only begins once the window is shown).
    /// The middle rung is the review fix for finding 6: <see cref="FitToContent"/>'s correction reaches
    /// the platform on the NEXT layout pass, i.e. after Show() has already painted, so restoring the
    /// placeholder here made every summon flash 1000x688 before snapping to the real size.
    /// </para>
    /// </remarks>
    public void RestorePlacement()
    {
        if (_window is null)
            return;

        _restoring = true;
        try
        {
            var g = _config.Config.General;

            var width = _userResized ? _lastNormalWidthDip : _lastFitWidthDip ?? PlaceholderWidthDip;
            var height = _userResized ? _lastNormalHeightDip : _lastFitHeightDip ?? PlaceholderHeightDip;

            var (work, scale) = ResolveTargetScreen(SavedRect(width, height));

            var placement = WindowPlacement.Resolve(
                g.WindowLeft, g.WindowTop, width, height,
                work, _window.MinWidth, _window.MinHeight);

            // NOTE (Round 3 residual review): this deliberately does NOT update _lastFitWidthDip/
            // _lastFitHeightDip, even though it changes the window's size. That is safe, and the
            // reasoning has to hold across DIFFERENT work areas — the only situation in which the
            // placement resolved here can differ from the pair at all — so here it is in full.
            //
            // The hazard being ruled out is precisely one thing: this method leaving the window at a
            // size S that differs from the pair B, and a later FitToContent then computing exactly B,
            // matching the basis and SKIPPING, so the window is stranded at S. Three exhaustive cases:
            //
            //  1. _userResized is true. S comes from _lastNormalWidthDip/_lastNormalHeightDip and can
            //     be anything, but FitToContent's own first guard returns immediately for the rest of
            //     the process, so no fit can skip and nothing can be stranded.
            //  2. _userResized is false and the pair is null (the process's genuinely first show). S is
            //     the placeholder; the basis is null, and FitToContent treats null as "different", so
            //     the first fit is never skipped.
            //  3. _userResized is false and the pair is B. Then S = Resolve(B, thisWorkArea), and S can
            //     only differ from B where Resolve's clamp moved it: Resolve caps width/height at
            //     workArea.Width/Height and floors them at Math.Min(min, workArea.*), so S != B implies
            //     either B > workArea.* (clamped DOWN, S = workArea.*) or B < Math.Min(min, workArea.*)
            //     (floored UP, S = that floor). A subsequent FitToContent runs ResolveContentFit
            //     against the SAME work area, i.e. the same clamp: its result is likewise <= workArea.*
            //     and >= that same floor. So in the clamped-down case the fit cannot produce B (that
            //     would require a result above the cap it just applied), and in the floored-up case it
            //     cannot produce B either (that would require a result below the floor). Either way the
            //     fit computes something != B, so it cannot match the basis and cannot skip: the window
            //     is corrected on the very next fit rather than stranded. (Strictly, IsSamePlacement
            //     compares to within 0.5 DIP, so the fit could still "match" if the clamp moved the
            //     size by LESS than that — in which case S is within half a DIP of B and there is
            //     nothing left to strand.)
            //
            // Updating the pair here instead was considered and rejected: it would write a work-area-
            // clamped size into the field RestorePlacement itself reads as the next summon's PRE-SHOW
            // size, so summoning back onto a larger monitor would paint the smaller clamped size first
            // and jump — reintroducing exactly the placeholder-flash defect this pair exists to fix.
            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));
            _window.WindowState = g.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            ApplyTopmost();

            // Logged at Information, not Debug: this is the only externally visible record of WHICH
            // monitor was chosen and at what scale, and it is what proves the placement was applied
            // before the window was shown (the "Launcher window shown" line follows it).
            Serilog.Log.Information(
                "Window placement applied: {Left},{Top} {Width}x{Height} DIP on work area " +
                "{WorkLeft},{WorkTop} {WorkWidth}x{WorkHeight} at scale {Scale}",
                placement.Left, placement.Top, placement.Width, placement.Height,
                work.Left, work.Top, work.Width, work.Height, scale);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to restore window placement; using defaults");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Resize the window to fit <paramref name="pageContent"/>'s natural size, clamped to the work area
    /// of the screen the window is CURRENTLY on. Position is preserved (only re-clamped if the new,
    /// content-driven size would otherwise push the window off screen — see the remarks). Called on
    /// EVERY summon, not only a cold start (see the class remarks), and again on every other event that
    /// changes what the active page measures: a docked notice opening or closing, the navigation rail
    /// expanding or collapsing, a 每列个数 / 字体 / 语言 change, a switch back to 应用 from 设置, and the
    /// Maximized→Normal transition. It is a cheap, idempotent re-measure, not a one-time setup step, and
    /// a re-fit that computes the geometry the PREVIOUS fit already requested
    /// (<see cref="_lastFitWidthDip"/>/<see cref="_lastFitHeightDip"/>, compared with
    /// <see cref="WindowPlacement.IsSamePlacement"/>) writes nothing at all, so none of those triggers
    /// can cascade. Note the basis: it is the last size this class ASKED FOR, not the size the window
    /// currently has — those two legitimately differ whenever the platform clamps a request (this
    /// window has a practical ~605 DIP outer height floor; see the comment on the check itself), which
    /// is exactly why comparing against the live window used to make the skip unreachable. A foreign
    /// resize therefore has to be observed rather than inferred from the live size, which is
    /// <see cref="_fitBasisStale"/>'s whole job.
    /// </summary>
    /// <param name="pageContent">The active page whose natural size drives the window's. It is the
    /// CALLER's job to pass the page that is actually hosted right now and to skip the call otherwise —
    /// <c>MainWindow.RefitToContent</c> does exactly that. (An earlier revision of this remark asserted
    /// this was always the launcher "on every path that reaches this method, since the window always
    /// opens on 应用". That was false: the tray's 选项 entry navigates to 设置 and THEN shows the window,
    /// so <c>OnOpened</c> re-fired with the launcher already detached from the logical tree, and
    /// measuring a detached page permanently un-styles it — see <c>MainWindow.RefitToContent</c>'s own
    /// remarks for the mechanism.) Measured with an
    /// UNBOUNDED width AND height (<see cref="Size"/> of <see cref="double.PositiveInfinity"/> on both
    /// axes) — this is what makes the measurement genuinely content-driven on both axes, per the user's
    /// Round 2 review decision that height should adapt too. Round 2's rule was "the category with the
    /// most items fits in a single column without wrapping, clamped to the work area", enforced by a
    /// height-based vertical WrapPanel that only reported that true, un-wrapped extent when given an
    /// unbounded height budget — handed any FINITE bound instead (this method's own first cut used a
    /// comfortable 640 DIP reference, and an earlier attempt still visible in this method's history used
    /// the whole work area), it just wrapped to avoid exceeding whatever bound it was given and reported
    /// back a number that reflected the BOUND, not the content — the "probe the WrapPanel will happily
    /// fill" failure mode that round's review flagged.
    /// <para>
    /// Round 3's Task 4 changed WHERE the cap lives, not this probe. AppPage.axaml no longer wraps a
    /// category's items with a height-driven WrapPanel at all: <c>MenuGroupVm.Columns</c> — chunked from
    /// its VisibleItems by <see cref="TanMenu.Core.Services.MenuLayout.ChunkIntoColumns{T}"/> at
    /// <c>GeneralConfig.ColButtonCount</c> items per column — decides the wrap point in the DATA before
    /// layout ever runs, and each column renders as a plain vertical StackPanel. A StackPanel's own
    /// DesiredSize is the sum of its children's desired sizes regardless of the available-size budget it
    /// is handed (a general fact about StackPanel, not about this probe specifically), so an item-count
    /// cap decided upstream survives an unbounded height probe unchanged. The rule this method measures
    /// is therefore now "the tallest COLUMN — every category's items already capped at ColButtonCount —
    /// fits without further wrapping, clamped to the work area", and nothing in this method's own
    /// arithmetic needed to change to get it: the unbounded probe this method already used was already
    /// exactly what an item-count-capped layout needs too.
    /// </para>
    /// With every category's tallest column unwrapped, the reported WIDTH is also then exactly what the
    /// FINAL layout needs once the window is actually sized to that column's height (every shorter column
    /// already fits at that same, more generous height too) — so one unbounded probe still answers both
    /// axes consistently. A genuinely oversized column (ColButtonCount set very high on a large category)
    /// still clamps correctly: <see cref="WindowPlacement.Resolve"/> caps the RESULT to the work area
    /// regardless of how tall the probe reported — no special-case wrapping logic needed here either
    /// way.</param>
    /// <param name="railWidth">The navigation rail's CURRENT width — 48 collapsed or 200 expanded. The
    /// caller reads this straight from <c>GeneralConfig.NavRailExpanded</c> rather than the live pane's
    /// own Width/Bounds, so a fit that happens to run while the rail's collapse/expand transition is
    /// still animating measures the STABLE target width, never an interpolated one.</param>
    /// <remarks>
    /// <para>
    /// Callable only after the window has been placed AND shown: unlike <see cref="RestorePlacement"/>,
    /// this needs to ask <see cref="Screens"/> which monitor the window is ACTUALLY on right now
    /// (<see cref="CurrentScreen"/>), which only answers correctly once the window is real — exactly the
    /// same restriction <see cref="CurrentScreen"/>'s own remarks document for <see cref="SavePlacement"/>.
    /// This is also, unavoidably, why fit-to-content cannot run any earlier than it does on a cold start:
    /// the launcher's categories are not known until its async startup build (a disk scan) completes, and
    /// that build cannot start before the window is shown — see <c>AppPage.BeginInitialize</c>'s own
    /// remarks.
    /// </para>
    /// <para>
    /// Skips entirely while the window is currently maximized. Fit-to-content targets the NORMAL-state
    /// (restored) geometry — there is no "restored size" visibly on screen to fit while maximized, and
    /// forcing one would mean toggling WindowState twice in a row (Maximized → Normal → Maximized) purely
    /// to update the invisible restore bounds, which this environment cannot verify is flicker-free
    /// end-to-end. <see cref="WindowState"/> itself is untouched either way, so a maximized start still
    /// restores maximized regardless. Review fix (finding 3): the restore rectangle used to stay the
    /// placeholder forever because nothing re-triggered a fit when the user un-maximized;
    /// <c>MainWindow.OnPropertyChanged</c> now calls back here once on the Maximized→Normal transition,
    /// when there IS a restored window on screen to fit and no state toggle is needed to reach it.
    /// </para>
    /// <para>
    /// ALSO skips once <see cref="_userResized"/> is true — the user has manually resized the window this
    /// process, and that choice wins over any further auto-fit, including one triggered by a notice
    /// appearing. See that field's own remarks for the full rule and the review finding it fixes.
    /// </para>
    /// <para>
    /// POSITION: the window's current top-left is fed back in and re-clamped against the work area, so a
    /// re-fit CAN move the window — but only ever by the amount the new size would otherwise hang off the
    /// work area's right or bottom edge, and never at all when the fit shrinks the window or leaves it
    /// unchanged (<see cref="WindowPlacement.Resolve"/> only moves a rectangle that no longer fits, and
    /// <see cref="WindowPlacement.IsSamePlacement"/> stops an unchanged one reaching the platform at
    /// all). Review finding 7 asked for a deliberate decision here; this is it. The alternative —
    /// pinning Left/Top and letting a grown window hang off the screen edge — trades a small, bounded,
    /// minimum-necessary shift for content the user cannot reach at all, which is strictly worse, and it
    /// would also diverge from what every other placement path in this class does with a rectangle that
    /// no longer fits.
    /// </para>
    /// </remarks>
    public void FitToContent(Layoutable pageContent, double railWidth)
    {
        if (_window is null || _window.WindowState != WindowState.Normal || _userResized)
            return;

        _restoring = true;
        try
        {
            var screen = CurrentScreen();
            var work = screen is null ? UnknownWorkArea : WorkAreaOf(screen);
            var scale = screen is null ? 1.0 : SafeScale(screen);

            // Unbounded on BOTH axes — see this method's own <param> remarks on pageContent for why an
            // unbounded height (not a fixed reference, not the work area) is what makes every category,
            // not only the tallest, report its true single-column extent.
            pageContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = pageContent.DesiredSize;

            var currentLeft = _window.Position.X / scale;
            var currentTop = _window.Position.Y / scale;

            var placement = WindowPlacement.ResolveContentFit(
                desired.Width, desired.Height,
                railWidth, TitleStripHeightDip,
                currentLeft, currentTop,
                work, _window.MinWidth, _window.MinHeight);

            // Round 3 final-fix report, finding 4: compare against the size the PREVIOUS fit
            // requested (captured here, before it is overwritten below), NOT the window's live
            // Width/Height. Task 4's MinHeight="568" can sit below this window's own practical floor —
            // its title strip/chrome mean Windows will not actually let the client area go below
            // roughly 605 DIP outer regardless of what is requested — and Win32.WindowImpl clamps a
            // too-small SetWindowPlacement call up to that floor, then writes the CLAMPED value back
            // into Width/Height on the next layout pass. Comparing against that live, clamped size
            // therefore never matched what a fit had actually asked for (568 vs ~605), so this check
            // could never short-circuit in that regime and every cold start logged and debounce-wrote
            // TWICE instead of once. The previous fit's own requested size is the one thing this class
            // truly controls and can compare against meaningfully; null (nothing has fit yet this
            // process) always counts as "different", so the very first fit of a process is never
            // skipped. _fitBasisStale additionally forces "different" when something outside this class
            // has resized the window since — see that field's own remarks.
            if (!_fitBasisStale &&
                _lastFitWidthDip is { } previousWidthDip && _lastFitHeightDip is { } previousHeightDip)
            {
                var previous = new PlacementRect(currentLeft, currentTop, previousWidthDip, previousHeightDip);
                if (WindowPlacement.IsSamePlacement(placement, previous))
                {
                    // Several triggers can fire for one user action (see IsSamePlacement's own remarks).
                    // Debug, not Information: a fit that changes nothing is not an event worth a line in the
                    // shipped log, but it is worth being able to see when chasing one that should have.
                    Serilog.Log.Debug(
                        "Window fit to content: already {Width}x{Height} DIP; nothing to apply",
                        placement.Width, placement.Height);
                    return;
                }
            }

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));

            // Written only AFTER the three assignments above have all returned, never before them.
            // Round 3 residual review: this pair used to be assigned right after the placement was
            // computed, above the skip test. That is a basis for "the size this window was last asked
            // to be", so recording a size the window was never actually asked to be is a lie that never
            // self-corrects — if any of those three property writes threw, the catch below would log,
            // the geometry would stay where it was, and the basis would nonetheless claim the new
            // value, so EVERY later fit computing that same value would match it and skip, forever. The
            // pre-fix code compared against the live window size and therefore healed on the next fit;
            // a request-based basis has no such luck and has to be sequenced correctly instead. Writing
            // it last also gives a partial apply (Width and Height taken, Position throwing) the safe
            // outcome rather than the sticky one: the basis stays at the previous value, so the next
            // trigger re-runs the whole apply instead of concluding there is nothing to do.
            //
            // The skip path above deliberately does NOT reach here. It applied nothing, so the previous
            // basis is still exactly what the window was last asked for, and it is within the same 0.5
            // DIP tolerance of this placement anyway — RestorePlacement, the pair's other reader, gets
            // the same pre-show size either way.
            _lastFitWidthDip = placement.Width;
            _lastFitHeightDip = placement.Height;
            _fitBasisStale = false; // basis and window agree again by construction

            Serilog.Log.Information(
                "Window fit to content: {Width}x{Height} DIP (page {ContentWidth}x{ContentHeight} + " +
                "rail {RailWidth} + title strip {TitleStrip}) on work area " +
                "{WorkLeft},{WorkTop} {WorkWidth}x{WorkHeight} at scale {Scale}",
                placement.Width, placement.Height, desired.Width, desired.Height, railWidth,
                TitleStripHeightDip, work.Left, work.Top, work.Width, work.Height, scale);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to fit the window to its content; keeping its current size");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Updates the in-memory config immediately but DEBOUNCES the disk write. A single window drag
    /// raises PositionChanged on every mouse move, and persisting each one would rewrite config.json
    /// hundreds of times per drag. <see cref="Hide"/> cancels the debounce and starts the write
    /// immediately (still asynchronously) so a hide does not sit on an unwritten position;
    /// <see cref="FlushPlacementBlocking"/> is the variant 退出 uses, which also WAITS for it.
    /// </remarks>
    public void SavePlacement()
    {
        if (_window is null || _restoring)
            return;

        try
        {
            var state = _window.WindowState;

            // Minimized carries NO placement information: its Width/Height/Position say nothing about
            // what to restore to, and reading it as "not maximized" would erase a maximized window's
            // remembered state the moment the user minimizes it.
            if (state == WindowState.Minimized)
                return;

            var g = _config.Config.General;
            g.WindowMaximized = state == WindowState.Maximized;

            // Width/Height are deliberately NOT written to CONFIG here any more (Round 2 retired
            // GeneralConfig.WindowWidth/WindowHeight — see their own remarks: nothing reads them back on
            // disk, so writing them would just be stale data). Position is still saved only while Normal:
            // a maximized window's Left/Top are the screen's own near-zero origin, not where to restore
            // to, so the remembered "normal" position is deliberately left alone while maximized.
            if (state == WindowState.Normal)
            {
                var scale = CurrentScale();
                g.WindowLeft = _window.Position.X / scale;
                g.WindowTop = _window.Position.Y / scale;
            }

            // A genuine manual resize is detected from Window.Resized's own WindowResizeReason
            // (OnWindowResized), NOT here — see _userResized's own remarks for why a float-equality
            // comparison against a remembered baseline used to live in this method and was replaced.

            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to record window placement");
        }
    }

    /// <summary>Start writing the recorded placement to disk, cancelling any pending debounced write.
    /// FIRE-AND-FORGET — the returned write may still be in flight when this returns. Use
    /// <see cref="FlushPlacementBlocking"/> on any path that is about to end the process.</summary>
    public void FlushPlacement() => _ = FlushPlacementAsync();

    private Task FlushPlacementAsync()
    {
        _saveDebounce.Stop();
        try
        {
            return _config.SaveAsync();
        }
        catch (Exception ex)
        {
            // SaveAsync only throws synchronously if serialization itself fails; a disk failure comes
            // back on the task. Either way a placement write must never take a caller down.
            Serilog.Log.Warning(ex, "Failed to start a window placement flush");
            return Task.CompletedTask;
        }
    }

    /// <summary>Flush the recorded placement and BLOCK until the write has finished. For
    /// <see cref="App.ExitApp"/> only: the fire-and-forget form yields at the first await inside
    /// <c>SaveAsync</c>, and <c>desktop.Shutdown()</c> then ends the dispatcher loop out from under it,
    /// losing any geometry change made inside the 500 ms debounce window and potentially stranding an
    /// orphan <c>config.json.tmp</c>. Blocking here is only deadlock-free because <c>SaveAsync</c>
    /// awaits with <c>ConfigureAwait(false)</c> throughout — see its remarks.</summary>
    public void FlushPlacementBlocking()
    {
        try
        {
            // Bounded rather than indefinite: the data folder can be a network share, and 退出 must
            // still end the process if that share is wedged. A few-KB write never approaches this.
            if (!FlushPlacementAsync().Wait(TimeSpan.FromSeconds(5)))
                Serilog.Log.Warning("Timed out flushing the window placement on exit");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to flush the window placement on exit");
        }
    }

    public void Hide()
    {
        SavePlacement();
        FlushPlacement(); // hiding is often the last thing before exit; do not leave it to the timer
        _window?.Hide();

        // Reset the shell to 应用 AFTER the window is off screen, so the page swap is never visible.
        // Spec §2: "the window always opens on 应用". ShellNavigator.Current is process-lifetime state
        // that survives hide/show, so without this a session that visited 设置 re-summons onto 设置 —
        // with AppPage detached, which also silently kills the search box's type-to-filter focus.
        // Done on HIDE rather than inside ShowAndActivate deliberately: SettingsLauncher.OpenCore
        // navigates to 设置 and THEN calls ShowAndActivate, so resetting on show would break the tray's
        // 选项 entry.
        _navigator.Navigate(ShellPage.App);
    }

    public void ShowAndActivate()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
        {
            RestorePlacement();
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            // Review fix (noted, not introduced this round): this assignment used to run outside
            // _restoring, so un-minimizing raised WindowStateProperty-changed with _restoring == false
            // and scheduled a redundant SavePlacement — harmless (it would only re-record the same
            // g.WindowMaximized this line just read to decide what to restore to), but inconsistent with
            // every other place this class writes to the window's own placement properties. _restoring
            // does not affect Resized/WindowResizeReason at all (that guard is about PositionChanged/
            // PropertyChanged only), so this costs nothing and closes the inconsistency.
            _restoring = true;
            try
            {
                _window.WindowState = _config.Config.General.WindowMaximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
            }
            finally
            {
                _restoring = false;
            }
        }

        _window.Activate();

        // Activate() alone is unreliable when the foreground window belongs to another process.
        var handle = WindowHandle();
        if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);

        _window.Focus();
        _events.RaiseLauncherShown(); // lets the app page clear + focus the search box
    }

    /// <summary>
    /// The hotkey / tray-click gesture: dismiss the launcher if it is what you are looking at, summon
    /// it otherwise.
    /// </summary>
    /// <remarks>
    /// "Visible" is NOT "focused". Under the flyout, 失焦自动隐藏 defaulted to true, so a visible window
    /// was always the foreground one and the two were interchangeable. Since 0.12 the window is an
    /// ordinary taskbar-resident app with hide-on-blur OFF by default, so it routinely sits visible
    /// behind whatever the user is working in — and treating that as "dismiss" made the global hotkey
    /// and the tray left-click HIDE the launcher the user was trying to summon. Ask Windows who is
    /// actually in front instead.
    /// </remarks>
    public void Toggle()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized)
        {
            ShowAndActivate();
            return;
        }

        if (IsForeground())
            Hide();
        else
            ShowAndActivate(); // visible but buried: raise it, do not dismiss it
    }

    private IntPtr WindowHandle() => _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    /// <summary>True when our window is the system's foreground window. A modal we own (a MessageBox,
    /// a folder picker) is a DIFFERENT HWND, so it reads as "not foreground" and Toggle raises rather
    /// than hides — which is the right answer while a dialog is up.</summary>
    private bool IsForeground()
    {
        var handle = WindowHandle();
        return handle != IntPtr.Zero && GetForegroundWindow() == handle;
    }

    /// <summary>
    /// Hold off hide-on-blur while something modal owns focus. REFERENCE-COUNTED, not a flag: the
    /// suppressions genuinely overlap — a folder picker raised from a settings card suppresses on top
    /// of whatever suppressed before it. Unbalanced releases are clamped at zero rather than going
    /// negative, which would permanently disable hide-on-blur for the session.
    /// </summary>
    public void SuppressHide(bool on)
    {
        if (on)
            _suppressDepth++;
        else if (_suppressDepth > 0)
            _suppressDepth--;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
}
