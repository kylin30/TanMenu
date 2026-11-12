using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandCapsuleGeometryTests
{
    [Fact]
    public void CollapsedCapsule_SitsOnTheBottomCenterOfTheFixedHost()
    {
        var capsule = IslandCapsuleGeometry.Layout(320, 72, 120, 32, 16);

        Assert.Equal(100, capsule.X);
        Assert.Equal(40, capsule.Y);
        Assert.Equal(120, capsule.Width);
        Assert.Equal(32, capsule.Height);
        Assert.Equal(16, capsule.Radius);
    }

    [Fact]
    public void ExpandedCapsule_FillsTheHostAndKeepsACapsuleRadius()
    {
        var capsule = IslandCapsuleGeometry.Layout(320, 72, 320, 72, 36);

        Assert.Equal(0, capsule.X);
        Assert.Equal(0, capsule.Y);
        Assert.Equal(320, capsule.Width);
        Assert.Equal(72, capsule.Height);
        Assert.Equal(36, capsule.Radius);
    }

    [Fact]
    public void HitTest_AcceptsTheSolidCapsule_AndRejectsTheTransparentMargin()
    {
        var collapsed = IslandCapsuleGeometry.Layout(320, 72, 120, 32, 16);

        Assert.True(IslandCapsuleGeometry.Contains(collapsed, 160, 56));
        Assert.True(IslandCapsuleGeometry.Contains(collapsed, 116, 40));
        Assert.False(IslandCapsuleGeometry.Contains(collapsed, 100, 40));
        Assert.False(IslandCapsuleGeometry.Contains(collapsed, 10, 10));
        Assert.False(IslandCapsuleGeometry.Contains(collapsed, 160, 20));

        var expanded = IslandCapsuleGeometry.Layout(320, 72, 320, 72, 36);
        Assert.True(IslandCapsuleGeometry.Contains(expanded, 160, 36));
        Assert.False(IslandCapsuleGeometry.Contains(expanded, 0, 0));
    }

    [Fact]
    public void RoundRectRegion_UsesExclusiveDevicePixelsInsideTheHost()
    {
        var capsule = IslandCapsuleGeometry.Layout(320, 72, 120, 32, 16);
        var region = IslandCapsuleGeometry.ToExclusiveRoundRect(capsule, scale: 1.5, hostWidthPx: 480, hostHeightPx: 108);

        Assert.Equal(150, region.Left);
        Assert.Equal(60, region.Top);
        Assert.Equal(330, region.Right);
        Assert.Equal(108, region.Bottom);
        Assert.Equal(48, region.Diameter);
        Assert.True(region.Right <= 480);
        Assert.True(region.Bottom <= 108);
    }

    [Fact]
    public void UnpackNcHitTestLParam_PreservesSignedScreenCoordinates()
    {
        var packed = (nint)((200 << 16) | 100);
        var (x, y) = IslandCapsuleGeometry.UnpackNcHitTestLParam(packed);
        Assert.Equal(100, x);
        Assert.Equal(200, y);

        var negative = (nint)(int)((unchecked((ushort)(short)-20) << 16) | unchecked((ushort)(short)-1));
        var (nx, ny) = IslandCapsuleGeometry.UnpackNcHitTestLParam(negative);
        Assert.Equal(-1, nx);
        Assert.Equal(-20, ny);
    }
}
