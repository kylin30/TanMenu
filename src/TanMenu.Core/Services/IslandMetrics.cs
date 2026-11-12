namespace TanMenu.Core.Services;

/// <summary>
/// V2 sizes. The HWND stays 320×72; the concave trapezoid animates inside it.
/// Bottom corners are one reverse fillet each, not a second convex foot.
/// </summary>
public static class IslandMetrics
{
    public const double CollapsedBottomWidth = 120;
    public const double CollapsedHeight = 32;
    public const double CollapsedTopRadius = 11;
    public const double CollapsedFilletRadius = 12;

    public const double ExpandedBottomWidth = 320;
    public const double ExpandedHeight = 72;
    public const double ExpandedTopRadius = 20;
    public const double ExpandedFilletRadius = 22;

    /// <summary>Fillet sweep from the horizontal baseline up to the sloped side. 66° leaves that side 24° off vertical.</summary>
    public const double FilletSweepDegrees = 66;

    public const int ArcSteps = 10;
    public const double GapDip = 0;
    public const double ExpandDurationMs = 300;
    public const double CollapseDurationMs = 250;
    public const double LeaveDelayMs = 80;
    public const double EnterDelayMs = 0;

    public const double CollapsedWidth = CollapsedBottomWidth;
    public const double ExpandedWidth = ExpandedBottomWidth;
}
