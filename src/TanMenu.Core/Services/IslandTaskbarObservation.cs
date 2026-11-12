namespace TanMenu.Core.Services;

/// <summary>
/// Turns a raw shell taskbar reading into the inputs <see cref="IslandPlacement"/> understands.
/// A failed read stays unknown. This type does not claim a Windows desktop was observed.
/// </summary>
public static class IslandTaskbarObservation
{
    public const uint AbsAutoHide = 0x1;

    public static (bool? AutoHide, double? ThicknessDip) Interpret(
        bool querySucceeded,
        uint state,
        int edge,
        int left,
        int top,
        int right,
        int bottom,
        double scale,
        bool positionKnown)
    {
        if (!querySucceeded)
            return (null, null);

        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;

        var autoHide = (state & AbsAutoHide) != 0;
        if (!positionKnown)
            return (autoHide, null);

        double thicknessPx = edge is 0 or 2
            ? right - left
            : edge is 1 or 3
                ? bottom - top
                : 0;
        if (thicknessPx <= 0)
            return (autoHide, null);

        return (autoHide, thicknessPx / scale);
    }
}
