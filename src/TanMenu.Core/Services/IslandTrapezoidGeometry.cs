namespace TanMenu.Core.Services;

public readonly record struct IslandPoint(double X, double Y);

/// <summary>One outline shared by drawing, hit testing, and the native polygon.</summary>
public readonly record struct IslandOutline(
    double Progress,
    double BottomWidth,
    double Height,
    IslandPoint[] Points,
    IslandPoint ConcaveCutout,
    IslandPoint TopCutout,
    IslandPoint Solid);

public readonly record struct IslandDevicePoint(int X, int Y);

/// <summary>
/// Narrow-top trapezoid. Top corners are large convex arcs. Each bottom corner is a single
/// reverse fillet that ends on the horizontal baseline: no second radius, no vertical toe.
/// </summary>
public static class IslandTrapezoidGeometry
{
    public static IslandOutline Layout(double progress, double hostWidth, double hostHeight)
    {
        var t = Clamp(progress);
        var bottom = Lerp(IslandMetrics.CollapsedBottomWidth, IslandMetrics.ExpandedBottomWidth, t);
        var height = Lerp(IslandMetrics.CollapsedHeight, IslandMetrics.ExpandedHeight, t);
        var fillet = Lerp(IslandMetrics.CollapsedFilletRadius, IslandMetrics.ExpandedFilletRadius, t);
        var topRadius = Lerp(IslandMetrics.CollapsedTopRadius, IslandMetrics.ExpandedTopRadius, t);
        var local = BuildLocal(bottom, height, fillet, topRadius, IslandMetrics.ArcSteps);
        var offsetX = (hostWidth - bottom) / 2;
        var offsetY = hostHeight - height;
        var points = new IslandPoint[local.Points.Length];
        for (var i = 0; i < local.Points.Length; i++)
            points[i] = new IslandPoint(local.Points[i].X + offsetX, local.Points[i].Y + offsetY);

        return new IslandOutline(
            t,
            bottom,
            height,
            points,
            Shift(local.ConcaveCutout, offsetX, offsetY),
            Shift(local.TopCutout, offsetX, offsetY),
            new IslandPoint(hostWidth / 2, hostHeight - 2));
    }

    public static bool Contains(IslandOutline outline, double x, double y)
    {
        var points = outline.Points;
        if (points.Length < 3)
            return false;

        var inside = false;
        var j = points.Length - 1;
        for (var i = 0; i < points.Length; i++)
        {
            var yi = points[i].Y;
            var yj = points[j].Y;
            if ((yi > y) != (yj > y))
            {
                var xi = points[i].X;
                var xj = points[j].X;
                var cross = (xj - xi) * (y - yi) / (yj - yi) + xi;
                if (x < cross)
                    inside = !inside;
            }

            j = i;
        }

        return inside;
    }

    public static bool TryGetDevicePoints(IslandOutline outline, double scale, out IslandDevicePoint[] points)
    {
        points = [];
        if (outline.Points.Length < 3)
            return false;
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;

        var raw = new IslandDevicePoint[outline.Points.Length];
        var count = 0;
        foreach (var point in outline.Points)
        {
            var device = new IslandDevicePoint(Round(point.X * scale), Round(point.Y * scale));
            if (count > 0 && raw[count - 1].Equals(device))
                continue;
            raw[count++] = device;
        }

        if (count > 1 && raw[0].Equals(raw[count - 1]))
            count--;
        if (count < 8)
            return false;

        points = raw[..count];
        return true;
    }

    private readonly record struct LocalOutline(IslandPoint[] Points, IslandPoint ConcaveCutout, IslandPoint TopCutout);

    private static LocalOutline BuildLocal(double bottom, double height, double fillet, double topRadius, int steps)
    {
        var sweep = IslandMetrics.FilletSweepDegrees * Math.PI / 180;
        var cos = Math.Cos(sweep);
        var sin = Math.Sin(sweep);
        var filletPoints = new IslandPoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var theta = sweep * i / steps;
            filletPoints[i] = new IslandPoint(
                fillet * Math.Sin(theta),
                height - fillet + fillet * Math.Cos(theta));
        }

        var sideStart = filletPoints[^1];
        var up = new IslandPoint(cos, -sin);
        var outward = new IslandPoint(-sin, -cos);
        var sideEndY = topRadius * (1 - cos);
        var run = (sideEndY - sideStart.Y) / up.Y;
        var sideEnd = new IslandPoint(sideStart.X + run * up.X, sideEndY);
        var topCenterX = sideEnd.X - topRadius * outward.X;
        var sideAngle = Math.Atan2(outward.Y, outward.X);
        var topAngle = -Math.PI / 2;
        var delta = NormalizeAngle(topAngle - sideAngle);

        var topPoints = new IslandPoint[steps];
        for (var i = 1; i <= steps; i++)
        {
            var theta = sideAngle + delta * i / steps;
            topPoints[i - 1] = new IslandPoint(
                topCenterX + topRadius * Math.Cos(theta),
                topRadius + topRadius * Math.Sin(theta));
        }

        var leftCount = filletPoints.Length + 1 + topPoints.Length;
        var left = new IslandPoint[leftCount];
        filletPoints.CopyTo(left, 0);
        left[filletPoints.Length] = sideEnd;
        topPoints.CopyTo(left, filletPoints.Length + 1);

        var points = new IslandPoint[left.Length * 2];
        left.CopyTo(points, 0);
        for (var i = 0; i < left.Length; i++)
        {
            var source = left[left.Length - 1 - i];
            points[left.Length + i] = new IslandPoint(bottom - source.X, source.Y);
        }

        var foot = filletPoints[0];
        var cutout = new IslandPoint((foot.X + sideStart.X) / 2, (foot.Y + sideStart.Y) / 2);
        return new LocalOutline(points, cutout, new IslandPoint(1, 1));
    }

    private static double NormalizeAngle(double delta)
    {
        var wrapped = (delta + Math.PI) % (2 * Math.PI);
        if (wrapped < 0)
            wrapped += 2 * Math.PI;
        return wrapped - Math.PI;
    }

    private static IslandPoint Shift(IslandPoint point, double x, double y) => new(point.X + x, point.Y + y);

    private static double Clamp(double progress)
    {
        if (!double.IsFinite(progress))
            return 0;
        return Math.Clamp(progress, 0, 1);
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static int Round(double value)
    {
        var rounded = Math.Round(value);
        if (rounded >= int.MaxValue)
            return int.MaxValue;
        if (rounded <= int.MinValue)
            return int.MinValue;
        return (int)rounded;
    }
}
