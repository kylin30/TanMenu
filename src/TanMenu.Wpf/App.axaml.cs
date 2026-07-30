using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;
using Velopack.Locators;

namespace TanMenu.Wpf;

public partial class App : Application
{
    /// <summary>Service locator for the code-behind of windows Avalonia constructs for us.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static TrayService? Tray { get; set; }
    public static bool IsPortable { get; private set; }
    public static string PortableDataRoot { get; private set; } = AppContext.BaseDirectory;

    /// <summary>Owned by <see cref="Program.Main"/>; released by <see cref="ExitApp"/>.</summary>
    internal static Mutex? SingleInstanceMutex { get; set; }

    private bool _taskbarPinPromptActive;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        // Last-resort safety net for UI-thread faults, wired FIRST so no later startup step can fault
        // silently. Avalonia mirrors WPF here: the filter decides whether the dispatcher catches at
        // all, the handler decides whether the app survives. The background-thread and
        // fire-and-forget-Task nets are wired even earlier, in Program.Main.
        Dispatcher.UIThread.UnhandledExceptionFilter += (_, args) => args.RequestCatch = true;
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            TryLogFatal(args.Exception, "Unhandled dispatcher exception");
            args.Handled = true; // keep the app alive through a transient runtime UI glitch
        };

        base.OnFrameworkInitializationCompleted();

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
            // language is null: config may not have loaded, so AppLanguage falls back to the UI culture.
            await MessageBox.ShowAsync(null, AppLanguage.Format("StartupFailed", null, ex.Message),
                "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Error);
            ExitApp();
        }
    }

    /// <summary>The fragile part of startup (data paths, logging, DI, config, the launcher window).
    /// Wrapped by <see cref="OnFrameworkInitializationCompleted"/>'s try/catch so any failure becomes a
    /// message + clean exit rather than a silent crash.</summary>
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
        // Same instance behind both registrations: startup needs the concrete WindowHost to Attach the
        // launcher window, everything else only ever sees IWindowHost.
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
        services.AddSingleton<ISettingsLauncher, SettingsLauncher>();
        services.AddSingleton<IShellCommands, ShellCommands>();
        services.AddSingleton<IUpdatePromptService, UpdatePromptService>();
        services.AddSingleton<IAppUpdateService>(_ =>
            PackageRuntime.HasPackageIdentity
                ? new FixedAppUpdateService(AppUpdateStatus.StoreManaged)
                : IsPortable && VelopackLocator.Current.IsPortable
                    ? new VelopackAppUpdateService()
                    : new FixedAppUpdateService(AppUpdateStatus.Disabled));
        // NOTE: ThemeService and the BlazorWebView registrations are gone with the CSS theme system
        // and the WebView. There is exactly one theme now (Themes/Retro.axaml, applied in App.axaml).

        Services = services.BuildServiceProvider();

        // Load config before the UI renders. NOTE: must await (not block) — blocking the UI thread
        // here deadlocks, because the continuation posts to a dispatcher that isn't pumping yet.
        await Services.GetRequiredService<ConfigService>().LoadAsync();
        Services.GetRequiredService<SoundService>()
            .Initialize(Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds"));

        // Warn the user if a global-hotkey (re)bind fails (occupied/invalid) instead of only logging.
        // Marshal onto the dispatcher asynchronously: the first Apply() runs synchronously inside the
        // launcher window's Opened handler, and a modal dialog there would pump a nested message loop
        // mid-window-init (before the tray is created). Post defers it until init finishes.
        Services.GetRequiredService<GlobalHotkeyService>().RegistrationFailed += combo =>
            Dispatcher.UIThread.Post(() => _ = WarnHotkeyRegistrationFailedAsync(combo));

        // Taskbar recall is the primary product workflow. Check after EVERY reveal (startup, tray,
        // hotkey, or second-instance activation), once the real window is foreground; TaskbarManager
        // rejects requests from background UI.
        Services.GetRequiredService<AppEvents>().LauncherShown += PromptForTaskbarPinAfterReveal;

        ShowLauncherWindow();
        _ = CheckForUpdatesAfterStartupAsync();
    }

    /// <summary>Create the launcher shell and bring it up transparent-but-shown, exactly as the WPF
    /// build did. The real reveal happens later from the UI layer via
    /// <see cref="IWindowHost.ShowAndActivate"/>, once the menu structure has actually rendered.</summary>
    private static void ShowLauncherWindow()
    {
        var window = new MainWindow
        {
            // Protocol, not cosmetics: a second instance finds us with FindWindow(null, title).
            // Forced here so the handshake cannot be broken by an edit to the window's XAML.
            Title = Program.MainWindowTitle,
        };

        var host = Services.GetRequiredService<WindowHost>();
        host.Attach(window);

        // The HWND-dependent wiring (message hook, global hotkey, tray) needs a realized window, so it
        // runs from Opened — the Avalonia analogue of WPF's SourceInitialized.
        window.Opened += OnLauncherOpened;
        window.Show();

        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
    }

    private static void OnLauncherOpened(object? sender, EventArgs e)
    {
        // One catch-all for the whole block, as before: a failure in an earlier step deliberately
        // skips the later ones rather than taking the process down. Losing the tray is bad but
        // survivable — the hotkey or the taskbar pin can still resurface the launcher.
        try
        {
            var window = (Window)sender!;
            window.Opened -= OnLauncherOpened;

            // Installs the WndProc hook (WM_HOTKEY + the single-instance message) and registers the
            // configured hotkey synchronously.
            Services.GetRequiredService<GlobalHotkeyService>().Attach(window);

            Tray = new TrayService(
                Services.GetRequiredService<IWindowHost>(),
                () => (Current as App)?.ExitApp(),
                () => Services.GetRequiredService<ISettingsLauncher>().Open(),
                Services.GetRequiredService<ConfigService>(),
                Services.GetRequiredService<AppEvents>());
            Tray.Create();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize tray");
        }
    }

    private static async Task WarnHotkeyRegistrationFailedAsync(string combo)
    {
        var host = Services.GetRequiredService<IWindowHost>();
        var language = Services.GetRequiredService<ConfigService>().Config.General.Language;
        host.SuppressHide(true);
        try
        {
            await MessageBox.ShowAsync(host.Window,
                AppLanguage.Format("HotkeyRegistrationFailed", language, combo),
                "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        }
        finally
        {
            host.SuppressHide(false);
        }
    }

    private void PromptForTaskbarPinAfterReveal()
    {
        if (_taskbarPinPromptActive)
            return;

        _taskbarPinPromptActive = true;
        // Fire-and-forget on the dispatcher so RaiseLauncherShown returns immediately and
        // CompleteReveal is never blocked by a modal dialog.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await PromptForTaskbarPinAsync();
            }
            catch (Exception ex)
            {
                TryLogFatal(ex, "Taskbar pin prompt failed");
            }
            finally
            {
                // A declined/failed request is checked again the next time the launcher is shown.
                // This guard only prevents overlapping prompts during one reveal operation.
                _taskbarPinPromptActive = false;
            }
        });
    }

    private static async Task PromptForTaskbarPinAsync()
    {
        var pin = Services.GetRequiredService<TaskbarPinService>();
        var state = await pin.GetStateAsync();
        if (state.Status == TaskbarPinStatus.Pinned)
            return;

        var config = Services.GetRequiredService<ConfigService>();
        var language = config.Config.General.Language;
        var host = Services.GetRequiredService<IWindowHost>();

        // The owned prompt and the following Windows confirmation temporarily move focus away from
        // the popup. Keep it alive until the whole pin flow completes, then restore normal blur-hide.
        host.SuppressHide(true);
        try
        {
            var choice = await MessageBox.ShowAsync(host.Window,
                AppLanguage.Text("TaskbarPinRequiredPrompt", language),
                AppLanguage.Text("TaskbarPinRequiredTitle", language),
                MessageBoxButtons.OkCancel, MessageBoxIcon.Info);

            if (choice != MessageBoxResult.Ok)
                return; // Ask again the next time the launcher is shown because this is the primary entry.

            // Windows 10 often cannot expose the consent-based TaskbarManager flow to an unpackaged
            // desktop app. In that case, show the app's Start entry and let the user use Explorer's
            // supported right-click command. Never mutate the protected taskbar pin store directly.
            if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
                TaskbarPinStatus.Failed)
            {
                await ShowManualPinLocationAsync(pin, host.Window, language);
                return;
            }

            state = await pin.RequestPinAsync();
            if (state.Status is TaskbarPinStatus.Pinned or TaskbarPinStatus.Available)
                return; // Available here means the user declined the Windows confirmation.

            await ShowManualPinLocationAsync(pin, host.Window, language);
        }
        finally
        {
            host.SuppressHide(false);
        }
    }

    private static async Task ShowManualPinLocationAsync(TaskbarPinService pin, Window? owner, string? language)
    {
        if (pin.OpenManualPinLocation())
            return;

        await MessageBox.ShowAsync(owner, AppLanguage.Text("PinToTaskbarManualOpenFailed", language),
            "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    private static async Task CheckForUpdatesAfterStartupAsync()
    {
        try
        {
            // Keep startup responsive and only CHECK in the background. Downloading is deliberately
            // user-initiated from the launcher/settings UI; an available update must never consume
            // bandwidth or surface a modal prompt merely because the app started.
            await Task.Delay(TimeSpan.FromSeconds(3));
            var updater = Services.GetRequiredService<IAppUpdateService>();
            await updater.CheckForUpdatesAsync();
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
    internal static void TryLogFatal(Exception? ex, string message)
    {
        try { Log.Error(ex, message); } catch { /* logging must never throw */ }
    }

    /// <summary>Full teardown. The order matters: the tray icon disappears first (the only visible
    /// feedback that 退出 was registered), then every singleton is disposed — which is what
    /// unregisters the global hotkey, stops the icon-cache timer and flushes Serilog.</summary>
    public void ExitApp()
    {
        Tray?.Dispose();
        (Services as IDisposable)?.Dispose(); // disposes singletons (unregisters the global hotkey, etc.)
        try { SingleInstanceMutex?.ReleaseMutex(); } catch { /* ignore: not owned / already released */ }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(); // required: ShutdownMode is OnExplicitShutdown
    }
}
