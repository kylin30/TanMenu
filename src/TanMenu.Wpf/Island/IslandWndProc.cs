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

    /// <summary>
    /// Installs the concave polygon. On success the system owns the GDI object.
    /// On failure the object is deleted here. This never installs a null region.
    /// </summary>
    public static bool TryApplyDeviceRegion(IntPtr hwnd, IslandDevicePoint[] region)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero || region.Length < 3)
            return false;

        var bytes = region.Length * Marshal.SizeOf<NativePoint>();
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            for (var i = 0; i < region.Length; i++)
            {
                Marshal.StructureToPtr(new NativePoint { X = region[i].X, Y = region[i].Y }, buffer + i * Marshal.SizeOf<NativePoint>(), false);
            }

            var handle = CreatePolygonRgn(buffer, region.Length, 2);
            if (handle == IntPtr.Zero)
            {
                Serilog.Log.Error("CreatePolygonRgn 失败，Win32 错误 {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            if (SetWindowRgn(hwnd, handle, true) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                DeleteObject(handle);
                Serilog.Log.Error("SetWindowRgn 失败，Win32 错误 {Error}", error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "更新灵动岛命中区域失败");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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

        var (screenX, screenY) = UnpackNcHitTest(lParam);
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

    private static (int X, int Y) UnpackNcHitTest(nint lParam)
    {
        var value = (long)lParam;
        return (unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreatePolygonRgn(IntPtr points, int count, int mode);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);
}
