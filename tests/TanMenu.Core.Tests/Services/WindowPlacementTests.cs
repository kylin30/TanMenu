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
    public void Resolve_UnsetPosition_CentresOnWorkArea()
    {
        var r = WindowPlacement.Resolve(null, null, 1000, 640, Work, 760, 520);

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

        var r = WindowPlacement.Resolve(null, null, 1000, 640, tiny, 760, 520);

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

        var r = WindowPlacement.Resolve(null, null, 1000, 640, offset, 760, 520);

        Assert.Equal(-1460, r.Left);  // -1920 + (1920 - 1000) / 2
        Assert.Equal(200, r.Top);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ChooseWorkArea — WHICH monitor a saved placement belongs to, decided before Resolve clamps
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // The real three-display desktop this was found on: a 7680x2160 primary, a portrait panel far to
    // its right at x=9216, and a third below. Work areas are DIPs, taskbar already excluded.
    private static readonly PlacementRect[] ThreeMonitors =
    [
        new(9216, -689, 2592, 4536),  // secondary, to the right of the primary
        new(0, 0, 7680, 2088),        // PRIMARY (index 1 — the platform does not enumerate it first)
        new(2285, 2592, 2304, 1368),  // third, below the primary
    ];

    [Fact]
    public void ChooseWorkArea_SavedRectOnSecondaryMonitor_ReturnsThatMonitor()
    {
        // The bug this exists for: asking the WINDOW which screen it is on returns the primary, because
        // at restore time the window is still wherever Win32 cascaded it. A rectangle saved at x=9500
        // would then be clamped against the primary's work area and yanked back onto it, making a
        // secondary monitor impossible to restore onto at all.
        var saved = new PlacementRect(9500, 100, 1000, 640);

        var index = WindowPlacement.ChooseWorkArea(saved, ThreeMonitors, primaryIndex: 1);

        Assert.Equal(0, index);
    }

    [Fact]
    public void ChooseWorkArea_SavedRectOnDisconnectedMonitor_FallsBackToPrimary()
    {
        // The monitor that used to live at x=9216 has been unplugged, so nothing contains this centre.
        var saved = new PlacementRect(9500, 100, 1000, 640);
        PlacementRect[] primaryOnly = [new(0, 0, 7680, 2088)];

        var index = WindowPlacement.ChooseWorkArea(saved, primaryOnly, primaryIndex: 0);

        Assert.Equal(0, index);
    }

    [Fact]
    public void ChooseWorkArea_NoSavedPosition_ReturnsPrimary()
    {
        // A fresh config remembers a size but no position, which means "centre me" — and centring
        // belongs on the primary, not on whichever monitor happens to be enumerated first.
        var index = WindowPlacement.ChooseWorkArea(null, ThreeMonitors, primaryIndex: 1);

        Assert.Equal(1, index);
    }

    [Fact]
    public void Resolve_RealNegativePosition_OnLeftHandMonitor_IsKept()
    {
        // A monitor to the LEFT of the primary occupies negative coordinates. This position is
        // remembered, not unset, and must survive: a negative-sentinel design would centre it on the
        // primary monitor instead, teleporting the window on every restart.
        var leftMonitor = new PlacementRect(-1920, 0, 1920, 1040);

        var r = WindowPlacement.Resolve(-1500, 120, 1000, 640, leftMonitor, 760, 520);

        Assert.Equal(-1500, r.Left);
        Assert.Equal(120, r.Top);
    }
}
