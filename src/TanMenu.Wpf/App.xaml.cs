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

        // One-time move of pre-existing %LOCALAPPDATA%\TanMenu data to the new Documents default.
        DataLocation.MigrateLegacyIfNeeded();

        var local = DataLocation.GetDataRoot(); // default Documents\TanMenu, or a user-chosen folder
        // Mutable so a data-folder change in settings can re-point it live (no restart).
        var paths = new MutableAppDataPaths(local);
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
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<AppEvents>();
        services.AddSingleton<ISettingsLauncher, WpfSettingsLauncher>();
        services.AddSingleton<IShellCommands, WpfShellCommands>();

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

        // Fail fast with a clear message if the WebView2 Evergreen runtime is missing — otherwise a
        // clean machine just shows a blank transparent window with no explanation.
        try
        {
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 runtime not found");
            MessageBox.Show(
                "运行 TanMenu 需要 Microsoft Edge WebView2 运行时。\n\n请从下面的地址下载安装后重试：\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "缺少 WebView2 运行时", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Warn the user if a global-hotkey (re)bind fails (occupied/invalid) instead of only logging.
        Services.GetRequiredService<GlobalHotkeyService>().RegistrationFailed += combo =>
            MessageBox.Show($"全局热键「{combo}」注册失败，可能已被其它程序占用。\n请在设置里改用其它组合键。",
                "TanMenu", MessageBoxButton.OK, MessageBoxImage.Warning);

        new MainWindow().Show();
    }

    public void ExitApp()
    {
        Tray?.Dispose();
        (Services as IDisposable)?.Dispose(); // disposes singletons (unregisters the global hotkey, etc.)
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        Shutdown();
    }
}
