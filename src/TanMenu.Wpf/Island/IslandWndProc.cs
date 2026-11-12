using System.Runtime.InteropServices;
using Avalonia.Controls;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Island;

/// <summary>
/// Per-pixel hit testing for the fixed preview host. Misses return HTTRANSPARENT and are also cut out
/// with a window region so the transparent margin is not part of the HWND. The host size is not changed.
/// Cross-process click-through still has to be observed on a Windows desktop; this is not that observation.
/// </summary>
internal static partial class IslandWndProc
{
    public static void AddHooks(
        Window window,
        Func<double, double, bool> hitTestDip,
        out Win32Properties.CustomWindowStylesCallback styles,
        out Win32Properties.CustomWndProcHookCallback hook)
    {
        styles = CombineStyles;
        hook = (hWnd, msg, wParam, lParam, ref handled) =>
            HitTest(window, hitTestDip, hWnd, msg, lParam, ref handled);
        Win32Properties.AddWindowStylesCallback(window, styles);
        Win32Properties.AddWndProcHookCallback(window, hook);
    }

    public static void RemoveHooks(
        Window window,
        Win32Properties.CustomWindowStylesCallback? styles,
        Win32Properties.CustomWndProcHookCallback? hook)
    {
        if (styles is not null)
            Win32Properties.RemoveWindowStylesCallback(window, styles);
        if (hook is not null)
            Win32Properties.RemoveWndProcHookCallback(window, hook);
    }

    public static void SetCapsuleRegion(
        IntPtr hwnd,
        double hostWidth,
        double hostHeight,
        double capsuleWidth,
        double capsuleHeight,
        double radius,
        double scale)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return;

        try
        {
            if (scale <= 0 || double.IsNaN(scale))
                scale = 1;
            if (capsuleWidth <= 0 || capsuleHeight <= 0 || double.IsNaN(capsuleWidth) || double.IsNaN(capsuleHeight))
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            var hostW = (int)Math.Round(hostWidth * scale);
            var hostH = (int)Math.Round(hostHeight * scale);
            var capsule = IslandCapsuleGeometry.Layout(hostWidth, hostHeight, capsuleWidth, capsuleHeight, radius);
            var region = IslandCapsuleGeometry.ToExclusiveRoundRect(capsule, scale, hostW, hostH);
            if (region.Right <= region.Left || region.Bottom <= region.Top)
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            var handle = CreateRoundRectRgn(region.Left, region.Top, region.Right, region.Bottom, region.Diameter, region.Diameter);
            if (handle == IntPtr.Zero)
                return;
            if (SetWindowRgn(hwnd, handle, true) == 0)
                DeleteObject(handle);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "更新灵动岛命中区域失败");
        }
    }

    public static void ClearRegion(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return;
        try
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "清除灵动岛命中区域失败");
        }
    }

    private static (uint Style, uint ExStyle) CombineStyles(uint style, uint exStyle) =>
        (style, exStyle | IslandWin32Styles.PreviewExtendedStyle);

    private static IntPtr HitTest(
        Window window,
        Func<double, double, bool> hitTestDip,
        IntPtr hWnd,
        uint msg,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == IslandWin32Styles.WmMouseActivate)
        {
            handled = true;
            return IslandWin32Styles.MaNoActivate;
        }

        if (msg != IslandWin32Styles.WmNcHitTest)
            return IntPtr.Zero;

        if (!OperatingSystem.IsWindows())
        {
            handled = true;
            return IslandWin32Styles.HtTransparent;
        }

        var (screenX, screenY) = IslandCapsuleGeometry.UnpackNcHitTestLParam(lParam);
        var point = new NativePoint { X = screenX, Y = screenY };
        if (!ScreenToClient(hWnd, ref point))
        {
            handled = true;
            return IslandWin32Styles.HtTransparent;
        }

        var scale = window.RenderScaling;
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;

        var inside = false;
        try
        {
            inside = hitTestDip(point.X / scale, point.Y / scale);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "灵动岛命中测试失败");
        }

        if (inside)
        {
            handled = false;
            return IntPtr.Zero;
        }

        handled = true;
        return IslandWin32Styles.HtTransparent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);
}
