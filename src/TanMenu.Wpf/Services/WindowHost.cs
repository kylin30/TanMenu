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
/// process's first one: <see cref="RestorePlacement"/> restores POSITION always, and either the
/// PLACEHOLDER size or the user's own last manually-resized size for this process (never
/// <c>GeneralConfig.WindowWidth</c>/<c>WindowHeight</c> — see those properties' own remarks for why);
/// <see cref="FitToContent"/> then measures the actual page and resizes the window to it, clamped to the
/// work area exactly like a restored size always was — UNLESS the user has already taken control of the
/// size by resizing it by hand, in which case it steps aside for the rest of the process. See
/// <see cref="_userResized"/>'s own remarks for the full rule.
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    /// <summary>MainWindow.axaml's own <c>ExtendClientAreaTitleBarHeightHint</c> — the title strip's
    /// fixed height, which sits ABOVE the page host and is therefore not part of what a page's own
    /// content measures. Duplicated here as a constant rather than read off the live window because
    /// nothing on <c>Window</c> exposes it as a public property; <see cref="FitToContent"/> adds it back
    /// on top of the measured page size to get the window's own full content-driven height.</summary>
    private const double TitleStripHeightDip = 48;

    /// <summary>Placeholder Width/Height <see cref="RestorePlacement"/> applies before the launcher's
    /// startup build — an async disk scan — has produced anything for <see cref="FitToContent"/> to
    /// measure, on the very first show of the process. Matches MainWindow.axaml's own declared
    /// Width/Height, so there is nothing visually jarring to reconcile once the real, content-driven size
    /// lands moments later. Fixed constants, NOT read from <c>GeneralConfig.WindowWidth</c>/
    /// <c>WindowHeight</c>, which Round 2 retired. Superseded by <see cref="_lastNormalWidthDip"/>/
    /// <see cref="_lastNormalHeightDip"/> on every later summon once the user has manually resized once
    /// this process — see those fields' own remarks.</summary>
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
    /// True once a genuine, external Width/Height change has been observed — the user dragging an edge,
    /// detected in <see cref="SavePlacement"/> as a live size that no longer matches
    /// <see cref="_lastNormalWidthDip"/>/<see cref="_lastNormalHeightDip"/> (both of <see cref="RestorePlacement"/>
    /// and <see cref="FitToContent"/> keep those two fields in sync with whatever they themselves just
    /// applied, so only an outside actor can make them disagree). Sticky for the rest of the PROCESS,
    /// never cleared: this is the fix for the review finding that a mid-session resize used to be
    /// silently discarded on the next summon (<see cref="RestorePlacement"/> reset to the placeholder,
    /// and a one-shot guard in <c>MainWindow</c> then skipped re-fitting away from it forever after,
    /// which was itself based on the false premise that RestorePlacement preserved a resize it was
    /// actually already destroying). Once true, <see cref="RestorePlacement"/> restores the REMEMBERED
    /// size instead of the placeholder, and <see cref="FitToContent"/> stops re-fitting entirely — the
    /// user's own explicit choice wins, the same rule <c>GeneralConfig.WindowMaximized</c> already gets.
    /// </summary>
    /// <remarks>
    /// Detected inside <see cref="SavePlacement"/> specifically because that method already runs
    /// deferred by one dispatcher tick (see <see cref="SchedulePlacementSave"/>'s own remarks): a
    /// maximize/restore transition raises Width/HeightProperty-changed SYNCHRONOUSLY, with WindowState
    /// still (momentarily) reading its PRE-transition value, before WindowState itself flips — reading
    /// the live size any earlier would occasionally mistake a maximize or an un-maximize for a manual
    /// resize. By the time <see cref="SavePlacement"/> runs, the transition has settled and WindowState
    /// reads its final value, so only a change that is STILL Normal-sized one tick later ever counts.
    /// </remarks>
    private bool _userResized;

    /// <summary>The window's own last-applied NORMAL-state Width/Height, in DIPs. <see cref="RestorePlacement"/>
    /// and <see cref="FitToContent"/> both write these every time either of them assigns a size, so this
    /// pair always reflects "the size WE most recently, intentionally applied" — never a stale value —
    /// which is exactly what lets <see cref="SavePlacement"/> tell a genuine external resize apart from
    /// our own placement writes (see <see cref="_userResized"/>). Seeded to the placeholder size, which
    /// is precisely what a cold start's first <see cref="RestorePlacement"/> call applies before either
    /// method has run even once.</summary>
    private double _lastNormalWidthDip = PlaceholderWidthDip;
    private double _lastNormalHeightDip = PlaceholderHeightDip;

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
    /// The SIZE this applies is <see cref="PlaceholderWidthDip"/>/<see cref="PlaceholderHeightDip"/> —
    /// the launcher's content is not known yet at this point (its startup build is an async disk scan
    /// that only begins once the window is shown), so <see cref="FitToContent"/> supersedes it moments
    /// later — UNLESS the user has already manually resized the window once this process
    /// (<see cref="_userResized"/>), in which case restoring the placeholder here would only get
    /// overwritten by FitToContent a moment later anyway; applying their remembered size directly avoids
    /// a visible placeholder-then-jump on every later summon and is what makes a mid-session resize
    /// actually survive being hidden and summoned again — review finding: it did not, before this fix.
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

            var width = _userResized ? _lastNormalWidthDip : PlaceholderWidthDip;
            var height = _userResized ? _lastNormalHeightDip : PlaceholderHeightDip;

            var (work, scale) = ResolveTargetScreen(SavedRect(width, height));

            var placement = WindowPlacement.Resolve(
                g.WindowLeft, g.WindowTop, width, height,
                work, _window.MinWidth, _window.MinHeight);

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));
            _window.WindowState = g.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            ApplyTopmost();

            // Keep the "what did we last intentionally apply" baseline current — see
            // _lastNormalWidthDip's own remarks for why SavePlacement depends on this.
            _lastNormalWidthDip = placement.Width;
            _lastNormalHeightDip = placement.Height;

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
    /// content-driven size would otherwise push the window off screen). Called on EVERY summon, not only
    /// a cold start (see the class remarks), and again whenever a docked notice's visibility might have
    /// changed the page's natural height (<c>MainWindow.OnNoticeVisibilityChanged</c>) — it is a cheap,
    /// idempotent re-measure, not a one-time setup step.
    /// </summary>
    /// <param name="pageContent">The active page whose natural size drives the window's — the launcher,
    /// on every path that reaches this method, since the window always opens on 应用. Measured with an
    /// UNBOUNDED width AND height (<see cref="Size"/> of <see cref="double.PositiveInfinity"/> on both
    /// axes) — this is what makes the measurement genuinely content-driven on both axes now, per the
    /// user's Round 2 review decision that height should adapt too: the rule is "the category with the
    /// most items fits in a single column without wrapping, clamped to the work area". A vertical
    /// WrapPanel only reports that true, un-wrapped extent when given an unbounded height budget — handed
    /// any FINITE bound instead (this method's own first cut used a comfortable 640 DIP reference, and an
    /// earlier attempt still visible in this method's history used the whole work area), it just wraps to
    /// avoid exceeding whatever bound it was given and reports back a number that reflects the BOUND, not
    /// the content — which is the "probe the WrapPanel will happily fill" failure mode the review flagged.
    /// With every category unwrapped, the reported WIDTH is also then exactly what the FINAL layout needs
    /// once the window is actually sized to the tallest category's need (every shorter category already
    /// fits in one column at that same, more generous height too) — so one unbounded probe answers both
    /// axes consistently. A genuinely oversized category still clamps correctly: <see cref="WindowPlacement.Resolve"/>
    /// caps the RESULT to the work area regardless of how tall the probe reported, and the next real
    /// Avalonia layout pass at that smaller, real window height wraps the WrapPanel normally, the same way
    /// it always has — no special-case wrapping logic needed here.</param>
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
    /// restores maximized regardless.
    /// </para>
    /// <para>
    /// ALSO skips once <see cref="_userResized"/> is true — the user has manually resized the window this
    /// process, and that choice wins over any further auto-fit, including one triggered by a notice
    /// appearing. See that field's own remarks for the full rule and the review finding it fixes.
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

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));

            // Same baseline SavePlacement compares against — see _lastNormalWidthDip's own remarks.
            _lastNormalWidthDip = placement.Width;
            _lastNormalHeightDip = placement.Height;

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

                // Detect a genuine, external resize: RestorePlacement/FitToContent both keep
                // _lastNormalWidthDip/HeightDip in sync with whatever THEY last applied (under
                // _restoring, which already made this method return above without reaching here), so a
                // live size that disagrees with that baseline can only mean the user dragged an edge.
                // This is what makes RestorePlacement restore the resized size on the next summon instead
                // of resetting to the placeholder — see _userResized's own remarks for the full rule and
                // the review finding it fixes.
                if (_window.Width != _lastNormalWidthDip || _window.Height != _lastNormalHeightDip)
                {
                    _userResized = true;
                    _lastNormalWidthDip = _window.Width;
                    _lastNormalHeightDip = _window.Height;
                }
            }

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
            _window.WindowState = _config.Config.General.WindowMaximized
                ? WindowState.Maximized
                : WindowState.Normal;
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
