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

        var first = coordinator.Apply(0, 320, 72, 1, _ =>
        {
            calls++;
            return true;
        });
        var second = coordinator.Apply(0, 320, 72, 1, _ =>
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
        var regions = new List<IslandDevicePoint[]>();
        var coordinator = new IslandRegionCoordinator();

        coordinator.Apply(0, 320, 72, 1, region =>
        {
            regions.Add(region);
            return true;
        });
        var kind = coordinator.Apply(0, 320, 72, 1.5, region =>
        {
            regions.Add(region);
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.Applied, kind);
        Assert.NotEqual(regions[0], regions[1]);
    }

    [Fact]
    public void ProgressChange_Rebuilds_ButIconLevelIsNotARegionInput()
    {
        var calls = 0;
        var coordinator = new IslandRegionCoordinator();
        coordinator.Apply(0, 320, 72, 1, _ =>
        {
            calls++;
            return true;
        });

        var kind = coordinator.Apply(1, 320, 72, 1, _ =>
        {
            calls++;
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.Applied, kind);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void InitialNativeFailure_ClosesAndDoesNotKeepARegion()
    {
        var coordinator = new IslandRegionCoordinator();
        var kind = coordinator.Apply(0, 320, 72, 1, _ => false);

        Assert.Equal(IslandRegionApplyKind.NativeFailed, kind);
        Assert.Null(coordinator.Applied);
        Assert.True(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: false));
    }

    [Fact]
    public void MidAnimationNativeFailure_ClosesWithoutReplacingTheLastGoodRegion()
    {
        var coordinator = new IslandRegionCoordinator();
        coordinator.Apply(0, 320, 72, 1, _ => true);
        var previous = coordinator.Applied;

        var kind = coordinator.Apply(0.5, 320, 72, 1, _ => false);

        Assert.Equal(IslandRegionApplyKind.NativeFailed, kind);
        Assert.Equal(previous, coordinator.Applied);
        Assert.True(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: true));
    }

    [Fact]
    public void InvalidHost_DoesNotCallNativeOrClearAnExistingRegion()
    {
        var calls = 0;
        var coordinator = new IslandRegionCoordinator();
        coordinator.Apply(0, 320, 72, 1, _ =>
        {
            calls++;
            return true;
        });
        var previous = coordinator.Applied;

        var kind = coordinator.Apply(0, 0, 72, 1, _ =>
        {
            calls++;
            return true;
        });

        Assert.Equal(IslandRegionApplyKind.RejectedGeometry, kind);
        Assert.Equal(previous, coordinator.Applied);
        Assert.Equal(1, calls);
        Assert.False(IslandRegionCoordinator.ShouldCloseOverlay(kind, overlayVisible: true));
    }
}
