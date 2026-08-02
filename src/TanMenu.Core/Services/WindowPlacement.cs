namespace TanMenu.Core.Services;

/// <summary>A window rectangle in DIPs. Deliberately not an Avalonia type: this lives in Core so the
/// arithmetic can be unit-tested without a UI thread or a display.</summary>
public readonly record struct PlacementRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>
/// The app's one placement rule: a window is horizontally CENTRED on its work area and anchored LOW,
/// a fixed gap above the work area's bottom edge (i.e. above the taskbar). Round 4, item 4 replaced
/// the previous "restore a remembered rectangle and clamp it into view" rule with this one, and with
/// it the two functions that implemented it (<c>Resolve</c> and <c>ChooseWorkArea</c>) — see
/// <see cref="ResolveBottomCentred"/>'s remarks for why nothing is remembered any more.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Resolve a window rectangle from a desired SIZE alone: size clamps first, then horizontal
    /// centring, then the bottom anchor — in that order, because both positions depend on the size.
    /// </summary>
    /// <param name="width">Desired width; clamped into [min, work area].</param>
    /// <param name="height">Desired height; clamped into [min, work area].</param>
    /// <param name="workArea">The target monitor's work area (excludes the taskbar). Its origin is
    /// NOT assumed to be (0,0): a monitor left of or above the primary one has negative coordinates,
    /// and both the centring and the anchor are computed relative to it.</param>
    /// <param name="minWidth">Floor for the resolved width, unless the work area is narrower.</param>
    /// <param name="minHeight">Floor for the resolved height, unless the work area is shorter.</param>
    /// <param name="bottomGap">DIPs to leave between the resolved rectangle's bottom edge and the work
    /// area's bottom edge.</param>
    /// <remarks>
    /// <para>
    /// Size is resolved first, because position depends on it. A desired size is clamped to
    /// [min, work-area]; when the work area itself is smaller than the minimum, FITTING THE SCREEN
    /// wins — a window larger than the display cannot be moved back into view, whereas one below the
    /// nominal minimum is merely cramped. That inversion is <see cref="Clamp"/>'s <c>max &lt; min</c>
    /// rule, kept byte-for-byte from the rule this replaced.
    /// </para>
    /// <para>
    /// The vertical clamp degenerates correctly rather than needing a special case: once the height
    /// has been clamped it can never exceed the work area, so <c>workArea.Bottom - height</c> is never
    /// above <c>workArea.Top</c>, and content that fills the work area exactly makes the two equal —
    /// the window top-aligns and the gap is simply not available. A gap larger than the spare height
    /// pins the top to <c>workArea.Top</c> instead of going negative.
    /// </para>
    /// <para>
    /// There is no remembered position to honour any more, and no "which monitor did this rectangle
    /// come from" question to answer: Round 4, item 4 retired <c>GeneralConfig.WindowLeft</c>/
    /// <c>WindowTop</c> (kept deserializable, read by nothing) because "always appear in one
    /// predictable place" and "come back where you dragged it" are mutually exclusive, and the caller
    /// cannot tell a cold start from a hotkey summon — the same <c>WindowHost.RestorePlacement</c>
    /// runs for both. The caller supplies the work area it wants; <c>WindowHost</c> uses the PRIMARY
    /// monitor's at restore time and the window's CURRENT monitor's on a re-fit.
    /// </para>
    /// </remarks>
    public static PlacementRect ResolveBottomCentred(
        double width,
        double height,
        PlacementRect workArea,
        double minWidth,
        double minHeight,
        double bottomGap)
    {
        var w = Clamp(width, Math.Min(minWidth, workArea.Width), workArea.Width);
        var h = Clamp(height, Math.Min(minHeight, workArea.Height), workArea.Height);

        var left = workArea.Left + (workArea.Width - w) / 2;
        var top = Clamp(workArea.Bottom - bottomGap - h, workArea.Top, workArea.Bottom - h);

        return new PlacementRect(left, top, w, h);
    }

    /// <summary>
    /// Combine a measured page-content size with the shell chrome around it (the navigation rail and
    /// the content pane's left border beside it, the title strip and the pane's top border above it)
    /// into a full window size, then place it exactly like <see cref="ResolveBottomCentred"/>. This is
    /// the arithmetic Round 2's fit-to-content feature needs on top of the placement rule — pulled out
    /// here, rather than left inline in <c>WindowHost</c>, so it is unit-testable without measuring a
    /// real Avalonia visual tree.
    /// </summary>
    /// <param name="contentWidth">The active page's natural content width, as measured by Avalonia (e.g.
    /// <c>AppPage.DesiredSize.Width</c>) — excludes the navigation rail.</param>
    /// <param name="contentHeight">The active page's natural content height — excludes the title strip.</param>
    /// <param name="chromeWidth">Everything beside the content that still counts toward the window's own
    /// Width: the navigation rail, which is a fixed 48 DIP, plus whatever left border the content pane
    /// carries. This function only adds it — what it is composed of is the caller's business
    /// (<c>MainWindow.RefitToContent</c>).</param>
    /// <param name="chromeHeight">Everything above the content that still counts toward the window's own
    /// Height: the title strip, plus whatever top border the content pane carries
    /// (<c>WindowHost.ChromeHeightDip</c>).</param>
    /// <remarks>
    /// A fit no longer takes a position at all. Before Round 4, item 4 it took the window's own current
    /// Left/Top and re-clamped them, so a re-fit preserved wherever the window happened to be; now
    /// position is a pure function of the resolved size, so every re-fit re-centres and re-anchors as
    /// well as resizing. That is the whole point: a fit that only clamped could leave a window that
    /// started centred-low 362 DIP off centre once the real content size landed.
    /// </remarks>
    public static PlacementRect ResolveContentFit(
        double contentWidth,
        double contentHeight,
        double chromeWidth,
        double chromeHeight,
        PlacementRect workArea,
        double minWidth,
        double minHeight,
        double bottomGap) =>
        ResolveBottomCentred(
            contentWidth + chromeWidth, contentHeight + chromeHeight,
            workArea, minWidth, minHeight, bottomGap);

    /// <summary>
    /// True when two resolved placements describe the same window rectangle to within
    /// <paramref name="tolerance"/> DIPs on every edge.
    /// </summary>
    /// <remarks>
    /// Exists so a re-fit that computes a geometry already asked for can skip writing it back.
    /// Round 2's review round wired several new re-fit triggers (小/中/大 — replaced by
    /// 每列个数 in Round 3, Task 4 — 字体/语言, the Maximized→Normal transition, and a rail
    /// expand/collapse toggle that Round 4, item 1 then deleted along with the rail's second width) on
    /// top of the two that existed, and more than one of them can fire
    /// for a single user action; without this test each one would re-assign Width/Height/Position and
    /// re-log an identical "Window fit to content" line. It also makes a notice-driven re-fit that
    /// changes nothing genuinely inert, rather than merely landing on the same numbers after having
    /// touched the platform window. Since Round 4, item 4 the stakes are the other way round too: a
    /// re-fit MOVES the window as well as resizing it, so this test is also what guarantees a placement
    /// that differs only in position is applied rather than mistaken for a no-op.
    /// <para>
    /// This function only compares two rectangles; WHICH second rectangle the caller supplies is the
    /// caller's decision, and it is deliberately not the window's own live geometry. Round 3's
    /// final fix wave changed <c>WindowHost.FitToContent</c> to pass the size the PREVIOUS fit itself
    /// requested, because a platform that clamps a request writes the clamped value back into
    /// <c>Width</c>/<c>Height</c> and so guarantees the live size never equals the computed one in that
    /// regime — making the skip unreachable exactly where it was needed. (The concrete clamp that
    /// motivated it — this window could not render below roughly 605 DIP outer — is gone as of Round 4,
    /// item 4, which removed <c>MainWindow.axaml</c>'s <c>MinHeight</c>; see
    /// <c>WindowHost.MinWindowHeightDip</c>. The rule stands on its own regardless: a request-based
    /// basis is what "the same placement" has to mean.) Round 4, item 4 then widened the basis from a
    /// size pair to the whole RECTANGLE, because position stopped being incidental the moment it
    /// became a function of the size. The old basis mixed the window's LIVE position with the previous
    /// request's size. That could not produce a FALSE skip — the old fit derived its position by
    /// clamping the live one, so the two positions matched exactly when the window was already where
    /// the fit wanted it. But kept alongside a rule that re-centres and re-anchors on every fit, it
    /// would leave a pure MOVE (a title-bar drag, an external <c>SetWindowPos</c>: neither raises
    /// <c>Resized</c>, so neither is otherwise observed) comparing unequal, and the next
    /// content-unchanged re-fit would snap the window back to the anchor. Against the whole
    /// rectangle that re-fit matches its own last request and skips, so a dragged window stays put
    /// until its content changes or it is re-summoned — see <c>WindowHost.FitToContent</c>, which
    /// records the same consequence at its skip test. Do not "restore" a live-geometry
    /// comparison here or in the caller on the assumption that the two are interchangeable; they are
    /// not.
    /// </para>
    /// <para>
    /// The default tolerance is half a DIP, not exact equality: Avalonia's Win32 backend truncates DIP
    /// to physical pixels with <c>(int)(value * RenderScaling)</c> before <c>SetWindowPlacement</c> and
    /// writes the converted-back value into <c>Width</c>/<c>Height</c>, so the size a window reports
    /// after a fit can differ from the one that was computed by a fraction of a DIP at any scaling
    /// other than 100%. Exact equality would therefore report "changed" on every re-fit at 125%/150%,
    /// which is precisely the case this is meant to catch.
    /// </para>
    /// </remarks>
    public static bool IsSamePlacement(PlacementRect a, PlacementRect b, double tolerance = 0.5) =>
        Math.Abs(a.Left - b.Left) <= tolerance &&
        Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Width - b.Width) <= tolerance &&
        Math.Abs(a.Height - b.Height) <= tolerance;

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);
}
