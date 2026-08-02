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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ResolveContentFit — Round 2's "size is recomputed from content on every start" feature.
    // The chrome terms below are 48/48 so each case reads as a concrete scenario rather than an
    // abstract one. They are no longer the app's LIVE terms, and this comment used to claim they were:
    // Round 4 item 1 added the content pane's left border to the width term and item 5 then added the
    // pane's top border to the height term, so WindowHost now passes 49 on both axes
    // (MainWindow.RailWidthDip + PaneBorderDip, and WindowHost.ChromeHeightDip). Nothing any case here
    // asserts depends on which value is passed — ResolveContentFit only ADDS the term to the content —
    // so they stay at 48 rather than being churned. 760x568 IS still the app's own minimum
    // (MainWindow.axaml's MinWidth/MinHeight, the single source of truth WindowHost reads directly).
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_SmallContent_IsRaisedToTheAppMinimum()
    {
        // Very little content (e.g. one or two thin categories) plus 48+48 DIP of chrome still lands
        // below 760x568 — the floor wins, exactly like a tiny saved size did before.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 300, contentHeight: 200,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 100, currentTop: 100,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(760, r.Width);
        Assert.Equal(568, r.Height);
    }

    [Fact]
    public void ResolveContentFit_ContentBetweenMinimumAndWorkArea_FitsExactly()
    {
        // The "5 categories: no scrollbar, no wasted space" case — content plus chrome above the
        // minimum but well inside the work area must come back UNCHANGED, not inflated to fill the
        // screen and not shrunk to the minimum.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 700,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 200, currentTop: 150,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(948, r.Width);   // 900 + 48
        Assert.Equal(748, r.Height);  // 700 + 48
        Assert.Equal(200, r.Left);
        Assert.Equal(150, r.Top);
    }

    [Fact]
    public void ResolveContentFit_ContentFarLargerThanWorkArea_ClampsInsteadOfGrowingOffScreen()
    {
        // "Force a content set far larger than the work area: confirm it clamps and scrolls" — the
        // window must never exceed the work area just because the content wants more.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 6000, contentHeight: 4000,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 0, currentTop: 0,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
    }

    [Fact]
    public void ResolveContentFit_UnsetPosition_CentresOnWorkArea()
    {
        // Fit-to-content always has a real current position by the time it runs (the window is already
        // placed and shown) — this documents that the null path still falls through to Resolve's own
        // centring, exercised directly by the other Resolve tests above.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 952, contentHeight: 592,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: null, currentTop: null,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(460, r.Left);   // (1920 - 1000) / 2
        Assert.Equal(200, r.Top);    // (1040 - 640) / 2
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Round 2's review round: the user changed height from a fixed 640 DIP reference to fully
    // content-driven — "the category with the most items fits in a single column without wrapping,
    // clamped to the work area", with NO comfort cap softening a tall result. WindowHost.FitToContent
    // supplies that height by probing the page at an UNBOUNDED height (WPF-side, not testable here
    // without a visual tree); what IS testable in Core is that Resolve/ResolveContentFit's own clamp
    // never reintroduces a hidden ceiling below the work area, and still clamps independently per axis
    // when content genuinely exceeds it.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_TallNarrowCategory_HeightNotArtificiallyCapped()
    {
        // A tall, narrow single category (e.g. ~20 items in one un-wrapped column) well under the work
        // area's own height (1040 here) must come back at its full natural height — no 640-ish reference
        // cap softening it, the way the retired fixed-height probe used to impose one.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 216, contentHeight: 900,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 100, currentTop: 100,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(760, r.Width);  // 216 + 48 = 264 falls below the app minimum, raised to it
        Assert.Equal(948, r.Height); // 900 + 48, kept in full — well short of the 1040 work-area ceiling
    }

    [Fact]
    public void ResolveContentFit_CategoryTallerThanWorkArea_HeightClampsButWidthStaysContentDriven()
    {
        // "A category taller than the work area clamps and wraps instead of growing off-screen" — the
        // two axes clamp INDEPENDENTLY: height clamps to the work area while width, comfortably under
        // it, is left untouched.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 5000,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 0, currentTop: 0,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(948, r.Width);   // 900 + 48, well under the work area — untouched
        Assert.Equal(1040, r.Height); // clamped to the work area's own height
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Round 2 review, finding 2: the navigation rail's width is a term in the content fit, so
    // expanding it (48 -> 200) has to widen the window by exactly that 152 DIP rather than take the
    // space out of the content pane and put the horizontal scrollbar back.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_ExpandingTheRail_WidensTheWindowByTheRailDelta()
    {
        var collapsed = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 100, currentTop: 100,
            workArea: Work, minWidth: 760, minHeight: 568);

        var expanded = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 200, chromeHeight: 48,
            currentLeft: 100, currentTop: 100,
            workArea: Work, minWidth: 760, minHeight: 568);

        Assert.Equal(948, collapsed.Width);
        Assert.Equal(1100, expanded.Width);
        Assert.Equal(152, expanded.Width - collapsed.Width);
        Assert.Equal(collapsed.Height, expanded.Height); // the rail is a width term only
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Round 2 review: several re-fit triggers can fire for one user action, so a fit that computes
    // the geometry the window already has must be recognisable as a no-op before it is applied.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsSamePlacement_IdenticalRects_IsTrue()
    {
        var a = new PlacementRect(100, 200, 948, 648);

        Assert.True(WindowPlacement.IsSamePlacement(a, a));
    }

    [Fact]
    public void IsSamePlacement_SubPixelDifference_IsStillTheSamePlacement()
    {
        // Avalonia's Win32 backend truncates DIP to physical pixels before SetWindowPlacement and
        // writes the converted-back value into Width/Height, so a re-fit at 150% scaling can legally
        // report a size a fraction of a DIP away from the one it computed.
        var computed = new PlacementRect(100, 200, 948, 648);
        var reported = new PlacementRect(100.33, 199.67, 948.33, 647.67);

        Assert.True(WindowPlacement.IsSamePlacement(computed, reported));
    }

    [Theory]
    [InlineData(101, 200, 948, 648)] // moved
    [InlineData(100, 201, 948, 648)]
    [InlineData(100, 200, 949, 648)] // resized
    [InlineData(100, 200, 948, 649)]
    public void IsSamePlacement_AWholeDipOnAnyEdge_IsADifferentPlacement(
        double left, double top, double width, double height)
    {
        var a = new PlacementRect(100, 200, 948, 648);

        Assert.False(WindowPlacement.IsSamePlacement(a, new PlacementRect(left, top, width, height)));
    }

    [Fact]
    public void IsSamePlacement_ShrinkingFit_DoesNotMoveTheWindow()
    {
        // Round 2 review finding 7's other half, stated as a test rather than only in a comment: a fit
        // that SHRINKS the window never re-clamps its position, because a smaller rectangle at the same
        // origin is still inside the work area. Only a fit that would push the window off the right or
        // bottom edge moves it, and then only by the overflow.
        var start = new PlacementRect(1500, 800, 400, 200);

        var smaller = WindowPlacement.ResolveContentFit(
            contentWidth: 252, contentHeight: 102,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: start.Left, currentTop: start.Top,
            workArea: Work, minWidth: 0, minHeight: 0);

        Assert.Equal(300, smaller.Width);
        Assert.Equal(150, smaller.Height);
        Assert.Equal(start.Left, smaller.Left);
        Assert.Equal(start.Top, smaller.Top);
    }

    [Fact]
    public void ResolveContentFit_GrowthPastTheEdge_MovesByExactlyTheOverflow()
    {
        // The counterpart: growing a window that sits near the right edge moves it left by the overflow
        // and no further — the minimum move that keeps the new size on screen.
        var grown = WindowPlacement.ResolveContentFit(
            contentWidth: 552, contentHeight: 102,
            chromeWidth: 48, chromeHeight: 48,
            currentLeft: 1500, currentTop: 800,
            workArea: Work, minWidth: 0, minHeight: 0);

        Assert.Equal(600, grown.Width);
        Assert.Equal(1320, grown.Left); // 1920 - 600, i.e. 1500 shifted left by the 180 of overflow
        Assert.Equal(800, grown.Top);   // vertically it still fits: 800 + 150 <= 1040
    }
}
