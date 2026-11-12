using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Island;

/// <summary>
/// Reads taskbar auto-hide and thickness through SHAppBarMessage. Any failure, including running
/// where shell32 is absent, stays unknown so placement uses the conservative reserve. This is not a
/// Windows observation of auto-hide on or off.
/// </summary>
internal static partial class Win32TaskbarQuery
{
    private const uint AbmGetState = 0x00000004;
    private const uint AbmGetTaskbarPos = 0x00000005;

    public static (bool? AutoHide, double? ThicknessDip) Query(double scale)
    {
        if (!OperatingSystem.IsWindows())
            return (null, null);

        try
        {
            var stateData = new AppBarData { cbSize = (uint)Unsafe.SizeOf<AppBarData>() };
            var state = SHAppBarMessage(AbmGetState, ref stateData);

            var position = stateData;
            var positionResult = SHAppBarMessage(AbmGetTaskbarPos, ref position);
            return IslandTaskbarObservation.Interpret(
                querySucceeded: true,
                state: (uint)state,
                edge: (int)position.uEdge,
                left: position.rcLeft,
                top: position.rcTop,
                right: position.rcRight,
                bottom: position.rcBottom,
                scale: scale,
                positionKnown: positionResult != 0);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "读取任务栏状态失败");
            return (null, null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public int rcLeft;
        public int rcTop;
        public int rcRight;
        public int rcBottom;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    private static partial nuint SHAppBarMessage(uint dwMessage, ref AppBarData pData);
}
