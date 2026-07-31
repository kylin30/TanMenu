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
    /// <param name="savedLeft">Saved X, or a negative value meaning "centre me".</param>
    /// <param name="savedTop">Saved Y, or a negative value meaning "centre me".</param>
    /// <param name="workArea">The target monitor's work area (excludes the taskbar).</param>
    /// <remarks>
    /// Size is resolved first, because position depends on it. A saved size is clamped to
    /// [min, work-area]; when the work area itself is smaller than the minimum, FITTING THE SCREEN
    /// wins — a window larger than the display cannot be moved back into view, whereas one below the
    /// nominal minimum is merely cramped.
    /// </remarks>
    public static PlacementRect Resolve(
        double savedLeft,
        double savedTop,
        double savedWidth,
        double savedHeight,
        PlacementRect workArea,
        double minWidth,
        double minHeight)
    {
        var width = Clamp(savedWidth, Math.Min(minWidth, workArea.Width), workArea.Width);
        var height = Clamp(savedHeight, Math.Min(minHeight, workArea.Height), workArea.Height);

        var left = savedLeft < 0
            ? workArea.Left + (workArea.Width - width) / 2
            : Clamp(savedLeft, workArea.Left, workArea.Right - width);
        var top = savedTop < 0
            ? workArea.Top + (workArea.Height - height) / 2
            : Clamp(savedTop, workArea.Top, workArea.Bottom - height);

        return new PlacementRect(left, top, width, height);
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);
}
