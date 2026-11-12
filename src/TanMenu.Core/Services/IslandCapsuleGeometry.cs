namespace TanMenu.Core.Services;

/// <summary>Capsule rectangle inside the fixed host, in DIPs, origin top-left.</summary>
public readonly record struct IslandCapsuleRect(double X, double Y, double Width, double Height, double Radius);

/// <summary>Hit shape shared by the Win32 hit test and the window region. The host size does not change.</summary>
public static class IslandCapsuleGeometry
{
    public static IslandCapsuleRect Layout(double hostWidth, double hostHeight, double capsuleWidth, double capsuleHeight, double radius)
    {
        var width = Math.Max(0, capsuleWidth);
        var height = Math.Max(0, capsuleHeight);
        var x = (hostWidth - width) / 2;
        var y = hostHeight - height;
        var r = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        return new IslandCapsuleRect(x, y, width, height, r);
    }

    public static bool Contains(IslandCapsuleRect capsule, double x, double y)
    {
        if (capsule.Width <= 0 || capsule.Height <= 0)
            return false;
        if (x < capsule.X || y < capsule.Y || x > capsule.X + capsule.Width || y > capsule.Y + capsule.Height)
            return false;

        var r = capsule.Radius;
        if (r <= 0)
            return true;

        var right = capsule.X + capsule.Width;
        var bottom = capsule.Y + capsule.Height;
        if (x < capsule.X + r && y < capsule.Y + r)
            return DistanceSquared(x, y, capsule.X + r, capsule.Y + r) <= r * r;
        if (x > right - r && y < capsule.Y + r)
            return DistanceSquared(x, y, right - r, capsule.Y + r) <= r * r;
        if (x < capsule.X + r && y > bottom - r)
            return DistanceSquared(x, y, capsule.X + r, bottom - r) <= r * r;
        if (x > right - r && y > bottom - r)
            return DistanceSquared(x, y, right - r, bottom - r) <= r * r;
        return true;
    }

    public static (int Left, int Top, int Right, int Bottom, int Diameter) ToExclusiveRoundRect(
        IslandCapsuleRect capsule, double scale, int hostWidthPx, int hostHeightPx)
    {
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;

        var left = Round(capsule.X * scale);
        var top = Round(capsule.Y * scale);
        var right = Round((capsule.X + capsule.Width) * scale);
        var bottom = Round((capsule.Y + capsule.Height) * scale);
        var diameter = Math.Max(0, Round(capsule.Radius * 2 * scale));
        var maxW = Math.Max(0, hostWidthPx);
        var maxH = Math.Max(0, hostHeightPx);
        left = Math.Clamp(left, 0, maxW);
        top = Math.Clamp(top, 0, maxH);
        right = Math.Clamp(right, left, maxW);
        bottom = Math.Clamp(bottom, top, maxH);
        return (left, top, right, bottom, diameter);
    }

    /// <summary>Low 16 bits are x and the next 16 are y, both signed. This is the WM_NCHITTEST lParam layout.</summary>
    public static (int X, int Y) UnpackNcHitTestLParam(nint lParam)
    {
        var value = (long)lParam;
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return (x, y);
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

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
