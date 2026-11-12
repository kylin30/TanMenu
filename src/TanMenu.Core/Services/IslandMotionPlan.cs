namespace TanMenu.Core.Services;

public readonly record struct IslandMotion(double Target, double DurationMs);

/// <summary>One progress value. Reversal keeps the current progress and shortens the remaining time.</summary>
public static class IslandMotionPlan
{
    public static IslandMotion Toward(double current, bool expand, bool reducedMotion)
    {
        var target = expand ? 1d : 0d;
        var start = double.IsFinite(current) ? Math.Clamp(current, 0, 1) : 0;
        if (reducedMotion)
            return new IslandMotion(target, 0);

        var full = expand ? IslandMetrics.ExpandDurationMs : IslandMetrics.CollapseDurationMs;
        return new IslandMotion(target, full * Math.Abs(target - start));
    }
}
