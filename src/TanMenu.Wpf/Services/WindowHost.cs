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
/// process's first one. Round 4, item 4 brought the bottom-centre PLACEMENT back too, this time to an
/// ordinary application window: there is one placement rule
/// (<see cref="WindowPlacement.ResolveBottomCentred"/>) and both callers use it, so the window is
/// horizontally centred with its bottom edge <see cref="BottomGapDip"/> above the taskbar on every show
/// AND after every re-fit. <see cref="RestorePlacement"/> applies that rule on the primary monitor at
/// the best size it knows — the user's own last manually-resized size for this process, else the size
/// the last fit requested, else the PLACEHOLDER (never <c>GeneralConfig.WindowWidth</c>/
/// <c>WindowHeight</c>, and no longer <c>WindowLeft</c>/<c>WindowTop</c> either — see those properties'
/// own remarks for why); <see cref="FitToContent"/> then measures the actual page and re-applies the
/// same rule at the measured size on whichever monitor the window is on — UNLESS
/// the user has already taken control of the size by resizing it by hand, in which case it steps aside
/// for the rest of the process. See <see cref="_userResized"/>'s own remarks for the full rule.
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    /// <summary>
    /// The global Aero Window theme's <c>ExtendClientAreaTitleBarHeightHint</c> — the caption band's fixed
    /// height, which sits ABOVE the page host and is therefore not part of what a page's own content
    /// measures. Duplicated here as a constant rather than read off the live window because nothing on
    /// <c>Window</c> exposes it as a public property.
    /// </summary>
    /// <remarks>
    /// Round 7, Task 1 moved this from 48 (the Windows 11 caption height) to 30, which is the Aero
    /// caption band MEASURED on Microsoft's own published Windows 7 screenshot — see
    /// <c>MainWindow.axaml</c>'s <c>Window.WindowDecorationsTheme</c> block for the capture, the URL and
    /// the coordinates. The number lives in the decoration theme, the Window padding and here; the
    /// source test <c>CaptionHeight_IsTheSameNumberInTheThemeAndFitTerm</c> is what
    /// catches a drift between them.
    /// <para>
    /// A CHANGE HERE IS NOT VISIBLE IN THE FIT LOG'S HEIGHT, and that is not a bug. <c>Ph +
    /// ChromeHeightDip</c> is below <see cref="MinWindowHeightDip"/> at every content size this app
    /// produces, so any <c>ChromeHeightDip</c> up to 74 yields the identical 568 DIP window. The term
    /// that does move is the log line's own <c>chrome {w}x{h}</c>: it read 49x49 before Task 1, 49x31
    /// after it, 161x31 after Round 7, Task 8 widened the navigation pane, and <b>0x55</b> after
    /// Round 8, Task 3 deleted that pane and put a menu band above the page instead — the height half
    /// of that pair is this constant's only observable.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The classic menu band's height, in DIP — <c>MainWindow.axaml</c>'s <c>MenuBand</c>, which Round 8,
    /// Task 3 put where the navigation pane used to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>25, and the number lands in THREE unlinked places</b>: the vendored
    /// <c>Themes/Aero/ControlStyles/Menu.axaml</c>'s <c>&lt;x:Double x:Key="MenuBarHeight"&gt;</c> (which
    /// is what the bar actually PAINTS, through the <c>{x:Type Menu}</c> theme's own <c>Height</c>
    /// setter), <c>MainWindow.axaml</c>'s <c>MenuBand</c> <c>Height</c> (which is what the Auto row
    /// RESERVES), and this constant (which is what the fit HANDS BACK).
    /// <c>ShellConstantTests.MenuBandHeight_IsTheSameNumberInAllThreePlaces</c> ties all three, and it
    /// needs all three: two can agree while the third drifts, and the only symptom of that drift is a
    /// window fitted to a chrome height its own band does not have — silently.
    /// </para>
    /// <para>
    /// WHERE THE 25 COMES FROM: the item strip is 23, which is PUBLISHED
    /// (https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout gives one height for the
    /// whole family of text-line controls, and item 5 puts one of those — the search field — in this
    /// bar), and the vendored template docks two 1 DIP bottom-border rules inside the same height. It
    /// DEPARTS from the MEASURED <c>SM_CYMENU</c> of 20 by 5 DIP; that vendored file's own header
    /// carries the departure, the measurement and the two alternatives that were rejected.
    /// </para>
    /// </remarks>
    private const double TitleBarHeightDip = 48;

    /// <summary>The bottom edge of the managed Aero frame. The 8 DIP left/right pair is supplied by
    /// <c>MainWindow.ChromeWidthDip</c>; this is the matching vertical edge below the page.</summary>
    /// <summary>
    /// Everything ABOVE the page that still counts toward the window's own Height, which
    /// <see cref="FitToContent"/> adds back on top of the measured page size: the caption band plus the
    /// menu band plus the bottom edge of the Aero frame.
    /// </summary>
    /// <remarks>
    /// <b>ROUND 8, TASK 3 REPLACED THE "+1" WITH A WHOLE BAND.</b> This was
    /// <c>TitleStripHeightDip + 1</c>, where the 1 was the 应用 pane's own TOP border — the vertical
    /// counterpart of the <c>chromeWidth</c> the caller composed as
    /// <c>MainWindow.RailWidthDip + PaneBorderDip</c>. <c>Border.tm-page-pane</c>
    /// (Themes/Shell/Launcher.axaml) no longer sets <c>BorderThickness</c> at all: its left edge
    /// separated the page from a navigation pane that is deleted, and its top edge is redundant against
    /// the two 1 DIP bottom rules the vendored Menu template draws directly above it. So both DIP left
    /// the arithmetic in the same commit, and what arrived in their place is
    /// <see cref="TitleBarHeightDip"/>. The caller's horizontal term is the compact navigation pane,
    /// now 16 DIP for the matching left and right frame edges.
    /// </remarks>
    private const double ChromeHeightDip = TitleBarHeightDip;

    /// <summary>The app's own minimum window WIDTH, in DIP. Mirrors <c>MainWindow.axaml</c>'s
    /// <c>MinWidth="727"</c>, which stays declared there and is still enforced by the platform against
    /// an interactive resize; this constant is what the placement arithmetic clamps with, and the two
    /// have to be kept in step —
    /// <c>ShellConstantTests.MinWindowWidth_IsTheSameNumberInBothPlaces</c> is what catches a drift.
    /// Unlike the height (below), <c>MinWidth</c> is safe to declare: Avalonia's
    /// <c>WM_GETMINMAXINFO</c> handler adds <c>BorderThickness.Left + Right</c> to it, which for this
    /// window's style is exactly the horizontal non-client inset <c>AdjustWindowRectEx</c> reports, so the
    /// resulting client floor equals <c>MinWidth</c> and a requested width is never silently raised. That
    /// declarability argument is unchanged by Round 7 — <c>ExtendClientAreaToDecorationsHint</c> is still
    /// on, and it is the vertical inset it distorts, not the horizontal one.</summary>
    /// <remarks>
    /// <para>
    /// <b>THE NUMBER THIS FLOOR PROTECTS IS 711 DIP OF CONTENT PANE.</b> At the floor it gets
    /// <c>727 − 16 = 711</c>: 16 DIP is the global Aero frame's two side edges. Earlier shell layouts
    /// protected the same content width with navigation-pane terms; the visible content floor has not
    /// changed.
    /// </para>
    /// <para>
    /// <b>THE 711 HAS ONE NEW FLOOR TO CLEAR THAT THE 872 DID NOT.</b> The menu band lays five items,
    /// then the separator's whole 23 DIP footprint, then the 320 DIP search field, then 11 DIP of
    /// trailing margin, end to end — and unlike the pane, that run is not a constant: it follows the UI
    /// language and the configured font. It was MEASURED rather than estimated in Task 3; the report
    /// carries the reading and the headroom.
    /// </para>
    /// <para>
    /// A persisted window width below the floor comes back CLAMPED UP to it by
    /// <see cref="RestorePlacement"/>'s existing clamp. That is the clamp doing its job, not a
    /// regression, and it is what happened the last two times this minimum moved.
    /// </para>
    /// </remarks>
    private const double MinWindowWidthDip = 640;

    /// <summary>
    /// The app's own minimum window HEIGHT, in DIP — the floor <see cref="RestorePlacement"/> and
    /// <see cref="FitToContent"/> clamp with. It is a constant here rather than
    /// <c>MainWindow.axaml</c>'s <c>MinHeight</c> because declaring it on the Window made the window
    /// taller than anything ever asked for.
    /// </summary>
    /// <remarks>
    /// Decompiled Avalonia 12.1.0, <c>Win32.WindowImpl</c>'s <c>WM_GETMINMAXINFO</c> case:
    /// <c>if (_minSize.Height > 0) ptMinTrackSize.Y = (int)(_minSize.Height * RenderScaling +
    /// BorderThickness.Top + BorderThickness.Bottom)</c>, where <c>BorderThickness</c> is derived from
    /// <c>AdjustWindowRectEx</c> on the live style. On this machine's 150% monitor that is T45 + B11 = 56
    /// physical px, so a declared <c>MinHeight="568"</c> produced a minimum TRACK size of
    /// 568 × 1.5 + 56 = 908 physical — the outer height the window actually rendered at. But the real
    /// vertical non-client inset is only 11 px, because <c>ExtendClientAreaToDecorationsHint</c> pulls
    /// the 45 px caption INTO the client area, so the client floor came out at 897 px = 598 DIP against a
    /// request of 568: the window was 30 DIP taller than the fit asked for, and a bottom anchor computed
    /// from the requested height would have put its bottom edge 56 px behind the taskbar. Raising
    /// <c>MinHeight</c> cannot escape that — the surplus is a function OF <c>MinHeight</c> — so Round 4,
    /// item 4 deleted the attribute instead: with <c>_minSize.Height == 0</c> the guard above fails,
    /// Windows falls back to its own default minimum track size, and requested height == real height.
    /// The floor itself is not lost, it just moved here, where it is applied to the REQUEST rather than
    /// to the window. Two accepted side effects: the user can now drag the window shorter than 568 DIP
    /// (which latches <see cref="_userResized"/>, and since no size is persisted the next process start
    /// comes back at the fitted size), and the window renders 30 DIP shorter than before at the same
    /// content.
    /// </remarks>
    private const double MinWindowHeightDip = 520;

    /// <summary>DIPs left between the placement rectangle's bottom edge and the work area's bottom edge
    /// (i.e. the top of the taskbar). The app's own precedent: the 0.11 flyout placed itself at
    /// <c>work.Bottom - height - GeneralConfig.PositionOffset × scale</c> with a default offset of 8.
    /// <para>
    /// The VISIBLE gap is smaller than 8 DIP and that is deliberate. A <c>PlacementRect</c>'s Top is the
    /// RAW frame origin while its Height is the CLIENT height, and the DWM-visible rectangle is inset
    /// another ~9 px from the raw one on this window's style, so at 150% an 8 DIP nominal gap renders as
    /// roughly 6.7 DIP of visible clearance. Matching what Windows' own tray flyout does — visible edge
    /// flush against the work area — would mean deliberately overhanging the work area by ~6 DIP, and
    /// any error in the non-client estimate then becomes a real taskbar overlap. 8 DIP never overlaps.
    /// </para></summary>
    private const double BottomGapDip = 8;

    /// <summary>Physical-pixel headroom added to a content fit so DIP-to-pixel truncation cannot make
    /// a ScrollViewer one pixel shorter than the extent it just reported.</summary>
    private const double FitRoundingSlackPixels = 2;

    /// <summary>Placeholder Width/Height <see cref="RestorePlacement"/> applies on the very FIRST show
    /// of the process, before the launcher's startup build — an async disk scan — has produced anything
    /// for <see cref="FitToContent"/> to measure. Matches MainWindow.axaml's own declared Width/Height,
    /// so there is nothing visually jarring to reconcile once the real, content-driven size lands moments
    /// later. Fixed constants, NOT read from <c>GeneralConfig.WindowWidth</c>/<c>WindowHeight</c>, which
    /// Round 2 retired. Superseded on every LATER summon by <see cref="_lastFitPlacement"/>'s size (the
    /// size the previous fit requested) or, once the user has
    /// manually resized this process, by <see cref="_lastNormalWidthDip"/>/<see cref="_lastNormalHeightDip"/>
    /// — see those fields' own remarks. Only the SIZE is superseded: the placeholder never carries a
    /// position, because since Round 4, item 4 nothing does.
    /// <para>
    /// The width moved 1000 → 1112 in Round 7, Task 8, by the navigation pane's own 112 DIP growth, and
    /// <b>back to 1000 in Round 8, Task 3</b>, which deleted that pane; <c>MainWindow.axaml</c>'s
    /// declared <c>Width</c> moved with it both times. Keeping the two equal is the whole point of the
    /// constant: the cold-start first frame is drawn at the XAML's width and this is what the first
    /// placement re-applies, so a mismatch shows up as a visible jump before the real, content-driven
    /// size lands. The height is unchanged through both — neither task moved anything vertical that
    /// reaches a PLACEHOLDER, because the menu band's 29 DIP is chrome the fit adds and the placeholder
    /// is a whole-window size.
    /// </para></summary>
    private const double PlaceholderWidthDip = 1100;
    private const double PlaceholderHeightDip = 760;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
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
    /// fix it is <see cref="_lastFitPlacement"/>'s size once a fit has run, falling
    /// back to <see cref="PlaceholderWidthDip"/>/<see cref="PlaceholderHeightDip"/> only on the process's
    /// genuinely first show (see <see cref="RestorePlacement"/>'s own remarks).</summary>
    private double _lastNormalWidthDip = PlaceholderWidthDip;
    private double _lastNormalHeightDip = PlaceholderHeightDip;

    /// <summary>The whole rectangle the most recent successful <see cref="FitToContent"/> REQUESTED, or
    /// null until one has run. <see cref="RestorePlacement"/> applies its SIZE as the PRE-SHOW size on
    /// every later summon, which is the whole point: the correction a fit makes only reaches the platform
    /// on the next layout pass, i.e. after <see cref="Window.Show"/> has already put the window on screen,
    /// so without this every summon painted the 1000x688 placeholder for a frame and then snapped to the
    /// real size — reintroducing, for SIZE, exactly the paint-then-jump this class already goes out of its
    /// way to avoid for POSITION (see <see cref="RestorePlacement"/>'s own remarks).</summary>
    /// <remarks>
    /// <para>
    /// A whole <see cref="PlacementRect"/>, not a width/height pair, as of Round 4, item 4. The skip test
    /// in <see cref="FitToContent"/> used to build its comparison rectangle out of the window's LIVE
    /// position and the PREVIOUS request's size — only half request-based, which was tolerable while a
    /// fit preserved position and is not now that position is a function of the size. The basis is
    /// therefore the previously requested rectangle in full; the one behaviour that changes with it (a
    /// window MOVED by something other than this class is no longer snapped back by a content-unchanged
    /// re-fit) is spelled out at the skip test itself.
    /// </para>
    /// <para>
    /// Deliberately SEPARATE from <see cref="_lastNormalWidthDip"/>/<see cref="_lastNormalHeightDip"/>
    /// rather than a reuse of them. Those two belong to the <see cref="_userResized"/> contract and mean
    /// "the size the USER chose"; writing a fitted size into them would make a later
    /// <see cref="RestorePlacement"/> unable to tell an automatic size from a deliberate one, and would
    /// resurrect the very baseline-drift class of bug <see cref="_userResized"/>'s remarks describe.
    /// This one means only "the last geometry we computed ourselves" and is read ONLY when
    /// <see cref="_userResized"/> is false.
    /// </para>
    /// <para>
    /// Noted so nobody chases it: the SAME width out of this field can land one PHYSICAL pixel apart
    /// depending on which of the two paths applies it. Measured on the shipped build at 150 %, with a
    /// fitted width of 1692.3333 DIP — <see cref="FitToContent"/> produced a client width of 2539 px,
    /// while a later <see cref="RestorePlacement"/> reusing this field's identical value produced
    /// 2538 px. 1692.3333 x 1.5 = 2538.5, i.e. the request lands on a half pixel and the two paths
    /// resolve it in opposite directions — unsurprising given they reach the platform differently (a
    /// pre-show <c>Resize</c> versus the next layout pass; see <see cref="_userResized"/>'s remarks on
    /// <c>Win32.WindowImpl.Resize</c>'s truncation). It cannot affect the skip test, which compares
    /// REQUESTS and never the live window (<see cref="WindowPlacement.IsSamePlacement"/> over this
    /// field): both paths request the identical <c>double</c>, so the divergence exists only after the
    /// request has left this class. Two thirds of a DIP on one edge is also not visible. Nor is it a
    /// case for widening that 0.5 DIP tolerance — the tolerance never sees this. Do not "fix" it by
    /// rounding the request either:
    /// the request is what the content measured to, and forcing it onto a physical-pixel grid would
    /// make the fit's own arithmetic depend on the scale factor.
    /// </para>
    /// </remarks>
    private PlacementRect? _lastFitPlacement;

    /// <summary>True when something OTHER than this class resized the window, so
    /// <see cref="_lastFitPlacement"/> still describes the last geometry we
    /// requested but no longer describes the window on screen. While it is set,
    /// <see cref="FitToContent"/> refuses to short-circuit even when the geometry it computes matches
    /// that rectangle — otherwise the very next content-unchanged re-fit trigger (a docked notice opening,
    /// a 每列个数 change) would decide "nothing to do" and leave the foreign size in place forever.</summary>
    /// <remarks>
    /// <para>
    /// Round 3 residual review. Comparing against the previous fit's own REQUEST rather than the live
    /// window (the fix this class needed, for the reasons <see cref="FitToContent"/> documents) traded
    /// one bug for a smaller one: the live-size comparison self-healed after any foreign resize,
    /// because the live size no longer matched anything we had computed. A request-based basis does
    /// not, so the foreign resize has to be OBSERVED instead of inferred. This flag is that
    /// observation, kept deliberately separate from nulling the rectangle itself:
    /// <see cref="RestorePlacement"/> reads its size as the PRE-SHOW size on the next summon, and
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

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;

        // Keeps the disk write off the WindowState transition that scheduled it, and coalesces a
        // maximize/restore flurry into one write. It used to coalesce the PositionChanged storm a
        // single drag produces; Round 4, item 4 retired the remembered position, so the only writer
        // left is WindowState (see Attach).
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
            //
            // THE SUPPRESSION TEST THAT USED TO BE SPELLED OUT HERE (_suppressDepth == 0) IS GONE
            // BECAUSE Hide() MAKES IT ITSELF NOW — see Hide's own remarks and the Round 8 review
            // defect they record. This was the only caller that made it, and it was not the caller
            // that needed it.
            if (_config.Config.General.AutoClose)
                Hide();
        };

        // 窗口置顶 has to reach a window that is already on screen. Restore applies it too, but that
        // only runs on a cold start or an un-hidden window, so without this the checkbox would appear
        // to do nothing until the next hide/show cycle.
        _events.SettingsChanged += ApplyTopmost;
        ApplyTopmost();

        // WindowState is the ONLY geometry this class still persists (GeneralConfig.WindowMaximized).
        // Round 4, item 4 retired the remembered position, and Round 2 had already retired the
        // remembered size, so a move or a resize no longer changes anything persistable and neither
        // PositionChanged nor Width/Height-changed is subscribed any more — a maximize/restore is the
        // whole trigger set. Persisting it as it happens rather than only on exit matters because the
        // process can be ended by an update handoff or a hard kill. _restoring guards against writing
        // back the value we are in the middle of applying.
        window.PropertyChanged += (_, e) =>
        {
            if (_restoring)
                return;
            if (e.Property == Window.WindowStateProperty)
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
    /// every LATER re-fit trigger for the rest of the process — a docked notice opening, a 每列个数
    /// change, another
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
    /// For <see cref="FitToContent"/> only — it runs AFTER the window has a real position. Do NOT use
    /// this to restore: at restore time (<see cref="RestorePlacement"/>) the window has not been placed
    /// yet, so this reports wherever Win32 cascaded it rather than a considered answer.
    /// <see cref="PrimaryScreen"/> is the restore-side answer, and since Round 4, item 4 it is
    /// unconditional — the restore anchor is always the primary monitor's bottom centre. A window
    /// dragged onto another monitor mid-session therefore re-anchors to THAT monitor on its next re-fit
    /// (this method) and returns to the primary on the next summon; that is deliberate.
    /// </remarks>
    private Screen? CurrentScreen()
    {
        var screens = _window?.Screens;
        return (_window is not null ? screens?.ScreenFromWindow(_window) : null)
               ?? screens?.Primary
               ?? screens?.All.FirstOrDefault();
    }

    private static double SafeScale(Screen screen) => screen.Scaling <= 0 ? 1.0 : screen.Scaling;

    /// <summary>A screen's work area in DIPs, converted with THAT screen's own scaling. Both placement
    /// paths convert the resolved DIP rectangle back to physical pixels with the SAME screen's scaling,
    /// so the two conversions cancel.</summary>
    private static PlacementRect WorkAreaOf(Screen screen)
    {
        var scale = SafeScale(screen);
        var w = screen.WorkingArea;
        return new PlacementRect(w.X / scale, w.Y / scale, w.Width / scale, w.Height / scale);
    }

    /// <summary>
    /// The PRIMARY monitor's work area (DIPs) and its scaling — the restore-side screen answer. Falls
    /// back to the first enumerated screen, and then to a 1920×1040 guess at scale 1 when the platform
    /// reports no screens at all.
    /// </summary>
    /// <remarks>
    /// Unconditional since Round 4, item 4. There is no saved rectangle left to derive a monitor from,
    /// and asking the window is wrong here anyway — it has not been placed yet when this runs, so its
    /// position is whatever Win32 cascaded it to. The primary is also what the 0.11 flyout used
    /// (<c>TryGetPrimaryDisplay</c>) and the monitor whose taskbar the user's request names. The
    /// scaling returned is that same screen's, because the caller converts the resolved DIP rectangle
    /// back to physical pixels with it.
    /// </remarks>
    private (PlacementRect Work, double Scale) PrimaryScreen()
    {
        var screens = _window?.Screens;
        var screen = screens?.Primary ?? screens?.All.FirstOrDefault();
        return screen is null ? (UnknownWorkArea, 1.0) : (WorkAreaOf(screen), SafeScale(screen));
    }

    /// <summary>
    /// Return the themed window frame outside Avalonia's client size. Width/Height on Window size the
    /// client, while Position addresses the outer frame; the bottom-centred placement therefore has to
    /// include this overhead or a standard title bar pushes the visible bottom behind the taskbar.
    /// Before the first platform frame exists FrameSize is null and the pre-show placement uses zero;
    /// MainWindow re-applies placement at Loaded priority, once the real frame metrics are available.
    /// </summary>
    private (double Width, double Height) CurrentFrameOverhead()
    {
        if (_window?.FrameSize is not { } frame)
            return (0, 0);

        return (
            Math.Max(0, frame.Width - _window.ClientSize.Width),
            Math.Max(0, frame.Height - _window.ClientSize.Height));
    }

    /// <summary>
    /// Defer to the next dispatcher cycle instead of calling <see cref="SavePlacement"/> inline from
    /// the property-changed handler above.
    /// </summary>
    /// <remarks>
    /// A maximize transition raises WidthProperty/HeightProperty-changed — carrying the new,
    /// screen-filling values — BEFORE WindowStateProperty flips to Maximized, all synchronously
    /// within the same WM_SIZE handling. That ordering is why this posts rather than calling inline: it
    /// defers the read until the whole synchronous transition has finished, so by the time SavePlacement
    /// runs, WindowState already reflects its final value. The concrete bug it was written for is gone
    /// with the fields it corrupted — a synchronous call from the Width/Height handler read a stale
    /// WindowState of Normal and persisted the maximized, near-zero-origin position as the restore
    /// geometry (confirmed by maximizing the window and hiding it while still maximized: WindowLeft/Top
    /// came back overwritten), and Round 4, item 4 retired WindowLeft/WindowTop entirely, as Round 2 had
    /// already retired WindowWidth/WindowHeight. What is left to protect is
    /// <c>GeneralConfig.WindowMaximized</c> itself, whose value IS the property whose ordering is at
    /// stake, so the deferral still earns its place.
    /// </remarks>
    private void SchedulePlacementSave() => Dispatcher.UIThread.Post(SavePlacement);

    /// <summary>Place the window horizontally centred and anchored low on the PRIMARY monitor's work
    /// area, at either the placeholder SIZE or the size the last fit asked for or the user's own last
    /// manually-resized size. Never throws: a failure here must not prevent the window from being
    /// shown.</summary>
    /// <remarks>
    /// Called BEFORE <see cref="Window.Show"/> on a cold start (<c>App.ShowLauncherWindow</c>) and
    /// before every un-hide (<see cref="ShowAndActivate"/>), never afterwards: applying geometry to a
    /// window that is already on screen paints it once at the OS cascade position and then jumps it,
    /// which is exactly what deleting the flyout's <c>Opacity="0"</c> assumed could not happen.
    /// <para>
    /// POSITION is not remembered any more (Round 4, item 4). This method and
    /// <see cref="FitToContent"/> now share one rule — <see cref="WindowPlacement.ResolveBottomCentred"/>
    /// — so the window appears in one predictable place on every cold start and every summon, and the
    /// fit that lands ~330 ms later corrects the SIZE without moving the anchored bottom edge. The rule
    /// could not have lived in this method alone: a fit fed the window's own Left/Top back in and only
    /// re-clamped, so a centred placeholder left the fitted 1724-DIP-wide window 362 DIP
    /// off centre with no clamp firing. <c>GeneralConfig.WindowLeft</c>/<c>WindowTop</c> are retired in
    /// place — still deserialized, read by nothing.
    /// </para>
    /// <para>
    /// The SIZE this applies is the best one known BEFORE the window is on screen, in descending order
    /// of authority: the user's own last manual resize this process (<see cref="_userResized"/>) —
    /// restoring anything else would only be overwritten by their choice again, and applying it directly
    /// is what makes a mid-session resize actually survive a hide/summon cycle (review finding: it did
    /// not, before that fix); else the size the last <see cref="FitToContent"/> requested
    /// (<see cref="_lastFitPlacement"/>); else <see cref="PlaceholderWidthDip"/>/<see cref="PlaceholderHeightDip"/>,
    /// which is only ever reached on the process's FIRST show, when the launcher's content genuinely is
    /// not known yet (its startup build is an async disk scan that only begins once the window is shown).
    /// The middle rung is the review fix for finding 6: <see cref="FitToContent"/>'s correction reaches
    /// the platform on the NEXT layout pass, i.e. after Show() has already painted, so restoring the
    /// placeholder here made every summon flash the placeholder size before snapping to the real one.
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

            var width = _userResized ? _lastNormalWidthDip : _lastFitPlacement?.Width ?? PlaceholderWidthDip;
            var height = _userResized ? _lastNormalHeightDip : _lastFitPlacement?.Height ?? PlaceholderHeightDip;

            var (work, scale) = PrimaryScreen();
            var (frameWidth, frameHeight) = CurrentFrameOverhead();

            var placement = WindowPlacement.ResolveBottomCentred(
                width, height, work, MinWindowWidthDip, MinWindowHeightDip, BottomGapDip,
                frameWidth, frameHeight);

            // NOTE (Round 3 residual review): this deliberately does NOT update _lastFitPlacement,
            // even though it changes the window's size. That is safe, and the
            // reasoning has to hold across DIFFERENT work areas — the only situation in which the
            // placement resolved here can differ from the basis at all — so here it is in full.
            //
            // The hazard being ruled out is precisely one thing: this method leaving the window at a
            // size S that differs from the basis B, and a later FitToContent then computing exactly B,
            // matching the basis and SKIPPING, so the window is stranded at S. Three exhaustive cases:
            //
            //  1. _userResized is true. S comes from _lastNormalWidthDip/_lastNormalHeightDip and can
            //     be anything, but FitToContent's own first guard returns immediately for the rest of
            //     the process, so no fit can skip and nothing can be stranded.
            //  2. _userResized is false and the basis is null (the process's genuinely first show). S is
            //     the placeholder; the basis is null, and FitToContent treats null as "different", so
            //     the first fit is never skipped.
            //  3. _userResized is false and the basis is B. Then S = ResolveBottomCentred(B.Size,
            //     thisWorkArea), and S can only differ from B where the clamp moved it: it caps
            //     width/height at workArea.Width/Height and floors them at Math.Min(min, workArea.*),
            //     so S != B implies either B > workArea.* (clamped DOWN, S = workArea.*) or
            //     B < Math.Min(min, workArea.*) (floored UP, S = that floor). A subsequent FitToContent
            //     runs ResolveContentFit against the SAME work area, i.e. the same clamp: its result is
            //     likewise <= workArea.* and >= that same floor. So in the clamped-down case the fit
            //     cannot produce B (that would require a result above the cap it just applied), and in
            //     the floored-up case it cannot produce B either (that would require a result below the
            //     floor). Either way the fit computes something != B, so it cannot match the basis and
            //     cannot skip: the window is corrected on the very next fit rather than stranded.
            //     (Strictly, IsSamePlacement compares to within 0.5 DIP, so the fit could still "match"
            //     if the clamp moved the size by LESS than that — in which case S is within half a DIP
            //     of B and there is nothing left to strand.)
            //
            // Round 4, item 4 added POSITION to that basis, and the same three cases cover it without a
            // fourth: this method and FitToContent compute position with the SAME function from the
            // resolved size, so whenever the size agrees the position agrees too — on the primary
            // monitor, which is the only work area this method ever uses and the one a summoned window
            // is on. When the size differs the skip cannot fire at all, because the size differs. (A
            // window the user dragged to a SECOND monitor is re-anchored to that monitor by the next
            // fit, per CurrentScreen's own remarks; it is back on the primary from the next summon on,
            // because that is what this method applies before Show.)
            //
            // Updating the basis here instead was considered and rejected: it would write a work-area-
            // clamped size into the field RestorePlacement itself reads as the next summon's PRE-SHOW
            // size, so summoning back onto a larger monitor would paint the smaller clamped size first
            // and jump — reintroducing exactly the placeholder-flash defect it exists to fix.
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
    /// of the screen the window is CURRENTLY on, and RE-PLACE it by the same rule
    /// <see cref="RestorePlacement"/> uses: horizontally centred, bottom edge <see cref="BottomGapDip"/>
    /// above the work area's. Called on
    /// EVERY summon, not only a cold start (see the class remarks), and again on every other event that
    /// changes what the active page measures: switching between 应用 and 设置, a docked notice opening
    /// or closing, a 每列个数 / 字体 / 语言 change, a navigation-pane layout change, and the
    /// Maximized→Normal transition. Page switches force a fresh fit because a page change replaces the
    /// user's sizing context; ordinary content changes still respect a manual resize.
    /// It is a cheap, idempotent re-measure, not a one-time setup step, and
    /// a re-fit that computes the geometry the PREVIOUS fit already requested
    /// (<see cref="_lastFitPlacement"/>, compared with
    /// <see cref="WindowPlacement.IsSamePlacement"/>) writes nothing at all, so none of those triggers
    /// can cascade. Note the basis: it is the last RECTANGLE this class ASKED FOR, not the geometry the
    /// window currently has — the two legitimately differ whenever the platform clamps a request, which
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
    /// still clamps correctly: <see cref="WindowPlacement.ResolveBottomCentred"/> caps the RESULT to the
    /// work area regardless of how tall the probe reported — no special-case wrapping logic needed here
    /// either way.</param>
    /// <param name="chromeWidth">Everything BESIDE the page that still counts toward the window's own
    /// Width. <b>It is zero as of Round 8, Task 3</b>, and the parameter stays because the parameter is
    /// the contract: this shell's chrome runs horizontally now — a menu band above the page — so nothing
    /// is laid out to the page's left or right, and all 23 <c>WindowPlacementTests</c> parameterise this
    /// term precisely so a future shell can put something back there. It was the navigation pane's width
    /// plus the content pane's left hairline: 49 on the 48 DIP icon rail, 161 after Round 7, Task 8
    /// widened the pane to 160, and both of those constants are deleted with the pane. The caller still
    /// composes it from a literal constant that mirrors the XAML rather than measuring live elements —
    /// <c>MainWindow.ChromeWidthDip</c>, whose own remarks say why it is named rather than written as a
    /// bare 0 here.</param>
    /// <remarks>
    /// <para>
    /// Callable only after the window has been placed AND shown: unlike <see cref="RestorePlacement"/>,
    /// which asks <see cref="PrimaryScreen"/>, this asks <see cref="Screens"/> which monitor the window is
    /// ACTUALLY on right now (<see cref="CurrentScreen"/>), which only answers correctly once the window
    /// is real — exactly the restriction <see cref="CurrentScreen"/>'s own remarks document.
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
    /// POSITION: a re-fit ALWAYS re-places the window, because position is a pure function of the
    /// resolved size — centred horizontally, bottom edge <see cref="BottomGapDip"/> above the work
    /// area's. It does not read the window's current top-left at all. Round 2's review finding 7 asked
    /// for a deliberate decision about moving on growth and got "move by exactly the overflow, never
    /// otherwise"; Round 4, item 4 supersedes that with a rule that has to move: an anchor that only
    /// applied at restore would be undone by the very first fit (the cold-start placeholder is 1000x688
    /// and the fitted window is neither), so all FOUR surviving re-fit triggers — the same four this
    /// method's own summary lists — move the window as well
    /// as resizing it. A shrink now moves the window DOWN by exactly the height delta, keeping the
    /// bottom edge where it was; growth is symmetric about the centre and cannot overflow, because the
    /// size is clamped to the work area before the position is derived from it.
    /// </para>
    /// <para>
    /// Position reaches the platform BEFORE size, and that is pre-existing: <c>Window.Position</c>'s
    /// setter calls <c>PlatformImpl?.Move</c> immediately, while <c>Width</c>/<c>Height</c> reach the
    /// platform on the next layout pass. So for at most one frame a Top computed from the new height is
    /// applied to a window that still has the old one. The cold-start path is exempt — <c>ShowCore</c>
    /// pushes the size before <c>PlatformImpl.Show</c> — and the assignment order is not restructured
    /// here because the same one-frame skew already applied to the horizontal re-clamp this replaces.
    /// </para>
    /// </remarks>
    /// <param name="forcePageFit">True only for an explicit page transition. It clears a previous
    /// manual-size override and bypasses the request-basis shortcut so the newly visible page always
    /// receives its own content size.</param>
    public void FitToContent(Layoutable pageContent, double chromeWidth, bool forcePageFit = false)
    {
        if (_window is null || _window.WindowState != WindowState.Normal ||
            (_userResized && !forcePageFit))
            return;

        if (forcePageFit)
            _userResized = false;

        _restoring = true;
        try
        {
            var screen = CurrentScreen();
            var work = screen is null ? UnknownWorkArea : WorkAreaOf(screen);
            var scale = screen is null ? 1.0 : SafeScale(screen);
            var (frameWidth, frameHeight) = CurrentFrameOverhead();

            // Unbounded on BOTH axes — see this method's own <param> remarks on pageContent for why an
            // unbounded height (not a fixed reference, not the work area) is what makes every category,
            // not only the tallest, report its true single-column extent.
            pageContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var naturalDesired = pageContent.DesiredSize;
            var contentHeight = naturalDesired.Height;

            // When the natural width is wider than the monitor, the final ScrollViewer gains a
            // horizontal scrollbar. Re-measuring at the actual available page width makes that bar's
            // height part of the fitted height instead of discovering it only after the window opens.
            var availableContentWidth = Math.Max(0, work.Width - frameWidth - chromeWidth);
            if (naturalDesired.Width > availableContentWidth + 0.5)
            {
                pageContent.Measure(new Size(availableContentWidth, double.PositiveInfinity));
                contentHeight = Math.Max(contentHeight, pageContent.DesiredSize.Height);
            }

            // Avalonia converts the requested DIP size to physical pixels using truncation. Exact-fit
            // ScrollViewers can therefore lose a fractional pixel and unnecessarily enable vertical
            // scrolling. Two physical pixels cover that conversion without a DPI-dependent DIP guess.
            contentHeight += FitRoundingSlackPixels / scale;

            // No current position is read: since Round 4, item 4 the placement is a pure function of the
            // resolved size and the work area (centred, bottom-anchored), so a re-fit re-places as well
            // as resizes. See this method's own POSITION remark.
            var placement = WindowPlacement.ResolveContentFit(
                naturalDesired.Width, contentHeight,
                chromeWidth, ChromeHeightDip,
                work, MinWindowWidthDip, MinWindowHeightDip, BottomGapDip,
                frameWidth, frameHeight);

            // Compare against the whole RECTANGLE the previous fit requested, NOT the window's live
            // geometry. Round 3's reason for a request-based basis: a platform that clamps a request
            // writes the clamped value back into Width/Height on the next layout pass, so the live size
            // and the requested one could never match in that regime and this check could never
            // short-circuit — every cold start logged and debounce-wrote TWICE instead of once. (The
            // clamp that provoked it was the ~605 DIP outer floor MainWindow.axaml's MinHeight="568"
            // produced; Round 4, item 4 removed the attribute, so a request is no longer clamped at all
            // — see MinWindowHeightDip. The basis rule outlives its cause: what this class controls and
            // can compare meaningfully is what it asked for.) Round 4 widened the basis from a size pair
            // to the full rectangle: the old comparison built its rectangle out of the window's LIVE
            // Position and the previous request's size, which is only half request-based, and position
            // stopped being incidental the moment it became a function of the size.
            //
            // That has one behavioural consequence, stated rather than glossed. A pure MOVE — a title-bar
            // drag, an external SetWindowPos — reaches PositionChanged, never Resized (decompiled
            // Win32.WindowImpl: WM_MOVE raises PositionChanged, only WM_SIZE raises Resized), so it sets
            // neither _userResized nor _fitBasisStale. Had the mixed basis been kept alongside the new
            // anchoring rule, a content-unchanged re-fit would see "live position != computed position"
            // and snap the window back to the anchor; against the whole rectangle it sees the one it
            // last requested, matches, and skips, so the window stays where it was dragged until the
            // content actually changes. (The counterfactual is deliberate: the PRE-Round-4 code did not
            // snap a dragged window back either, but only because its resolver derived the position by
            // clamping the live one, so the live position appeared on BOTH sides of the comparison and
            // cancelled out. That is gone — position is a function of the size now — and the basis had
            // to be widened to keep the same outcome.) It is re-anchored on
            // the next summon regardless, because RestorePlacement always applies the anchor before Show.
            //
            // null (nothing has fit yet this process) always counts as "different", so the very first fit
            // of a process is never skipped. _fitBasisStale additionally forces "different" when
            // something outside this class has RESIZED the window since — see that field's own remarks.
            if (!forcePageFit && !_fitBasisStale && _lastFitPlacement is { } previous &&
                WindowPlacement.IsSamePlacement(placement, previous))
            {
                // Several triggers can fire for one user action (see IsSamePlacement's own remarks).
                // Debug, not Information: a fit that changes nothing is not an event worth a line in the
                // shipped log, but it is worth being able to see when chasing one that should have.
                Serilog.Log.Debug(
                    "Window fit to content: already {Width}x{Height} DIP at {Left},{Top}; nothing to apply",
                    placement.Width, placement.Height, placement.Left, placement.Top);
                return;
            }

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));

            // Written only AFTER the three assignments above have all returned, never before them.
            // Round 3 residual review: this used to be assigned right after the placement was
            // computed, above the skip test. That is a basis for "the geometry this window was last
            // asked for", so recording one the window was never actually asked for is a lie that never
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
            // DIP tolerance of this placement anyway — RestorePlacement, the field's other reader, gets
            // the same pre-show size either way.
            _lastFitPlacement = placement;
            _fitBasisStale = false; // basis and window agree again by construction

            Serilog.Log.Information(
                "Window fit to content: {Left},{Top} {Width}x{Height} DIP (page " +
                "{ContentWidth}x{ContentHeight} + chrome {ChromeWidth}x{ChromeHeight}) on work area " +
                "{WorkLeft},{WorkTop} {WorkWidth}x{WorkHeight} at scale {Scale}",
                placement.Left, placement.Top, placement.Width, placement.Height,
                naturalDesired.Width, contentHeight, chromeWidth,
                ChromeHeightDip, work.Left, work.Top, work.Width, work.Height, scale);
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
    /// Updates the in-memory config immediately but DEBOUNCES the disk write, which also keeps the
    /// write off the maximize/restore transition that triggered it. <see cref="Hide"/> cancels the
    /// debounce and starts the write immediately (still asynchronously) so a hide does not sit on an
    /// unwritten state; <see cref="FlushPlacementBlocking"/> is the variant 退出 uses, which also WAITS
    /// for it. Exactly ONE config field is still written here — <c>GeneralConfig.WindowMaximized</c>.
    /// Round 2 retired the remembered size and Round 4, item 4 retired the remembered position, so
    /// there is nothing else about the window's geometry left to persist.
    /// </remarks>
    public void SavePlacement()
    {
        if (_window is null || _restoring)
            return;

        try
        {
            var state = _window.WindowState;

            // Minimized carries NO placement information: reading it as "not maximized" would erase a
            // maximized window's remembered state the moment the user minimizes it.
            if (state == WindowState.Minimized)
                return;

            var g = _config.Config.General;
            g.WindowMaximized = state == WindowState.Maximized;

            // Nothing else is written. Width/Height stopped being persisted in Round 2 and Left/Top in
            // Round 4, item 4 (GeneralConfig.WindowWidth/WindowHeight/WindowLeft/WindowTop are all
            // retired in place — see their own remarks): nothing reads them back on disk, so writing
            // them would just be stale data. Position specifically is now recomputed from the primary
            // work area on every show, so there is no "normal-state position" to protect from a
            // maximized window's near-zero origin any more either.

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

    /// <summary>
    /// Take the launcher off screen, flushing its placement on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A SUPPRESSION IN FORCE MAKES THIS A NO-OP, AND THAT IS THE ROUND 8 REVIEW'S HIGH DEFECT.</b>
    /// <see cref="SuppressHide"/> means "something modal we own has the user's attention"; the launcher
    /// is that modal's OWNER, and hiding an owner does not close what it owns — MEASURED on the live
    /// app: with the 选项 dialog up, one <see cref="Hide"/> left BOTH HWNDs alive
    /// (<c>IsWindow</c> true) and BOTH invisible (<c>IsWindowVisible</c> false). The dialog was then
    /// unreachable for the rest of the session: it never reached <c>OnClosed</c>, so
    /// <c>OptionsWindow.Current</c> went on pointing at it — accurately — and every later 设置 took
    /// <c>SettingsLauncher.OpenCoreAsync</c>'s single-instance branch and <c>Activate()</c>d a window
    /// nobody could see. Nothing threw and nothing was logged.
    /// </para>
    /// <para>
    /// The route measured was a key press: Avalonia routes a key to the FOCUSED ELEMENT, and when the
    /// dialog is opened from the menu band by keyboard the focus can still be on the band, so an
    /// Escape aimed at the dialog's HWND ran <c>MainWindow.OnKeyDown</c> → <c>HandleSearchEscape</c> →
    /// this method instead. But the guard is not written for that route: ALL THREE callers —
    /// <c>MainWindow.OnClosing</c>'s hide-to-tray, <c>LauncherViewModel.HandleSearchEscape</c> and
    /// <c>LauncherViewModel.LaunchItem</c> — are launcher-window gestures, and not one of them is
    /// something the user can mean while a dialog we own is up. Testing it HERE is what makes that
    /// true by construction instead of once per caller; the <c>Deactivated</c> handler in
    /// <see cref="Attach"/> used to be the only caller that tested, and it was the only one that could
    /// not cause the defect.
    /// </para>
    /// </remarks>
    public void Hide()
    {
        if (_suppressDepth > 0)
            return;

        SavePlacement();
        FlushPlacement(); // hiding is often the last thing before exit; do not leave it to the timer
        _window?.Hide();

        // ROUND 8, TASK 2 DELETED THE LINE THAT USED TO FOLLOW: _navigator.Navigate(ShellPage.App).
        // It reset the shell to 应用 after the window went off screen, because ShellNavigator.Current
        // was process-lifetime state that survived hide/show and a session that had visited 设置 would
        // otherwise re-summon onto 设置 — with AppPage detached, which also killed the search box's
        // type-to-filter focus. There is one page now: 设置 is a modal dialog (Dialogs/OptionsWindow),
        // the whole page model is gone, and "the window always opens on 应用" is true by construction
        // rather than by a reset here. Nothing replaces it, and this class no longer takes a navigator
        // at all.
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
            // every other place this class writes to the window's own placement properties. The flag
            // gates both listeners this class still has — PropertyChanged (Attach) and Resized
            // (OnWindowResized's own first line) — so bracketing this write also keeps any Resized the
            // un-minimize raises out of _userResized/_fitBasisStale, exactly as RestorePlacement and
            // FitToContent already do for their own writes. (An earlier revision of this comment
            // claimed _restoring "does not affect Resized/WindowResizeReason at all (that guard is
            // about PositionChanged/PropertyChanged only)". Both halves were wrong: OnWindowResized
            // begins with an _restoring guard of its own, and Round 4, item 4 deleted the
            // PositionChanged subscription this class used to have.)
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
