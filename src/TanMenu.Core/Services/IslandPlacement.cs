namespace TanMenu.Core.Services;

/// <summary>Primary-screen inputs for <see cref="IslandPlacement"/>. Rectangles are DIPs.</summary>
public sealed class IslandScreenMetrics
{
    public required PlacementRect Bounds { get; init; }
    public required PlacementRect WorkArea { get; init; }

    /// <summary>Null means the auto-hide state could not be read. That is not the same as off.</summary>
    public bool? AutoHide { get; init; }

    /// <summary>Taskbar thickness in DIPs when it was actually read. Null uses the fallback while auto-hide is on or unknown.</summary>
    public double? TaskbarThicknessDip { get; init; }
}

/// <summary>
/// Places the fixed preview host on the primary screen. Auto-hide and non-bottom taskbars follow the
/// reviewed conservative rules; those Windows layouts have not been observed by this implementation.
/// </summary>
public static class IslandPlacement
{
    public static PlacementRect? ResolveWindow(IslandScreenMetrics screen)
    {
        var width = IslandMetrics.ExpandedWidth;
        var height = IslandMetrics.ExpandedHeight;
        var bounds = screen.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
            return null;

        var work = screen.WorkArea;
        var autoHide = screen.AutoHide;
        var thickness = screen.TaskbarThicknessDip;
        if (work.Width <= 0 || work.Height <= 0 || double.IsNaN(work.Width) || double.IsNaN(work.Height))
        {
            // Work area unread: use the screen bottom and the unknown-reserve rule.
            work = bounds;
            autoHide = null;
            thickness = null;
        }

        double anchorBottom;
        if (autoHide == false)
        {
            anchorBottom = work.Bottom;
        }
        else if (autoHide == true)
        {
            if (thickness is not > 0)
                return null;
            anchorBottom = bounds.Bottom - thickness.Value;
        }
        else
        {
            var inset = bounds.Bottom - work.Bottom;
            if (inset <= 0)
                return null;
            anchorBottom = work.Bottom;
        }

        var left = work.Left + (work.Width - width) / 2;
        return new PlacementRect(left, anchorBottom - height, width, height);
    }

    public static IslandScreenMetrics FromDevicePixels(
        double boundsX, double boundsY, double boundsWidth, double boundsHeight,
        double workX, double workY, double workWidth, double workHeight,
        double scale,
        bool? autoHide,
        double? taskbarThicknessDip)
    {
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;

        return new IslandScreenMetrics
        {
            Bounds = new PlacementRect(boundsX / scale, boundsY / scale, boundsWidth / scale, boundsHeight / scale),
            WorkArea = new PlacementRect(workX / scale, workY / scale, workWidth / scale, workHeight / scale),
            AutoHide = autoHide,
            TaskbarThicknessDip = taskbarThicknessDip,
        };
    }

    /// <summary>Same rounding <c>WindowHost</c> uses when it turns a DIP position into a <c>PixelPoint</c>.</summary>
    public static (int X, int Y) ToDevicePosition(PlacementRect dip, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;
        return (RoundDevice(dip.Left * scale), RoundDevice(dip.Top * scale));
    }

    private static int RoundDevice(double value)
    {
        var rounded = Math.Round(value);
        if (rounded >= int.MaxValue)
            return int.MaxValue;
        if (rounded <= int.MinValue)
            return int.MinValue;
        return (int)rounded;
    }
}
