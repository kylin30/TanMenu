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
using Velopack;
using Velopack.Locators;

namespace TanMenu.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static TrayService? Tray { get; set; }
    public static bool IsPortable { get; private set; }
    public static string PortableDataRoot { get; private set; } = AppContext.BaseDirectory;

    /// <summary>Custom message a second instance posts to resurface the first.</summary>
    public const int WmShowFirstInstance = 0x8000 + 1;

    private Mutex? _mutex;
    private bool _taskbarPinPromptActive;

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack command-line hooks must run before WPF, DI, logging, or any application files
        // are opened. Microsoft Store/MSIX builds remain entirely Store-managed.
        if (!PackageRuntime.HasPackageIdentity)
        {
            VelopackApp.Build()
                .SetArgs(args)
                .SetAutoApplyOnStartup(false)
                .Run();

            // Give the unpackaged process the same stable identity as its Start-menu shortcut.
            // This must happen before WPF creates a window so Explorer groups the running app with
            // the permanent taskbar pin instead of producing a second, transient icon.
            TaskbarPinService.InitializeProcessIdentity();
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Last-resort safety nets, wired FIRST so no later startup step can fault silently. The
        // dispatcher handler catches UI-thread throws; the other two catch background-thread and
        // fire-and-forget (discarded-Task) faults that DispatcherUnhandledException never sees.
        DispatcherUnhandledException += (_, args) =>
        {
            TryLogFatal(args.Exception, "Unhandled dispatcher exception");
            args.Handled = true; // keep the app alive through a transient runtime UI glitch
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TryLogFatal(args.ExceptionObject as Exception, "AppDomain unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TryLogFatal(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

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

        // All remaining init is fragile (disk, registry, DI, native runtimes). Any unhandled failure
        // must surface a clear message and exit CLEANLY (release mutex, dispose services) — never a
        // silent pre-UI crash, and never a window-less/tray-less zombie that still holds the mutex.
        try
        {
            await StartupAsync();
        }
        catch (Exception ex)
        {
            TryLogFatal(ex, "Startup failed");
            MessageBox.Show(AppLanguage.Format("StartupFailed", null, ex.Message), "TanMenu",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApp();
        }
    }

    /// <summary>The fragile part of startup (data paths, logging, DI, config, native runtimes, the
    /// main window). Wrapped by <see cref="OnStartup"/>'s try/catch so any failure becomes a message +
    /// clean exit rather than a silent crash.</summary>
    private async Task StartupAsync()
    {
        IsPortable = !PackageRuntime.HasPackageIdentity &&
                     (PortableRuntime.IsEnabled() || VelopackLocator.Current.IsPortable);
        PortableDataRoot = IsPortable && VelopackLocator.Current.IsPortable
            ? VelopackLocator.Current.RootAppDir ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;

        if (IsPortable && VelopackLocator.Current.IsPortable)
            PortableRuntime.MigrateLegacyDataIfNeeded(AppContext.BaseDirectory, PortableDataRoot);

        // Unpackaged builds keep the user-relocatable Documents\TanMenu data root. Packaged/MSIX
        // builds use package-local ApplicationData; portable builds use a stable root outside the
        // replaceable Velopack content directory.
        if (!PackageRuntime.HasPackageIdentity && !IsPortable)
            DataLocation.MigrateLegacyIfNeeded();

        // Mutable so a data-folder change in settings can re-point it live (no restart). Falls back to
        // %LOCALAPPDATA% if the resolved root can't be created; logging degrades to console-only if its
        // folder isn't writable — so neither a bad data folder nor a locked log can crash startup.
        var paths = CreateDataPaths();
        ConfigureLogging(paths);

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
        services.AddSingleton<IAutoStartService>(_ =>
            IsPortable
                ? new PortableAutoStartService()
                : PackageRuntime.HasPackageIdentity
                ? new StartupTaskAutoStartService()
                : new RegistryAutoStartService());
        services.AddSingleton(_ => new TaskbarPinService(
            PackageRuntime.HasPackageIdentity,
            TaskbarPinService.ResolveLauncherPath));
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<AppEvents>();
        services.AddSingleton<ISettingsLauncher, WpfSettingsLauncher>();
        services.AddSingleton<IShellCommands, WpfShellCommands>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IAppUpdateService>(_ =>
            PackageRuntime.HasPackageIdentity
                ? new FixedAppUpdateService(AppUpdateStatus.StoreManaged)
                : IsPortable && VelopackLocator.Current.IsPortable
                    ? new VelopackAppUpdateService()
                    : new FixedAppUpdateService(AppUpdateStatus.Disabled));

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
        // Create the user-themes folder + seed README/template on first run so the feature is
        // discoverable (the settings 主题文件夹 button opens it; user .css files appear in the dropdown).
        Services.GetRequiredService<ThemeService>().EnsureSeeded();

        // Fail fast with a clear message if the WebView2 Evergreen runtime is missing — otherwise a
        // clean machine just shows a blank transparent window with no explanation. Probe OFF the UI
        // thread: GetAvailableBrowserVersionString is a synchronous shell/registry/filesystem lookup
        // that can take tens-to-hundreds of ms on a cold or AV-scanned disk, and running it inline
        // would freeze the dispatcher right before the main window is created.
        bool webView2Ok;
        try
        {
            var version = await Task.Run(() =>
                Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
            webView2Ok = !string.IsNullOrEmpty(version);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 runtime not found");
            webView2Ok = false;
        }
        if (!webView2Ok)
        {
            var language = Services.GetRequiredService<ConfigService>().Config.General.Language;
            MessageBox.Show(
                AppLanguage.Text("WebView2Missing", language),
                AppLanguage.Text("WebView2Title", language), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Warn the user if a global-hotkey (re)bind fails (occupied/invalid) instead of only logging.
        // Marshal onto the dispatcher asynchronously: the first Apply() runs synchronously inside
        // MainWindow.OnSourceInitialized, and a modal MessageBox there would pump a nested message loop
        // mid-window-init (before the tray is created). BeginInvoke defers it until init finishes.
        Services.GetRequiredService<GlobalHotkeyService>().RegistrationFailed += combo =>
            Dispatcher.BeginInvoke(() =>
                MessageBox.Show(AppLanguage.Format("HotkeyRegistrationFailed",
                        Services.GetRequiredService<ConfigService>().Config.General.Language, combo),
                    "TanMenu", MessageBoxButton.OK, MessageBoxImage.Warning));

        // Taskbar recall is the primary product workflow. Check after EVERY reveal (startup, tray,
        // hotkey, or second-instance activation), once the real window is foreground; TaskbarManager
        // rejects requests from background UI.
        Services.GetRequiredService<AppEvents>().LauncherShown += PromptForTaskbarPinAfterReveal;
        new MainWindow().Show();
        _ = CheckForUpdatesAfterStartupAsync();
    }

    private void PromptForTaskbarPinAfterReveal()
    {
        if (_taskbarPinPromptActive)
            return;

        _taskbarPinPromptActive = true;
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await PromptForTaskbarPinAsync();
            }
            finally
            {
                // A declined/failed request is checked again the next time the launcher is shown.
                // This guard only prevents overlapping prompts during one reveal operation.
                _taskbarPinPromptActive = false;
            }
        });
    }

    private async Task PromptForTaskbarPinAsync()
    {
        var pin = Services.GetRequiredService<TaskbarPinService>();
        var state = await pin.GetStateAsync();
        if (state.Status == TaskbarPinStatus.Pinned)
            return;

        var config = Services.GetRequiredService<ConfigService>();
        var language = config.Config.General.Language;
        var host = Services.GetRequiredService<IWindowHost>();
        var owner = Windows.OfType<MainWindow>().FirstOrDefault();

        // The owned prompt and the following Windows confirmation temporarily move focus away from
        // the popup. Keep it alive until the whole pin flow completes, then restore normal blur-hide.
        host.SuppressHide(true);
        try
        {
            var choice = owner is null
                ? MessageBox.Show(
                    AppLanguage.Text("TaskbarPinRequiredPrompt", language),
                    AppLanguage.Text("TaskbarPinRequiredTitle", language),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information)
                : MessageBox.Show(owner,
                    AppLanguage.Text("TaskbarPinRequiredPrompt", language),
                    AppLanguage.Text("TaskbarPinRequiredTitle", language),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);

            if (choice != MessageBoxResult.OK)
                return; // Ask again the next time the launcher is shown because this is the primary entry.

            // Windows 10 often cannot expose the consent-based TaskbarManager flow to an unpackaged
            // desktop app. In that case, show the app's Start entry and let the user use Explorer's
            // supported right-click command. Never mutate the protected taskbar pin store directly.
            if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
                TaskbarPinStatus.Failed)
            {
                ShowManualPinLocation(pin, owner, language);
                return;
            }

            state = await pin.RequestPinAsync();
            if (state.Status is TaskbarPinStatus.Pinned or TaskbarPinStatus.Available)
                return; // Available here means the user declined the Windows confirmation.

            ShowManualPinLocation(pin, owner, language);
        }
        finally
        {
            host.SuppressHide(false);
        }
    }

    private static void ShowManualPinLocation(TaskbarPinService pin, Window? owner, string? language)
    {
        if (pin.OpenManualPinLocation())
            return;

        var message = AppLanguage.Text("PinToTaskbarManualOpenFailed", language);
        if (owner is null)
            MessageBox.Show(message, "TanMenu", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(owner, message, "TanMenu", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static async Task CheckForUpdatesAfterStartupAsync()
    {
        try
        {
            // Keep startup responsive; the portable build checks and downloads quietly once the UI
            // is ready. Installation still waits for an explicit restart from Settings.
            await Task.Delay(TimeSpan.FromSeconds(3));
            var updater = Services.GetRequiredService<IAppUpdateService>();
            await updater.CheckForUpdatesAsync();
            if (updater.State.Status == AppUpdateStatus.Available)
                await updater.DownloadUpdateAsync();

            if (updater.State.Status == AppUpdateStatus.ReadyToRestart)
            {
                var language = Services.GetRequiredService<ConfigService>().Config.General.Language;
                var restart = MessageBox.Show(
                    AppLanguage.Format("UpdateRestartPrompt", language, updater.State.Version ?? ""),
                    AppLanguage.Text("UpdateTitle", language),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (restart == MessageBoxResult.Yes)
                    updater.ApplyAndRestart();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background update check failed");
        }
    }

    /// <summary>Resolve the writable data root and ensure its folders exist, falling back to the
    /// always-local <c>%LOCALAPPDATA%\TanMenu</c> if the resolved root (e.g. an offline redirected
    /// Documents, or a read-only/full volume) can't be created.</summary>
    private static IAppDataPaths CreateDataPaths()
    {
        if (PackageRuntime.HasPackageIdentity)
        {
            var packagedPaths = new AppDataPaths();
            packagedPaths.EnsureCreated();
            return packagedPaths;
        }

        if (IsPortable)
            return new MutableAppDataPaths(PortableRuntime.GetDataRoot(PortableDataRoot));

        var root = DataLocation.GetDataRoot(); // default Documents\TanMenu, or a user-chosen folder
        try
        {
            return new MutableAppDataPaths(root); // ctor ensures the folders exist
        }
        catch (Exception)
        {
            // If this also throws, StartupAsync's caller surfaces a message and exits cleanly.
            return new MutableAppDataPaths(DataLocation.LocalAppDataRoot);
        }
    }

    /// <summary>Configure Serilog, degrading to console-only if the log folder/file can't be opened
    /// (locked by AV, read-only ACL, disk full) so logging can never crash startup.</summary>
    private static void ConfigureLogging(IAppDataPaths paths)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(paths.LogsFolder, "log-.txt"),
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
        catch (Exception)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();
        }
    }

    /// <summary>Log an exception without ever throwing (used from the global handlers, where logging
    /// may not be configured yet — Serilog's default sink is a safe no-op).</summary>
    private static void TryLogFatal(Exception? ex, string message)
    {
        try { Log.Error(ex, message); } catch { /* logging must never throw */ }
    }

    public void ExitApp()
    {
        Tray?.Dispose();
        (Services as IDisposable)?.Dispose(); // disposes singletons (unregisters the global hotkey, etc.)
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        Shutdown();
    }
}
