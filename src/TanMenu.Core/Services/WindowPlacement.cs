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

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);
}
