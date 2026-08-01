namespace TanMenu.Core.Services;

/// <summary>A window rectangle in DIPs. Deliberately not an Avalonia type: this lives in Core so the
/// arithmetic can be unit-tested without a UI thread or a display.</summary>
public readonly record struct PlacementRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Turns a saved (possibly stale or hostile) window placement into one that is guaranteed to
/// be visible on the supplied work area.</summary>
public static class WindowPlacement
{
    /// <param name="savedLeft">Remembered X, or null when there is none — centre instead.</param>
    /// <param name="savedTop">Remembered Y, or null when there is none — centre instead.</param>
    /// <param name="savedWidth">Remembered width; clamped into [min, work area].</param>
    /// <param name="savedHeight">Remembered height; clamped into [min, work area].</param>
    /// <param name="workArea">The target monitor's work area (excludes the taskbar). Its origin is
    /// NOT assumed to be (0,0): a monitor left of or above the primary one has negative coordinates.</param>
    /// <param name="minWidth">Floor for the resolved width, unless the work area is narrower.</param>
    /// <param name="minHeight">Floor for the resolved height, unless the work area is shorter.</param>
    /// <remarks>
    /// <para>
    /// Size is resolved first, because position depends on it. A saved size is clamped to
    /// [min, work-area]; when the work area itself is smaller than the minimum, FITTING THE SCREEN
    /// wins — a window larger than the display cannot be moved back into view, whereas one below the
    /// nominal minimum is merely cramped.
    /// </para>
    /// <para>
    /// "No remembered position" is null, NOT a negative number. A monitor to the left of the primary
    /// occupies negative coordinates, so a real remembered X can be negative; a negative sentinel
    /// would silently discard it and re-centre the window on every restart.
    /// </para>
    /// </remarks>
    public static PlacementRect Resolve(
        double? savedLeft,
        double? savedTop,
        double savedWidth,
        double savedHeight,
        PlacementRect workArea,
        double minWidth,
        double minHeight)
    {
        var width = Clamp(savedWidth, Math.Min(minWidth, workArea.Width), workArea.Width);
        var height = Clamp(savedHeight, Math.Min(minHeight, workArea.Height), workArea.Height);

        var left = savedLeft is { } l
            ? Clamp(l, workArea.Left, workArea.Right - width)
            : workArea.Left + (workArea.Width - width) / 2;
        var top = savedTop is { } t
            ? Clamp(t, workArea.Top, workArea.Bottom - height)
            : workArea.Top + (workArea.Height - height) / 2;

        return new PlacementRect(left, top, width, height);
    }

    /// <summary>
    /// Pick WHICH monitor a saved placement belongs to, before <see cref="Resolve"/> clamps against it.
    /// </summary>
    /// <param name="saved">The remembered window rectangle in DIPs, or null when there is no
    /// remembered POSITION (a fresh config, or one carrying only a size) — that always means the
    /// primary, because a centred window is centred on the primary.</param>
    /// <param name="workAreas">Every connected monitor's work area in DIPs, in the platform's own
    /// enumeration order.</param>
    /// <param name="primaryIndex">Index of the primary monitor within <paramref name="workAreas"/>.
    /// Out of range is treated as 0.</param>
    /// <returns>An index into <paramref name="workAreas"/>, or -1 when the list is empty.</returns>
    /// <remarks>
    /// <para>
    /// Exists because the obvious source for "which monitor" — asking the window itself — is WRONG at
    /// restore time. The window has not been placed yet; it is wherever Win32 cascaded it, which is
    /// normally the primary. Clamping a rectangle saved at x=9500 against the PRIMARY's work area
    /// yanks it back onto the primary, and a window can then never be restored onto a secondary
    /// monitor at all. The saved rectangle is the only thing that knows where the window belongs.
    /// </para>
    /// <para>
    /// The test is the saved rectangle's CENTRE, not its origin: a window the user dragged so its
    /// left edge hangs slightly off a monitor still belongs to that monitor, and an origin test would
    /// hand it to the neighbour (or to the primary) and move it on the next launch.
    /// </para>
    /// <para>
    /// A saved rectangle whose centre is on no connected monitor — unplugged, or the desktop
    /// rearranged — falls back to the primary, where <see cref="Resolve"/> then clamps it into view.
    /// </para>
    /// <para>
    /// Mixed-DPI caveat: the caller converts each monitor's work area with THAT monitor's own scaling,
    /// which is exactly how the position was recorded, so a rectangle saved on monitor N matches
    /// monitor N's converted work area. It does not make the converted rectangles a globally
    /// consistent coordinate space — with different scalings they can overlap, in which case the
    /// first match wins. That is a heuristic, not a proof; it is still strictly better than always
    /// asking the primary.
    /// </para>
    /// </remarks>
    public static int ChooseWorkArea(
        PlacementRect? saved,
        IReadOnlyList<PlacementRect> workAreas,
        int primaryIndex = 0)
    {
        if (workAreas.Count == 0)
            return -1;

        var primary = primaryIndex >= 0 && primaryIndex < workAreas.Count ? primaryIndex : 0;
        if (saved is not { } rect)
            return primary;

        var centreX = rect.Left + rect.Width / 2;
        var centreY = rect.Top + rect.Height / 2;

        for (var i = 0; i < workAreas.Count; i++)
        {
            var w = workAreas[i];
            // Half-open on the far edges so two abutting monitors never both claim a centre that
            // lands exactly on the seam.
            if (centreX >= w.Left && centreX < w.Right && centreY >= w.Top && centreY < w.Bottom)
                return i;
        }

        return primary;
    }

    /// <summary>
    /// Combine a measured page-content size with the shell chrome around it (the navigation rail's
    /// current width, the title strip's fixed height) into a full window size, then clamp/position it
    /// exactly like <see cref="Resolve"/>. This is the one new piece of arithmetic Round 2's
    /// fit-to-content feature needs on top of <see cref="Resolve"/>/<see cref="ChooseWorkArea"/> — pulled
    /// out here, rather than left inline in <c>WindowHost</c>, so it is unit-testable without measuring a
    /// real Avalonia visual tree.
    /// </summary>
    /// <param name="contentWidth">The active page's natural content width, as measured by Avalonia (e.g.
    /// <c>AppPage.DesiredSize.Width</c>) — excludes the navigation rail.</param>
    /// <param name="contentHeight">The active page's natural content height — excludes the title strip.</param>
    /// <param name="chromeWidth">Everything beside the content that still counts toward the window's own
    /// Width: the navigation rail's CURRENT width (48 collapsed / 200 expanded).</param>
    /// <param name="chromeHeight">Everything above the content that still counts toward the window's own
    /// Height: the title strip.</param>
    /// <param name="currentLeft">Where to keep the window horizontally — the window's OWN current
    /// position, not a saved config value: fit-to-content runs after the window is already placed and
    /// shown, so there is always a real position to preserve. Null only centres, exactly like
    /// <see cref="Resolve"/>'s <c>savedLeft</c>.</param>
    /// <param name="currentTop">As <paramref name="currentLeft"/>, vertically.</param>
    public static PlacementRect ResolveContentFit(
        double contentWidth,
        double contentHeight,
        double chromeWidth,
        double chromeHeight,
        double? currentLeft,
        double? currentTop,
        PlacementRect workArea,
        double minWidth,
        double minHeight) =>
        Resolve(
            currentLeft, currentTop,
            contentWidth + chromeWidth, contentHeight + chromeHeight,
            workArea, minWidth, minHeight);

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);
}
