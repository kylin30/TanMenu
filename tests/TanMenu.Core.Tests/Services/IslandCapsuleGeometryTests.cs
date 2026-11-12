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
    public void ZeroRadius_DoesNotFillTheBoundingRectangle()
    {
        var box = IslandCapsuleGeometry.Layout(320, 72, 120, 32, 0);

        Assert.Equal(0, box.Radius);
        Assert.False(IslandCapsuleGeometry.Contains(box, 160, 56));
        Assert.False(IslandCapsuleGeometry.TryGetDeviceRoundRegion(box, 1, 320, 72, out _));
    }

    [Fact]
    public void CollapsedRoundRegion_ExcludesTheHostMarginAndTheSquareCorner()
    {
        var capsule = IslandCapsuleGeometry.Layout(320, 72, 120, 32, 16);
        Assert.True(IslandCapsuleGeometry.TryGetDeviceRoundRegion(capsule, 1, 320, 72, out var region));

        Assert.Equal(100, region.Left);
        Assert.Equal(40, region.Top);
        Assert.Equal(220, region.Right);
        Assert.Equal(72, region.Bottom);
        Assert.Equal(32, region.Diameter);
        Assert.True(region.Top > 0);
        Assert.True(region.Left > 0);
        Assert.False(IslandCapsuleGeometry.DeviceRoundRegionContains(region, 100, 40));
        Assert.True(IslandCapsuleGeometry.DeviceRoundRegionContains(region, 160, 56));
        Assert.False(IslandCapsuleGeometry.DeviceRoundRegionContains(region, 10, 10));
    }

    [Fact]
    public void TweenSample_StaysACapsuleInsideTheHost()
    {
        var progress = 1 - Math.Pow(1 - 0.5, 3);
        var width = 120 + (320 - 120) * progress;
        var height = 32 + (72 - 32) * progress;
        var radius = 16 + (36 - 16) * progress;
        Assert.Equal(height / 2, radius, 5);

        var capsule = IslandCapsuleGeometry.Layout(320, 72, width, height, radius);
        Assert.True(IslandCapsuleGeometry.TryGetDeviceRoundRegion(capsule, 1, 320, 72, out var region));
        Assert.True(region.Top > 0);
        Assert.True(region.Diameter + 1 >= region.Bottom - region.Top);
        Assert.False(IslandCapsuleGeometry.DeviceRoundRegionContains(region, 0, 0));
    }

    [Fact]
    public void FractionalCapsule_RoundsOutwardWithoutBecomingTheHostRectangle()
    {
        var capsule = IslandCapsuleGeometry.Layout(400, 80, 20.2, 10.2, 5.1);
        Assert.True(IslandCapsuleGeometry.TryGetDeviceRoundRegion(capsule, 1, 400, 80, out var region));

        Assert.True(region.Left <= capsule.X);
        Assert.True(region.Top <= capsule.Y);
        Assert.True(region.Right >= capsule.X + capsule.Width);
        Assert.True(region.Bottom >= capsule.Y + capsule.Height);
        Assert.True(region.Top > 0);
        Assert.True(region.Diameter + 1 >= region.Bottom - region.Top);
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
