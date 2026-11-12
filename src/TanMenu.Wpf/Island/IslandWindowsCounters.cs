using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Island;

internal static partial class IslandWindowsCounters
{
    public static IslandRawSample Read()
    {
        if (!OperatingSystem.IsWindows())
            return default;

        var cpuOk = GetSystemTimes(out var idle, out var kernel, out var user);
        var memoryOk = false;
        var used = 0d;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
        {
            memoryOk = true;
            used = 1d - status.AvailPhys / (double)status.TotalPhys;
        }

        var networkOk = false;
        ulong bytesIn = 0;
        ulong bytesOut = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                var stats = nic.GetIPStatistics();
                bytesIn += (ulong)Math.Max(0, stats.BytesReceived);
                bytesOut += (ulong)Math.Max(0, stats.BytesSent);
            }

            networkOk = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "读取网卡计数失败");
        }

        return new IslandRawSample(cpuOk, (ulong)idle, (ulong)kernel, (ulong)user, memoryOk, used, networkOk, bytesIn, bytesOut);
    }

    public static bool ReducedMotion()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var enabled = 1;
        return SystemParametersInfoW(0x1042, 0, ref enabled, 0) && enabled == 0;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, ref int value, uint ini);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
