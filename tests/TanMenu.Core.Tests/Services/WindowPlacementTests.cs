using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class WindowPlacementTests
{
    private static readonly PlacementRect Work = new(0, 0, 1920, 1040);

    /// <summary>The gap the app itself uses (<c>WindowHost.BottomGapDip</c>), mirrored here so the
    /// expected numbers below read as the shipped behaviour rather than as an arbitrary fixture. Cases
    /// that are about the gap itself pass their own value.</summary>
    private const double Gap = 8;

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ResolveBottomCentred — Round 4, item 4's one placement rule: clamp the size, centre it
    // horizontally on the work area, then put its bottom edge Gap DIPs above the work area's.
    //
    // It replaced Resolve (a saved rectangle clamped into view) and ChooseWorkArea (which monitor a
    // saved rectangle belonged to). Both are gone, together with every case that pinned them — a saved
    // position is no longer read (GeneralConfig.WindowLeft/WindowTop are retired in place), so there is
    // no remembered rectangle to keep, to pull back inside an edge, or to attribute to a monitor. The
    // three-monitor fixture those cases shared went with them; its comment claimed "Work areas are
    // DIPs" over numbers that were this machine's PHYSICAL pixels, so nothing here inherits it.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveBottomCentred_CentresHorizontallyAndAnchorsLow()
    {
        var r = WindowPlacement.ResolveBottomCentred(1000, 640, Work, 760, 520, Gap);

        Assert.Equal(460, r.Left);   // (1920 - 1000) / 2
        Assert.Equal(392, r.Top);    // 1040 - 8 - 640
        Assert.Equal(1000, r.Width);
        Assert.Equal(640, r.Height);
    }

    [Fact]
    public void ResolveBottomCentred_HonoursTheBottomGapExactly()
    {
        // The anchor is stated as an invariant on the resolved rectangle, not as a literal Top: the
        // bottom edge sits exactly Gap DIPs above the work area's, whatever the height turned out to be.
        // (This absorbs the retired "unset position centres on the work area" case: there is no unset
        // position any more — every placement is centred horizontally and anchored vertically.)
        var r = WindowPlacement.ResolveBottomCentred(1000, 640, Work, 760, 520, Gap);
        var tighter = WindowPlacement.ResolveBottomCentred(1000, 640, Work, 760, 520, Gap + 40);

        Assert.Equal(Work.Bottom - Gap, r.Bottom);
        Assert.Equal(Work.Left + (Work.Width - r.Width) / 2, r.Left);
        Assert.Equal(r.Top - 40, tighter.Top);      // a larger gap lifts the window by exactly the delta
        Assert.Equal(r.Left, tighter.Left);         // the gap is a vertical term only
    }

    [Fact]
    public void ResolveBottomCentred_ZeroGap_SitsFlushOnTheWorkAreaBottom()
    {
        // Not what the app ships (see WindowHost.BottomGapDip for why 8 and not 0), but the degenerate
        // end of the same arithmetic: gap 0 must land the rectangle exactly on the work area's bottom
        // edge, never one DIP past it.
        var r = WindowPlacement.ResolveBottomCentred(1000, 640, Work, 760, 520, bottomGap: 0);

        Assert.Equal(Work.Bottom, r.Bottom);
        Assert.Equal(400, r.Top);   // 1040 - 0 - 640
    }

    [Fact]
    public void ResolveBottomCentred_GapLargerThanTheSpareHeight_PinsTheTopToTheWorkArea()
    {
        // A gap bigger than what is left over after the height must not push the window off the TOP of
        // the work area. 1040 - 200 - 1000 = -160, which clamps to the work area's own top.
        var r = WindowPlacement.ResolveBottomCentred(1000, 1000, Work, 760, 520, bottomGap: 200);

        Assert.Equal(1000, r.Height);
        Assert.Equal(Work.Top, r.Top);
        Assert.True(r.Bottom <= Work.Bottom);
    }

    [Fact]
    public void ResolveBottomCentred_ContentAsTallAsTheWorkArea_TopAlignsAndFills()
    {
        // The clamp's degenerate case: once the height has been clamped it can never exceed the work
        // area, so workArea.Bottom - height is never above workArea.Top, and a window that fills the
        // work area exactly simply has no gap available. Both an exact fill and an over-tall request
        // land on the same rectangle.
        var exact = WindowPlacement.ResolveBottomCentred(1000, 1040, Work, 760, 568, Gap);
        var over = WindowPlacement.ResolveBottomCentred(1000, 1200, Work, 760, 568, Gap);

        Assert.Equal(Work.Top, exact.Top);
        Assert.Equal(Work.Height, exact.Height);
        Assert.Equal(Work.Bottom, exact.Bottom);
        Assert.Equal(exact, over);
    }

    [Fact]
    public void ResolveBottomCentred_SizeLargerThanWorkArea_IsShrunkToFit()
    {
        var r = WindowPlacement.ResolveBottomCentred(4000, 3000, Work, 760, 520, Gap);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void ResolveBottomCentred_SizeBelowMinimum_IsRaisedToMinimum()
    {
        var r = WindowPlacement.ResolveBottomCentred(100, 80, Work, 760, 520, Gap);

        Assert.Equal(760, r.Width);
        Assert.Equal(520, r.Height);
        Assert.Equal(580, r.Left);   // (1920 - 760) / 2
        Assert.Equal(512, r.Top);    // 1040 - 8 - 520
    }

    [Fact]
    public void ResolveBottomCentred_WorkAreaSmallerThanMinimum_PrefersFittingTheScreen()
    {
        // A window larger than the display cannot be moved back into view; one below the nominal
        // minimum is merely cramped. Fitting the screen therefore wins over the minimum.
        var tiny = new PlacementRect(0, 0, 640, 400);

        var r = WindowPlacement.ResolveBottomCentred(1000, 640, tiny, 760, 520, Gap);

        Assert.Equal(640, r.Width);
        Assert.Equal(400, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void ResolveBottomCentred_WorkAreaWithOffset_PositionsRelativeToIt()
    {
        // A left-hand secondary monitor: the work area does not start at the origin, and BOTH the
        // centring and the anchor are relative to it. (WindowHost only ever anchors on the primary at
        // restore time, but a re-fit uses whichever monitor the window is currently on.)
        var offset = new PlacementRect(-1920, 0, 1920, 1040);

        var r = WindowPlacement.ResolveBottomCentred(1000, 640, offset, 760, 520, Gap);

        Assert.Equal(-1460, r.Left);  // -1920 + (1920 - 1000) / 2
        Assert.Equal(392, r.Top);     // 1040 - 8 - 640
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ResolveContentFit — Round 2's "size is recomputed from content on every start" feature, now
    // placed by the rule above rather than pinned to wherever the window happened to be.
    // The chrome terms below are 48/48 so each case reads as a concrete scenario rather than an
    // abstract one. They are NOT the app's live terms, and neither is the 760 minimum. As of Round 8,
    // Task 3 the live terms are chromeWidth = 0 (MainWindow.ChromeWidthDip — the navigation pane and
    // the content pane's left hairline are deleted, and the band that replaced them sits ABOVE the
    // page) and chromeHeight = 55 (WindowHost.ChromeHeightDip = TitleStripHeightDip 30 +
    // MenuBandHeightDip 25), against an app minimum of 711x568 (WindowHost.MinWindowWidthDip /
    // MinWindowHeightDip; MinWidth="711" is additionally declared in MainWindow.axaml, MinHeight
    // deliberately is not — see MinWindowHeightDip's own remarks).
    // Nothing any case here asserts depends on which values are passed — ResolveContentFit only ADDS
    // the chrome term to the content, which the two chrome cases below pin directly, and only RAISES
    // to the minimum, which the first case below pins directly — so the arguments stay put rather than
    // being churned every time the shell moves.
    // THIS BLOCK IS A REPEAT OFFENDER AND THE DATES SAY SO. It said "49 on both axes" and "760x568"
    // right up to Task 3, by which time the live terms had been 161x31 (RailWidthDip 160 +
    // PaneBorderDip 1, ChromeHeightDip 30 + 1) against an 872 minimum since Round 7, Task 8 — so it
    // was already two rounds stale BEFORE Task 3 deleted the two constants it cited by name. Every
    // number above is re-derived off the live constants here rather than adjusted off the old text.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_SmallContent_IsRaisedToTheAppMinimum()
    {
        // Very little content (e.g. one or two thin categories) plus 48+48 DIP of chrome still lands
        // below 760x568 — the floor wins, and the anchored position is computed from the FLOORED size.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 300, contentHeight: 200,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(760, r.Width);
        Assert.Equal(568, r.Height);
        Assert.Equal(580, r.Left);   // (1920 - 760) / 2
        Assert.Equal(464, r.Top);    // 1040 - 8 - 568
    }

    [Fact]
    public void ResolveContentFit_ContentBetweenMinimumAndWorkArea_FitsExactly()
    {
        // The "5 categories: no scrollbar, no wasted space" case — content plus chrome above the
        // minimum but well inside the work area must come back UNCHANGED in SIZE, not inflated to fill
        // the screen and not shrunk to the minimum. Its POSITION is the anchor, not a preserved one.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 700,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(948, r.Width);   // 900 + 48
        Assert.Equal(748, r.Height);  // 700 + 48
        Assert.Equal(486, r.Left);    // (1920 - 948) / 2
        Assert.Equal(284, r.Top);     // 1040 - 8 - 748
    }

    [Fact]
    public void ResolveContentFit_ContentFarLargerThanWorkArea_ClampsInsteadOfGrowingOffScreen()
    {
        // "Force a content set far larger than the work area: confirm it clamps and scrolls" — the
        // window must never exceed the work area just because the content wants more.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 6000, contentHeight: 4000,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void ResolveContentFit_NegativeOriginWorkArea_CentresAndAnchorsRelativeToIt()
    {
        // A monitor above and to the left of the primary occupies negative coordinates on both axes.
        // Nothing in the arithmetic may assume a (0,0) origin — an earlier design's negative-coordinate
        // hazard was about a saved position, but the centring and the anchor have the same exposure.
        var upperLeft = new PlacementRect(-1920, -300, 1920, 1040);

        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 700,
            chromeWidth: 48, chromeHeight: 48,
            workArea: upperLeft, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(948, r.Width);
        Assert.Equal(748, r.Height);
        Assert.Equal(-1434, r.Left);              // -1920 + (1920 - 948) / 2
        Assert.Equal(-16, r.Top);                 // 740 - 8 - 748
        Assert.Equal(upperLeft.Bottom - Gap, r.Bottom);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Round 2's review round: the user changed height from a fixed 640 DIP reference to fully
    // content-driven — "the category with the most items fits in a single column without wrapping,
    // clamped to the work area", with NO comfort cap softening a tall result. WindowHost.FitToContent
    // supplies that height by probing the page at an UNBOUNDED height (WPF-side, not testable here
    // without a visual tree); what IS testable in Core is that the placement clamp never reintroduces a
    // hidden ceiling below the work area, and still clamps independently per axis when content
    // genuinely exceeds it.
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
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(760, r.Width);  // 216 + 48 = 264 falls below the app minimum, raised to it
        Assert.Equal(948, r.Height); // 900 + 48, kept in full — well short of the 1040 work-area ceiling
        Assert.Equal(580, r.Left);   // (1920 - 760) / 2
        Assert.Equal(84, r.Top);     // 1040 - 8 - 948
    }

    [Fact]
    public void ResolveContentFit_CategoryTallerThanWorkArea_HeightClampsButWidthStaysContentDriven()
    {
        // "A category taller than the work area clamps and wraps instead of growing off-screen" — the
        // two axes clamp INDEPENDENTLY: height clamps to the work area while width, comfortably under
        // it, is left untouched. The height clamp then costs the gap, and the window top-aligns.
        var r = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 5000,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(948, r.Width);   // 900 + 48, well under the work area — untouched
        Assert.Equal(1040, r.Height); // clamped to the work area's own height
        Assert.Equal(486, r.Left);    // still centred
        Assert.Equal(Work.Top, r.Top);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The chrome terms. Round 2 review, finding 2 pinned this as "expanding the rail (48 -> 200) must
    // widen the WINDOW by exactly 152 rather than take the space out of the content pane and put the
    // horizontal scrollbar back". Round 4, item 1 removed the rail's second width, so the scenario is
    // gone; the invariant it protected is not, and the vertical twin it acquired outlived its own first
    // cause too — that twin was the 应用 pane's 1 DIP top border (Round 4, item 5), and since Round 8,
    // Task 3 deleted that border the height term WindowHost.ChromeHeightDip adds back is the caption
    // strip plus the menu band. The two cases below are deliberately written in bare numbers, not in
    // the shell's constants, so that neither move invalidates them.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_ChromeWidthIsAddedToTheContentWidth()
    {
        var bare = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 0, chromeHeight: 49,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        var withChrome = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 49, chromeHeight: 49,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(900, bare.Width);
        Assert.Equal(949, withChrome.Width);
        Assert.Equal(49, withChrome.Width - bare.Width);
        Assert.Equal(bare.Height, withChrome.Height);          // a width term only
        Assert.Equal(485.5, withChrome.Left);                  // (1920 - 949) / 2 — still centred
    }

    [Fact]
    public void ResolveContentFit_ChromeHeightIsAddedToTheContentHeight()
    {
        var bare = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 49, chromeHeight: 0,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        var withChrome = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 600,
            chromeWidth: 49, chromeHeight: 49,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(600, bare.Height);
        Assert.Equal(649, withChrome.Height);
        Assert.Equal(49, withChrome.Height - bare.Height);
        Assert.Equal(bare.Width, withChrome.Width);            // a height term only
        Assert.Equal(bare.Bottom, withChrome.Bottom);          // both anchored: the TOP absorbs the 49
        Assert.Equal(bare.Top - 49, withChrome.Top);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Round 2 review: several re-fit triggers can fire for one user action, so a fit that computes
    // the geometry the window already has must be recognisable as a no-op before it is applied. Since
    // Round 4, item 4 the same test carries the opposite weight too: every re-fit re-places the window,
    // so a placement that differs only in POSITION must not be mistaken for a no-op — which is why
    // WindowHost's skip basis is the whole previously-requested rectangle, not just its size.
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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // What a re-fit does to the window's position now. Round 2's review finding 7 asked for a
    // deliberate decision and got "move only by the overflow, never on a shrink"; Round 4, item 4
    // supersedes it — the bottom edge is the anchored one, so a shrink moves the window DOWN and growth
    // is symmetric about the centre and cannot overflow at all.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveContentFit_ShrinkingFit_MovesTheWindowDownByExactlyTheHeightDelta()
    {
        var before = WindowPlacement.ResolveContentFit(
            contentWidth: 552, contentHeight: 402,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 0, minHeight: 0, bottomGap: Gap);

        var after = WindowPlacement.ResolveContentFit(
            contentWidth: 552, contentHeight: 202,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 0, minHeight: 0, bottomGap: Gap);

        Assert.Equal(450, before.Height);
        Assert.Equal(250, after.Height);
        Assert.Equal(before.Top + 200, after.Top);   // down by exactly the 200 DIP the window lost
        Assert.Equal(before.Bottom, after.Bottom);   // because the BOTTOM edge is the anchored one
        Assert.Equal(before.Left, after.Left);       // width unchanged, so the centring is unchanged
    }

    [Fact]
    public void ResolveContentFit_GrowthCannotOverflow_StaysCentredAndGapped()
    {
        // The counterpart of the shrink. A grown window used to be re-clamped left by its overflow;
        // now it simply grows symmetrically about the work area's centre, and the size is clamped to
        // the work area BEFORE the position is derived from it, so there is no overflow to correct.
        var grown = WindowPlacement.ResolveContentFit(
            contentWidth: 1800, contentHeight: 900,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(1848, grown.Width);
        Assert.Equal(948, grown.Height);
        Assert.Equal(36, grown.Left);                     // (1920 - 1848) / 2
        Assert.True(grown.Right <= Work.Right);
        Assert.Equal(Work.Bottom - Gap, grown.Bottom);
    }

    [Fact]
    public void ResolveContentFit_EveryReFitReAnchors_EvenWhenOnlyTheWidthChanges()
    {
        // §6.1's invariant, at the arithmetic level: all five surviving re-fit triggers move the window
        // as well as resizing it. A width-only change must re-centre — the two rectangles differ in
        // Left, so WindowHost's skip test sees a different placement and applies it.
        var narrow = WindowPlacement.ResolveContentFit(
            contentWidth: 900, contentHeight: 700,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        var wide = WindowPlacement.ResolveContentFit(
            contentWidth: 1100, contentHeight: 700,
            chromeWidth: 48, chromeHeight: 48,
            workArea: Work, minWidth: 760, minHeight: 568, bottomGap: Gap);

        Assert.Equal(486, narrow.Left);   // (1920 - 948) / 2
        Assert.Equal(386, wide.Left);     // (1920 - 1148) / 2
        Assert.Equal(narrow.Top, wide.Top);
        Assert.False(WindowPlacement.IsSamePlacement(narrow, wide));
    }
}
