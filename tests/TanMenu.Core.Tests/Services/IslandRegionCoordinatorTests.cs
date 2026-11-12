using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandRegionCoordinatorTests
{
    [Fact]
    public void SameShapeAndScale_DoesNotCallNativeAgain()
    {
        var calls = 0;
        var coordinator = new IslandRegionCoordinator();

        var first = coordinator.Apply(320, 72, 120, 32, 16, 1, _ =>
        {
            calls++;
            return true;
        });
        var second = coordinator.Apply(320, 72, 120, 32, 16, 1, _ =>
        {
            calls++;
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.Applied, first);
        Assert.Equal(IslandRegionApplyKind.Unchanged, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ScaleChange_From100To150_RebuildsADifferentDeviceRegion()
    {
        var regions = new List<IslandDeviceRoundRegion>();
        var coordinator = new IslandRegionCoordinator();

        coordinator.Apply(320, 72, 120, 32, 16, 1, region =>
        {
            regions.Add(region);
            return true;
        });
        var kind = coordinator.Apply(320, 72, 120, 32, 16, 1.5, region =>
        {
            regions.Add(region);
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.Applied, kind);
        Assert.Equal(2, regions.Count);
        Assert.NotEqual(regions[0], regions[1]);
        Assert.Equal(150, regions[1].Left);
        Assert.Equal(60, regions[1].Top);
        Assert.Equal(330, regions[1].Right);
        Assert.Equal(108, regions[1].Bottom);
    }

    [Fact]
    public void InitialNativeFailure_ClosesAndDoesNotKeepARegion()
    {
        var coordinator = new IslandRegionCoordinator();
        var kind = coordinator.Apply(320, 72, 120, 32, 16, 1, _ => false);

        Assert.Equal(IslandRegionApplyKind.NativeFailed, kind);
        Assert.Null(coordinator.Applied);
        Assert.True(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: false));
    }

    [Fact]
    public void MidAnimationNativeFailure_ClosesWithoutReplacingTheLastGoodRegion()
    {
        var coordinator = new IslandRegionCoordinator();
        coordinator.Apply(320, 72, 120, 32, 16, 1, _ => true);
        var previous = coordinator.Applied;

        var kind = coordinator.Apply(320, 72, 220, 52, 26, 1, _ => false);

        Assert.Equal(IslandRegionApplyKind.NativeFailed, kind);
        Assert.Equal(previous, coordinator.Applied);
        Assert.True(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: true));
    }

    [Fact]
    public void InvalidGeometry_DoesNotCallNativeOrClearAnExistingRegion()
    {
        var calls = 0;
        var coordinator = new IslandRegionCoordinator();
        coordinator.Apply(320, 72, 120, 32, 16, 1, _ =>
        {
            calls++;
            return true;
        });
        var previous = coordinator.Applied;

        var kind = coordinator.Apply(320, 72, 0, 32, 16, 1, _ =>
        {
            calls++;
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.RejectedGeometry, kind);
        Assert.Equal(previous, coordinator.Applied);
        Assert.Equal(1, calls);
        Assert.False(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: true));
        Assert.True(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: false));
    }
}
