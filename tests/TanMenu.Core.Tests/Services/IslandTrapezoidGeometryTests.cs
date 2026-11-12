using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandTrapezoidGeometryTests
{
    [Fact]
    public void Collapsed_IsNarrowerAtTheTop_AndSitsOnTheBaseline()
    {
        var outline = IslandTrapezoidGeometry.Layout(0, 120, 32);

        Assert.Equal(120, outline.BottomWidth);
        Assert.Equal(32, outline.Height);
        Assert.Contains(outline.Points, point => point.Y > 31.9 && point.X < 0.1);
        Assert.Contains(outline.Points, point => point.Y > 31.9 && point.X > 119.9);
        Assert.DoesNotContain(outline.Points, point => point.Y > 32.01 || point.Y < -0.01);
        Assert.DoesNotContain(outline.Points, point => point.X < -0.01 || point.X > 120.01);
        Assert.True(TopWidth(outline) < 90);
    }

    [Fact]
    public void BottomFillet_EndsOnTheBaseline_WithoutASecondConvexFoot()
    {
        var outline = IslandTrapezoidGeometry.Layout(0, 120, 32);
        var foot = outline.Points.OrderBy(point => point.X).ThenByDescending(point => point.Y).First();
        var next = outline.Points[1];

        Assert.Equal(0, foot.X, 2);
        Assert.Equal(32, foot.Y, 2);
        Assert.True(next.Y < foot.Y);
        Assert.True(next.X > foot.X);
        var rise = foot.Y - next.Y;
        var run = next.X - foot.X;
        Assert.True(rise / run < 0.45, "the first step off the foot is nearly horizontal, not a vertical toe");
    }

    [Fact]
    public void ConcaveCutout_IsOutsideTheSharedOutline_AndTheBodyIsInside()
    {
        var collapsed = IslandTrapezoidGeometry.Layout(0, 320, 72);
        var expanded = IslandTrapezoidGeometry.Layout(1, 320, 72);

        Assert.False(IslandTrapezoidGeometry.Contains(collapsed, collapsed.ConcaveCutout.X, collapsed.ConcaveCutout.Y));
        Assert.False(IslandTrapezoidGeometry.Contains(expanded, expanded.ConcaveCutout.X, expanded.ConcaveCutout.Y));
        Assert.False(IslandTrapezoidGeometry.Contains(expanded, expanded.TopCutout.X, expanded.TopCutout.Y));
        Assert.True(IslandTrapezoidGeometry.Contains(collapsed, collapsed.Solid.X, collapsed.Solid.Y));
        Assert.True(IslandTrapezoidGeometry.Contains(expanded, expanded.Solid.X, expanded.Solid.Y));
        Assert.False(IslandTrapezoidGeometry.Contains(expanded, 0, 0));
    }

    [Fact]
    public void LeftEdge_WidensMonotonically_AndNeverCrossesTheBaseline()
    {
        var outline = IslandTrapezoidGeometry.Layout(0, 120, 32);
        var left = outline.Points.Take(outline.Points.Length / 2).ToArray();

        for (var i = 1; i < left.Length; i++)
        {
            Assert.True(left[i].Y <= left[i - 1].Y + 0.02);
            Assert.True(left[i].X + 0.05 >= left[i - 1].X);
        }

        Assert.True(left[^1].Y < 0.2);
        Assert.All(outline.Points, point => Assert.True(point.Y <= 32.01));
    }

    [Fact]
    public void Outline_IsSymmetric()
    {
        var outline = IslandTrapezoidGeometry.Layout(0.35, 320, 72);

        foreach (var point in outline.Points)
        {
            Assert.Contains(outline.Points, other =>
                Math.Abs(other.X - (320 - point.X)) < 0.02 &&
                Math.Abs(other.Y - point.Y) < 0.02);
        }
    }

    [Fact]
    public void DevicePolygon_ChangesWithScale_AndKeepsTheConcaveSamples()
    {
        var outline = IslandTrapezoidGeometry.Layout(0, 320, 72);
        Assert.True(IslandTrapezoidGeometry.TryGetDevicePoints(outline, 1, out var full));
        Assert.True(IslandTrapezoidGeometry.TryGetDevicePoints(outline, 1.5, out var scaled));

        Assert.NotEqual(full, scaled);
        Assert.True(full.Length > 20);
        Assert.Contains(full, point => point.X < 120 && point.Y > 60);
    }

    private static double TopWidth(IslandOutline outline)
    {
        var top = outline.Points.Where(point => point.Y < 0.2).Select(point => point.X).ToArray();
        return top.Max() - top.Min();
    }
}
