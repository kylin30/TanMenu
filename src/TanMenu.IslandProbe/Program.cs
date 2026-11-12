using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TanMenu.Wpf.Island;

namespace TanMenu.IslandProbe;

/// <summary>
/// Discovers the hosted Windows session, then drives the real preview window with SendInput
/// against a second process. A missing input desktop fails the process. This is not a Windows 11
/// taskbar or multi-monitor acceptance test.
/// </summary>
public static partial class Program
{
    private const int SessionBlocked = 2;
    private const int SendInputFailed = 3;
    private const int OverlayMissing = 4;
    private const int HitTestFailed = 5;
    private const int BlackScreenshot = 6;
    private const int HoverFailed = 7;

    [STAThread]
    public static int Main(string[] args)
    {
        if (Has(args, "--underlay"))
            return RunUnderlay(args);

        var outDir = ValueAfter(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "probe-out");
        Directory.CreateDirectory(outDir);
        var report = Discover();
        File.WriteAllText(Path.Combine(outDir, "session.json"), JsonSerializer.Serialize(report, JsonOptions()));
        if (report.Blocked)
        {
            Console.Error.WriteLine("overlay probe blocked: " + report.BlockReason);
            return SessionBlocked;
        }

        return RunInteractive(outDir, report);
    }

    private static int RunInteractive(string outDir, SessionReport report)
    {
        var underlayDir = Path.Combine(outDir, "underlay");
        Directory.CreateDirectory(underlayDir);
        var ready = Path.Combine(underlayDir, "ready.txt");
        var clicks = Path.Combine(underlayDir, "clicks.txt");
        File.WriteAllText(clicks, "0");
        const int x = 120;
        const int y = 80;
        const int width = 900;
        const int height = 640;
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self))
        {
            Console.Error.WriteLine("overlay probe has no process path");
            return OverlayMissing;
        }

        using var underlay = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(self)
        {
            ArgumentList = { "--underlay", x.ToString(), y.ToString(), width.ToString(), height.ToString(), ready, clicks },
            UseShellExecute = false,
        });
        if (underlay is null || !WaitForFile(ready, TimeSpan.FromSeconds(8)))
        {
            Console.Error.WriteLine("underlay process did not create a window");
            try { underlay?.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return OverlayMissing;
        }

        var code = OverlayMissing;
        try
        {
            ProbeRun.OutDir = outDir;
            ProbeRun.UnderlayX = x;
            ProbeRun.UnderlayY = y;
            ProbeRun.UnderlayWidth = width;
            ProbeRun.UnderlayHeight = height;
            ProbeRun.ClicksPath = clicks;
            ProbeRun.UnderlayPid = underlay.Id;
            code = BuildApp().StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            try
            {
                if (!underlay.HasExited)
                    underlay.Kill(entireProcessTree: true);
            }
            catch
            {
                /* underlay already exited */
            }
        }

        report.ProbeExitCode = code;
        File.WriteAllText(Path.Combine(outDir, "session.json"), JsonSerializer.Serialize(report, JsonOptions()));
        return code;
    }

    private static AppBuilder BuildApp() =>
        AppBuilder.Configure<ProbeApplication>()
            .UsePlatformDetect()
            .LogToTrace();

    private static int Exercise(IslandPreviewWindow window, string outDir, string clicksPath, int underlayPid)
    {
        var placed = Dispatcher.UIThread.Invoke(() => (window.Position.X, window.Position.Y, window.TryGetPlatformHandle()?.Handle ?? 0));
        if (placed.Item3 == 0)
        {
            Console.Error.WriteLine("preview window has no HWND");
            return OverlayMissing;
        }

        var transparent = (placed.Item1 + 24, placed.Item2 + 8);
        var solid = (placed.Item1 + 160, placed.Item2 + 56);
        var outside = (placed.Item1 - 40, placed.Item2 - 40);
        var initialWidth = Dispatcher.UIThread.Invoke(() => window.PreviewCapsuleWidth);
        var beforeForeground = GetForegroundWindow();
        if (!MoveAndClick(transparent.Item1, transparent.Item2))
        {
            Console.Error.WriteLine("SendInput failed on the transparent pixel");
            return SendInputFailed;
        }

        Thread.Sleep(250);
        var transparentClicks = ReadCount(clicksPath);
        SaveShot(outDir, "collapsed.png", placed.Item1, placed.Item2, 320, 72, out var collapsedMean);
        if (!Move(solid.Item1, solid.Item2))
            return SendInputFailed;
        Thread.Sleep(500);
        var expandedWidth = Dispatcher.UIThread.Invoke(() => window.PreviewCapsuleWidth);
        SaveShot(outDir, "expanded.png", placed.Item1, placed.Item2, 320, 72, out var expandedMean);
        var clicksBeforeSolid = ReadCount(clicksPath);
        if (!MoveAndClick(solid.Item1, solid.Item2))
            return SendInputFailed;
        Thread.Sleep(200);
        var solidClicks = ReadCount(clicksPath);
        if (!Move(outside.Item1, outside.Item2))
            return SendInputFailed;
        Thread.Sleep(450);
        var retractedWidth = Dispatcher.UIThread.Invoke(() => window.PreviewCapsuleWidth);
        var afterForeground = GetForegroundWindow();
        var foregroundPid = 0;
        _ = GetWindowThreadProcessId(afterForeground, out foregroundPid);
        var pointOwner = WindowFromPoint(new NativePoint { X = transparent.Item1, Y = transparent.Item2 });

        var outcome = new
        {
            hwnd = placed.Item3.ToInt64(),
            transparent,
            solid,
            transparentClicks,
            solidClicksAdded = solidClicks - clicksBeforeSolid,
            initialWidth,
            expandedWidth,
            retractedWidth,
            collapsedMean,
            expandedMean,
            beforeForeground = beforeForeground.ToInt64(),
            afterForeground = afterForeground.ToInt64(),
            foregroundPid,
            underlayPid,
            pointOwner = pointOwner.ToInt64(),
            os = ReadProductName(),
        };
        File.WriteAllText(Path.Combine(outDir, "input.json"), JsonSerializer.Serialize(outcome, JsonOptions()));

        if (collapsedMean < 8 || expandedMean < 8)
        {
            Console.Error.WriteLine("screenshot is black; the desktop was not captured");
            return BlackScreenshot;
        }

        if (transparentClicks < 1)
        {
            Console.Error.WriteLine("transparent pixel did not reach the underlying process");
            return HitTestFailed;
        }

        if (solidClicks - clicksBeforeSolid > 0)
        {
            Console.Error.WriteLine("solid capsule click reached the underlying process");
            return HitTestFailed;
        }

        if (expandedWidth < 200 || retractedWidth > 180)
        {
            Console.Error.WriteLine($"hover did not expand and retract ({expandedWidth} -> {retractedWidth})");
            return HoverFailed;
        }

        if (foregroundPid == 0 || foregroundPid == Environment.ProcessId)
        {
            Console.Error.WriteLine("foreground did not stay with the underlying process");
            return HitTestFailed;
        }

        Console.WriteLine("overlay probe passed on this Windows session");
        return 0;
    }

    private static void SaveShot(string outDir, string name, int x, int y, int width, int height, out double mean)
    {
        mean = 0;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
            bitmap.Save(Path.Combine(outDir, name), System.Drawing.Imaging.ImageFormat.Png);
            long sum = 0;
            var samples = 0;
            for (var py = 0; py < height; py += 4)
            {
                for (var px = 0; px < width; px += 4)
                {
                    var color = bitmap.GetPixel(px, py);
                    sum += color.R + color.G + color.B;
                    samples++;
                }
            }

            mean = samples == 0 ? 0 : sum / (samples * 3d);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("screenshot failed: " + ex.Message);
            mean = 0;
        }
    }

    private static SessionReport Discover()
    {
        var session = 0u;
        ProcessIdToSessionId((uint)Environment.ProcessId, ref session);
        var input = OpenInputDesktop(0, false, 0x0100);
        var inputOk = input != 0;
        if (inputOk)
            CloseDesktop(input);
        var product = ReadProductName();
        var tray = FindWindowW("Shell_TrayWnd", null);
        var reason = "";
        if (session == 0)
            reason = "process is in session 0";
        else if (!inputOk)
            reason = "OpenInputDesktop failed";
        else if (GetSystemMetrics(0) <= 0 || GetSystemMetrics(1) <= 0)
            reason = "no screen size";

        return new SessionReport
        {
            SessionId = session,
            InputDesktopOpen = inputOk,
            ProductName = product,
            ScreenWidth = GetSystemMetrics(0),
            ScreenHeight = GetSystemMetrics(1),
            MonitorCount = GetSystemMetrics(80),
            Dpi = GetDpiForSystem(),
            TrayWindow = tray.ToInt64(),
            WorkArea = ReadWorkArea(),
            Blocked = reason.Length > 0,
            BlockReason = reason,
            TaskbarClaim = "not-tested-as-windows-11",
        };
    }

    private static string ReadWorkArea()
    {
        var rect = new RectNative();
        return SystemParametersInfoW(0x0030, 0, ref rect, 0)
            ? $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}"
            : "";
    }

    private static string ReadProductName()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName",
                "") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int RunUnderlay(string[] args)
    {
        var x = int.Parse(args[1]);
        var y = int.Parse(args[2]);
        var width = int.Parse(args[3]);
        var height = int.Parse(args[4]);
        var ready = args[5];
        var clicks = args[6];
        var count = 0;
        var className = "TanMenuIslandUnderlay";
        var wnd = new WndProc((hWnd, msg, wParam, lParam) =>
        {
            if (msg == 0x0201)
            {
                count++;
                File.WriteAllText(clicks, count.ToString());
            }

            if (msg == 0x0002)
                PostQuitMessage(0);
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        });
        var classNamePtr = Marshal.StringToHGlobalUni(className);
        var cls = new WndClassW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wnd),
            lpszClassName = classNamePtr,
            hbrBackground = (IntPtr)6,
        };
        if (RegisterClassW(ref cls) == 0)
        {
            Marshal.FreeHGlobal(classNamePtr);
            return 1;
        }
        var hwnd = CreateWindowExW(0, className, "TanMenuIslandUnderlay", 0x00CF0000,
            x, y, width, height, 0, 0, GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return 1;
        ShowWindow(hwnd, 5);
        SetForegroundWindow(hwnd);
        File.WriteAllText(ready, hwnd.ToString());
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        GC.KeepAlive(wnd);
        Marshal.FreeHGlobal(classNamePtr);
        return 0;
    }

    private static bool MoveAndClick(int x, int y) => Move(x, y) && Click();

    private static bool Move(int x, int y)
    {
        var vx = GetSystemMetrics(76);
        var vy = GetSystemMetrics(77);
        var vw = Math.Max(1, GetSystemMetrics(78));
        var vh = Math.Max(1, GetSystemMetrics(79));
        var ax = (int)Math.Round((x - vx) * 65535d / (vw - 1));
        var ay = (int)Math.Round((y - vy) * 65535d / (vh - 1));
        var input = new Input { Type = 0, Mi = new MouseInput { Dx = ax, Dy = ay, DwFlags = 0x8001 | 0x4000 } };
        return SendInput(1, ref input, Marshal.SizeOf<Input>()) == 1;
    }

    private static bool Click()
    {
        var down = new Input { Type = 0, Mi = new MouseInput { DwFlags = 0x0002 } };
        var up = new Input { Type = 0, Mi = new MouseInput { DwFlags = 0x0004 } };
        return SendInput(1, ref down, Marshal.SizeOf<Input>()) == 1
            && SendInput(1, ref up, Marshal.SizeOf<Input>()) == 1;
    }

    private static int ReadCount(string path)
    {
        try
        {
            return int.Parse(File.ReadAllText(path).Trim());
        }
        catch
        {
            return 0;
        }
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (File.Exists(path))
                return true;
            Thread.Sleep(50);
        }

        return false;
    }

    private static bool Has(string[] args, string value) =>
        args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

    private static string? ValueAfter(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static class ProbeRun
    {
        public static string OutDir = "";
        public static int UnderlayX;
        public static int UnderlayY;
        public static int UnderlayWidth;
        public static int UnderlayHeight;
        public static string ClicksPath = "";
        public static int UnderlayPid;
    }

    private sealed class ProbeApplication : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var islandX = ProbeRun.UnderlayX + (ProbeRun.UnderlayWidth - 320) / 2;
                var islandY = ProbeRun.UnderlayY + ProbeRun.UnderlayHeight - 72 - 24;
                var window = new IslandPreviewWindow { Position = new PixelPoint(islandX, islandY) };
                window.Opened += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    var code = Exercise(window, ProbeRun.OutDir, ProbeRun.ClicksPath, ProbeRun.UnderlayPid);
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(code));
                });
                window.AttachPlatformHooks();
                if (!window.TryApplyHitRegion())
                {
                    Console.Error.WriteLine("preview region was not installed; overlay will not be shown");
                    desktop.Shutdown(OverlayMissing);
                    return;
                }

                desktop.MainWindow = window;
                window.Show();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class SessionReport
    {
        public uint SessionId { get; set; }
        public bool InputDesktopOpen { get; set; }
        public string ProductName { get; set; } = "";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public int MonitorCount { get; set; }
        public uint Dpi { get; set; }
        public long TrayWindow { get; set; }
        public string WorkArea { get; set; } = "";
        public bool Blocked { get; set; }
        public string BlockReason { get; set; } = "";
        public string TaskbarClaim { get; set; } = "";
        public int ProbeExitCode { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassW
    {
        public uint Style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mi;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint dwProcessId, ref uint pSessionId);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(IntPtr hDesktop);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref RectNative pvParam, uint fWinIni);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ushort RegisterClassW(ref WndClassW lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Msg lpMsg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(ref Msg lpMsg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint nInputs, ref Input pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(NativePoint point);
}
