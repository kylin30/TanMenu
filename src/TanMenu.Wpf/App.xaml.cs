using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static TrayService? Tray { get; set; }
    public static HotkeyService? Hotkey { get; set; }

    /// <summary>Custom message a second instance posts to resurface the first.</summary>
    public const int WmShowFirstInstance = 0x8000 + 1;

    private Mutex? _mutex;

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: if already running, signal the existing window and exit.
        // Use a WPF-app-specific name so we don't collide with the original WinForms TanMenu
        // (which uses the mutex/window name "TanMenu").
        _mutex = new Mutex(true, "TanMenuWpf", out var createdNew);
        if (!createdNew)
        {
            var existing = FindWindow(null, "TanMenuWpf");
            if (existing != IntPtr.Zero)
                SendMessage(existing, WmShowFirstInstance, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TanMenu");
        var cache = Path.Combine(local, "cache");
        var paths = new AppDataPaths(local, cache); // 2-arg ctor = unpackaged, no WinRT
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(paths.LogsFolder, "log-.txt"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled dispatcher exception");
            args.Handled = true;
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));
        services.AddSingleton<IAppDataPaths>(paths);

        // Core services
        services.AddSingleton<ConfigService>();
        services.AddSingleton<IShortcutResolver, ShortcutResolver>();
        services.AddSingleton<MenuDataService>();
        services.AddSingleton<IIconProvider, IconProvider>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<SoundService>();
        // App services
        services.AddSingleton<MenuService>();
        services.AddSingleton<WindowHost>();
        services.AddSingleton<IWindowHost>(sp => sp.GetRequiredService<WindowHost>());
        services.AddSingleton<IAutoStartService, RegistryAutoStartService>();
        services.AddSingleton<IFolderPicker, WpfFolderPicker>();

        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        Services = services.BuildServiceProvider();

        // Load config + initialize sounds before the UI renders.
        // NOTE: must await (not block) — blocking on the WPF UI thread during OnStartup
        // deadlocks because the continuation posts to a dispatcher that isn't pumping yet.
        await Services.GetRequiredService<ConfigService>().LoadAsync();
        Services.GetRequiredService<SoundService>()
            .Initialize(Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds"));

        new MainWindow().Show();
    }

    public void ExitApp()
    {
        Tray?.Dispose();
        Hotkey?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        Shutdown();
    }
}
