using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class WindowPlacementTests
{
    private static readonly PlacementRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Resolve_SavedRectInsideWorkArea_IsKept()
    {
        var r = WindowPlacement.Resolve(200, 100, 1000, 640, Work, 760, 520);

        Assert.Equal(new PlacementRect(200, 100, 1000, 640), r);
    }

    [Fact]
    public void Resolve_NegativePosition_CentresOnWorkArea()
    {
        var r = WindowPlacement.Resolve(-1, -1, 1000, 640, Work, 760, 520);

        Assert.Equal(460, r.Left);   // (1920 - 1000) / 2
        Assert.Equal(200, r.Top);    // (1040 - 640) / 2
    }

    [Fact]
    public void Resolve_RectOffTheRightEdge_IsPulledBackInside()
    {
        // Saved on a second monitor that is no longer connected.
        var r = WindowPlacement.Resolve(3000, 50, 1000, 640, Work, 760, 520);

        Assert.Equal(920, r.Left);   // 1920 - 1000
        Assert.Equal(50, r.Top);
    }

    [Fact]
    public void Resolve_RectLargerThanWorkArea_IsShrunkToFit()
    {
        var r = WindowPlacement.Resolve(0, 0, 4000, 3000, Work, 760, 520);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Resolve_SizeBelowMinimum_IsRaisedToMinimum()
    {
        var r = WindowPlacement.Resolve(10, 10, 100, 80, Work, 760, 520);

        Assert.Equal(760, r.Width);
        Assert.Equal(520, r.Height);
    }

    [Fact]
    public void Resolve_WorkAreaSmallerThanMinimum_PrefersFittingTheScreen()
    {
        var tiny = new PlacementRect(0, 0, 640, 400);

        var r = WindowPlacement.Resolve(-1, -1, 1000, 640, tiny, 760, 520);

        Assert.Equal(640, r.Width);
        Assert.Equal(400, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Resolve_WorkAreaWithOffset_PositionsRelativeToIt()
    {
        // A left-hand secondary monitor: the work area does not start at the origin.
        var offset = new PlacementRect(-1920, 0, 1920, 1040);

        var r = WindowPlacement.Resolve(-1, -1, 1000, 640, offset, 760, 520);

        Assert.Equal(-1460, r.Left);  // -1920 + (1920 - 1000) / 2
        Assert.Equal(200, r.Top);
    }
}
