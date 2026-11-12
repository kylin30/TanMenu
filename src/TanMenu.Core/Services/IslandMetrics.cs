namespace TanMenu.Core.Services;

/// <summary>Approved first-preview sizes. The HWND stays at the expanded size; the capsule animates inside it.</summary>
public static class IslandMetrics
{
    public const double CollapsedWidth = 120;
    public const double CollapsedHeight = 32;
    public const double ExpandedWidth = 320;
    public const double ExpandedHeight = 72;
    public const double CollapsedRadius = 16;
    public const double ExpandedRadius = 36;
    public const double GapDip = 12;
    public const double AutoHideReserveFallbackDip = 48;
    public const double ExpandDurationMs = 300;
    public const double CollapseDurationMs = 250;
    public const double LeaveDelayMs = 80;
    public const double EnterDelayMs = 0;
}
