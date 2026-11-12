using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandTaskbarObservationTests
{
    [Fact]
    public void FailedQuery_StaysUnknown()
    {
        var (autoHide, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: false, state: 1, edge: 3, left: 0, top: 1040, right: 1920, bottom: 1080,
            scale: 1, positionKnown: true);

        Assert.Null(autoHide);
        Assert.Null(thickness);
    }

    [Fact]
    public void BottomTaskbar_ConvertsThicknessToDip_AndReportsAutoHide()
    {
        var (autoHide, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: true, state: IslandTaskbarObservation.AbsAutoHide, edge: 3,
            left: 0, top: 1000, right: 1920, bottom: 1080, scale: 1.5, positionKnown: true);

        Assert.True(autoHide);
        Assert.Equal(80 / 1.5, thickness);
    }

    [Fact]
    public void AlwaysOnTaskbar_IsNotAutoHide()
    {
        var (autoHide, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: true, state: 0x2, edge: 3,
            left: 0, top: 1040, right: 1920, bottom: 1080, scale: 1, positionKnown: true);

        Assert.False(autoHide);
        Assert.Equal(40, thickness);
    }

    [Fact]
    public void SideTaskbar_UsesTheHorizontalThickness()
    {
        var (_, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: true, state: 0, edge: 0,
            left: 0, top: 0, right: 62, bottom: 1080, scale: 1, positionKnown: true);

        Assert.Equal(62, thickness);
    }

    [Fact]
    public void MissingPosition_KeepsAutoHide_AndLeavesThicknessUnknown()
    {
        var (autoHide, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: true, state: IslandTaskbarObservation.AbsAutoHide, edge: -1,
            left: 0, top: 0, right: 0, bottom: 0, scale: 1, positionKnown: false);

        Assert.True(autoHide);
        Assert.Null(thickness);
    }

    [Fact]
    public void NonPositiveThickness_IsTreatedAsUnread()
    {
        var (_, thickness) = IslandTaskbarObservation.Interpret(
            querySucceeded: true, state: 0, edge: 3,
            left: 0, top: 10, right: 100, bottom: 10, scale: 1, positionKnown: true);

        Assert.Null(thickness);
    }
}
