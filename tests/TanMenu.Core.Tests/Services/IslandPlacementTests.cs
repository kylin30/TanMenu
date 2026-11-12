using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

/// <summary>
/// Placement is pure DIP arithmetic. Auto-hide and non-bottom taskbar cases follow the reviewed
/// conservative rules; they are not evidence that a Windows desktop was observed.
/// </summary>
public class IslandPlacementTests
{
    [Fact]
    public void StandardBottomTaskbar_CentersOnWorkArea_AndSitsOneGapAboveIt()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1032),
            AutoHide = false,
        });

        Assert.NotNull(placed);
        Assert.Equal(800, placed.Value.Left);
        Assert.Equal(948, placed.Value.Top);
        Assert.Equal(320, placed.Value.Width);
        Assert.Equal(72, placed.Value.Height);
        Assert.Equal(1020, placed.Value.Bottom);
    }

    [Fact]
    public void NonZeroOrigin_CentersOnTheWorkArea_NotTheScreenOrigin()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(2560, 0, 1920, 1080),
            WorkArea = new PlacementRect(2560, 0, 1920, 1032),
            AutoHide = false,
        });

        Assert.NotNull(placed);
        Assert.Equal(3360, placed.Value.Left);
        Assert.Equal(948, placed.Value.Top);
        Assert.Equal(320, placed.Value.Width);
        Assert.Equal(72, placed.Value.Height);
    }

    [Fact]
    public void NegativeWorkAreaOrigin_StaysRelativeToThatWorkArea()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(-1920, 0, 1920, 1080),
            WorkArea = new PlacementRect(-1920, 0, 1920, 1032),
            AutoHide = false,
        });

        Assert.NotNull(placed);
        Assert.Equal(-1120, placed.Value.Left);
        Assert.Equal(948, placed.Value.Top);
    }

    [Fact]
    public void WindowSize_IsAlwaysTheExpandedHost_IndependentOfTheCollapsedCapsule()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1032),
            AutoHide = false,
        });

        Assert.Equal(IslandMetrics.ExpandedWidth, placed!.Value.Width);
        Assert.Equal(IslandMetrics.ExpandedHeight, placed.Value.Height);
        Assert.NotEqual(IslandMetrics.CollapsedWidth, placed.Value.Width);
        Assert.NotEqual(IslandMetrics.CollapsedHeight, placed.Value.Height);
    }

    [Fact]
    public void AutoHide_NotYetObservedOnWindows_ReservesMeasuredThicknessAboveTheScreenBottom()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1080),
            AutoHide = true,
            TaskbarThicknessDip = 40,
        });

        Assert.NotNull(placed);
        Assert.Equal(800, placed.Value.Left);
        Assert.Equal(956, placed.Value.Top);
        Assert.Equal(1028, placed.Value.Bottom);
    }

    [Fact]
    public void AutoHide_WithoutAMeasuredThickness_ReservesTheFallbackPlusGap()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1080),
            AutoHide = true,
            TaskbarThicknessDip = null,
        });

        Assert.NotNull(placed);
        Assert.Equal(948, placed.Value.Top);
        Assert.Equal(1080 - 48 - 12, placed.Value.Bottom);
    }

    [Fact]
    public void UnknownAutoHide_WithNoBottomInset_StillReservesTheFallback()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1080),
            AutoHide = null,
        });

        Assert.NotNull(placed);
        Assert.Equal(1080 - 48 - 12 - 72, placed.Value.Top);
    }

    [Fact]
    public void UnknownAutoHide_UsesTheLargerOfInsetAndFallback()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 1920, 1030),
            AutoHide = null,
        });

        Assert.NotNull(placed);
        Assert.Equal(50, 1080 - 1030);
        Assert.Equal(1080 - 50 - 12 - 72, placed.Value.Top);
    }

    [Fact]
    public void LeftTaskbarShape_StaysOnTheWorkAreaBottom_AndDoesNotDockToTheLeftEdge()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(80, 0, 1840, 1080),
            AutoHide = false,
        });

        Assert.NotNull(placed);
        Assert.Equal(80 + (1840 - 320) / 2d, placed.Value.Left);
        Assert.Equal(1080 - 12 - 72, placed.Value.Top);
        Assert.True(placed.Value.Left > 80);
    }

    [Fact]
    public void TopTaskbarShape_StaysOnTheWorkAreaBottom_AndDoesNotDockToTheTop()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 48, 1920, 1032),
            AutoHide = false,
        });

        Assert.NotNull(placed);
        Assert.Equal(800, placed.Value.Left);
        Assert.Equal(1032 + 48 - 12 - 72, placed.Value.Top);
        Assert.True(placed.Value.Top > 48);
    }

    [Fact]
    public void MissingWorkArea_FallsBackToTheUnknownReserveFromTheScreenBottom()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 1920, 1080),
            WorkArea = new PlacementRect(0, 0, 0, 0),
            AutoHide = false,
            TaskbarThicknessDip = 10,
        });

        Assert.NotNull(placed);
        Assert.Equal(800, placed.Value.Left);
        Assert.Equal(1080 - 48 - 12 - 72, placed.Value.Top);
    }

    [Fact]
    public void MissingScreenBounds_ReturnsNull_SoTheHostDoesNotShow()
    {
        var placed = IslandPlacement.ResolveWindow(new IslandScreenMetrics
        {
            Bounds = new PlacementRect(0, 0, 0, 0),
            WorkArea = new PlacementRect(0, 0, 1920, 1032),
            AutoHide = false,
        });

        Assert.Null(placed);
    }

    [Fact]
    public void FromDevicePixels_ConvertsWithThatScreenScale()
    {
        var metrics = IslandPlacement.FromDevicePixels(
            boundsX: 0, boundsY: 0, boundsWidth: 1920, boundsHeight: 1080,
            workX: 0, workY: 0, workWidth: 1920, workHeight: 1040,
            scale: 1.5,
            autoHide: false,
            taskbarThicknessDip: null);

        Assert.Equal(1280, metrics.Bounds.Width);
        Assert.Equal(720, metrics.Bounds.Height);
        Assert.Equal(1280, metrics.WorkArea.Width, 5);
        Assert.Equal(1040 / 1.5, metrics.WorkArea.Height, 5);
        Assert.False(metrics.AutoHide);
    }

    [Fact]
    public void ToDevicePosition_RoundsLikeTheExistingLauncherPlacement()
    {
        var (x, y) = IslandPlacement.ToDevicePosition(new PlacementRect(800, 948, 320, 72), 1.5);
        Assert.Equal(1200, x);
        Assert.Equal(1422, y);

        var (low, high) = IslandPlacement.ToDevicePosition(new PlacementRect(10.4, 10.6, 1, 1), 1);
        Assert.Equal(10, low);
        Assert.Equal(11, high);
    }
}
