namespace TanMenu.Core.Services;

/// <summary>Capsule rectangle inside the fixed host, in DIPs, origin top-left.</summary>
public readonly record struct IslandCapsuleRect(double X, double Y, double Width, double Height, double Radius);

/// <summary>Exclusive device-pixel round region. Right and bottom are excluded, matching CreateRoundRectRgn.</summary>
public readonly record struct IslandDeviceRoundRegion(int Left, int Top, int Right, int Bottom, int Diameter);

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
            return false;

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
        if (!TryGetDeviceRoundRegion(capsule, scale, hostWidthPx, hostHeightPx, out var region))
            return default;
        return (region.Left, region.Top, region.Right, region.Bottom, region.Diameter);
    }

    /// <summary>
    /// Builds a capsule-shaped device region that contains the DIP capsule. A zero radius, a
    /// non-capsule corner, or an empty rect is rejected so the caller keeps the previous region
    /// instead of installing a rectangle or clearing the window region.
    /// </summary>
    public static bool TryGetDeviceRoundRegion(
        IslandCapsuleRect capsule,
        double scale,
        int hostWidthPx,
        int hostHeightPx,
        out IslandDeviceRoundRegion region)
    {
        region = default;
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;
        if (capsule.Width <= 0 || capsule.Height <= 0 || capsule.Radius <= 0)
            return false;
        if (Math.Abs(capsule.Radius * 2 - capsule.Height) > 0.51)
            return false;

        var left = Floor(capsule.X * scale);
        var top = Floor(capsule.Y * scale);
        var right = Ceil((capsule.X + capsule.Width) * scale);
        var bottom = Ceil((capsule.Y + capsule.Height) * scale);
        var diameter = Ceil(capsule.Radius * 2 * scale);
        var maxW = Math.Max(0, hostWidthPx);
        var maxH = Math.Max(0, hostHeightPx);
        left = Math.Clamp(left, 0, maxW);
        top = Math.Clamp(top, 0, maxH);
        right = Math.Clamp(right, left, maxW);
        bottom = Math.Clamp(bottom, top, maxH);
        if (right - left < 2 || bottom - top < 2 || diameter < 2)
            return false;
        if (diameter + 1 < bottom - top)
            return false;

        region = new IslandDeviceRoundRegion(left, top, right, bottom, diameter);
        return true;
    }

    /// <summary>True when a device pixel lies inside the round region, including its curved ends.</summary>
    public static bool DeviceRoundRegionContains(IslandDeviceRoundRegion region, int x, int y)
    {
        if (region.Diameter < 2 || x < region.Left || y < region.Top || x >= region.Right || y >= region.Bottom)
            return false;

        var radius = region.Diameter / 2.0;
        var right = region.Right;
        var bottom = region.Bottom;
        if (x < region.Left + radius && y < region.Top + radius)
            return DistanceSquared(x + 0.5, y + 0.5, region.Left + radius, region.Top + radius) <= radius * radius;
        if (x >= right - radius && y < region.Top + radius)
            return DistanceSquared(x + 0.5, y + 0.5, right - radius, region.Top + radius) <= radius * radius;
        if (x < region.Left + radius && y >= bottom - radius)
            return DistanceSquared(x + 0.5, y + 0.5, region.Left + radius, bottom - radius) <= radius * radius;
        if (x >= right - radius && y >= bottom - radius)
            return DistanceSquared(x + 0.5, y + 0.5, right - radius, bottom - radius) <= radius * radius;
        return true;
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

    private static int Floor(double value)
    {
        var floored = Math.Floor(value);
        if (floored <= int.MinValue)
            return int.MinValue;
        if (floored >= int.MaxValue)
            return int.MaxValue;
        return (int)floored;
    }

    private static int Ceil(double value)
    {
        var ceiled = Math.Ceiling(value);
        if (ceiled >= int.MaxValue)
            return int.MaxValue;
        if (ceiled <= int.MinValue)
            return int.MinValue;
        return (int)ceiled;
    }
}
