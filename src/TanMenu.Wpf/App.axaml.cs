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
using TanMenu.Wpf.Views.Settings;
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

    /// <summary>Set by <see cref="ExitApp"/> before the lifetime is shut down, so MainWindow.OnClosing
    /// can tell a real exit from the close button (which hides to the tray).</summary>
    public static bool IsExiting { get; private set; }

    /// <summary>Unsubscriber for the OS accent/colour subscription. Held for the process lifetime —
    /// the app is tray-resident, so this is released only by exit.</summary>
    private IDisposable? _platformColors;

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
        // ROUND 8, TASK 2 REMOVED TWO REGISTRATIONS HERE — ShellNavigator and IShellNavigator, the
        // shell's page router. The launcher has exactly one page now: 设置 became a modal dialog
        // (Dialogs/OptionsWindow), so there is nothing to navigate BETWEEN, and Services/IShellNavigator.cs
        // was deleted whole rather than left registered and unread.
        services.AddSingleton<ISettingsLauncher, SettingsLauncher>();
        // The settings surface's pending-changes buffer (Round 8, Task 1). Registered rather than
        // constructed by the page because the Options dialog's five card groups and its commit row have
        // to share ONE draft — plus AboutCard, which resolves it for the UI language alone and writes
        // nothing (Round 8, Task 4 moved that card into Dialogs/AboutWindow) —
        // and none of them can be handed it: Avalonia's XAML loader owns the cards' construction
        // and ISettingsCard has no member to push it through. Resolved lazily, so it is seeded from the
        // config as it stands when 设置 — or 关于, whose card resolves it too — is first opened; the seed
        // is a config clone plus one registry read either way. Re-seeded in place afterwards (Reseed) on every
        // close AND on every successful commit, never replaced, so the commit row's DirtyChanged
        // subscription survives all of them.
        services.AddSingleton(sp => SettingsDraft.From(
            sp.GetRequiredService<ConfigService>(),
            sp.GetRequiredService<IAutoStartService>()));
        services.AddSingleton<IShellCommands, ShellCommands>();
        services.AddSingleton<IUpdatePromptService, UpdatePromptService>();
        services.AddSingleton<IAppUpdateService>(_ =>
            PackageRuntime.HasPackageIdentity
                ? new FixedAppUpdateService(AppUpdateStatus.StoreManaged)
                : IsPortable && VelopackLocator.Current.IsPortable
                    ? new VelopackAppUpdateService()
                    : new FixedAppUpdateService(AppUpdateStatus.Disabled));
        // NOTE: ThemeService and the BlazorWebView registrations are gone with the CSS theme system
        // and the WebView. There is exactly one look now (Themes/Shell, applied in App.axaml),
        // rendered in a light or dark variant chosen by AppTheme.

        Services = services.BuildServiceProvider();

        // Load config before the UI renders. NOTE: must await (not block) — blocking the UI thread
        // here deadlocks, because the continuation posts to a dispatcher that isn't pumping yet.
        await Services.GetRequiredService<ConfigService>().LoadAsync();

        // Light/dark before the first window is constructed, so a dark-mode user never sees a light
        // launcher paint and then flip. ThemeVariant.Default (the "System" setting) makes Avalonia
        // track the OS itself, so there is nothing to re-apply on an OS theme change.
        AppTheme.Apply(this, Services.GetRequiredService<ConfigService>().Config.General.Appearance);
        // The real Windows accent, replacing FluentTheme's hardcoded #0078D7. Platform settings hang
        // off Application in Avalonia 12, so this needs no window to exist yet.
        AppTheme.PublishAccent(this);
        _platformColors = AppTheme.TrackPlatformChanges(this);
        Services.GetRequiredService<SoundService>()
            .Initialize(Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds"));

        // Warn the user if a global-hotkey (re)bind fails (occupied/invalid) instead of only logging.
        // Marshal onto the dispatcher asynchronously: the first Apply() runs synchronously inside the
        // launcher window's Opened handler, and a modal dialog there would pump a nested message loop
        // mid-window-init (before the tray is created). Post defers it until init finishes.
        Services.GetRequiredService<GlobalHotkeyService>().RegistrationFailed += combo =>
            Dispatcher.UIThread.Post(() => _ = WarnHotkeyRegistrationFailedAsync(combo));

        ShowLauncherWindow();
        _ = CheckForUpdatesAfterStartupAsync();
    }

    /// <summary>
    /// Create the main window and show it. It is an ordinary application window now — no reveal
    /// sequence.
    /// </summary>
    /// <remarks>
    /// The cold-start order below is the whole startup contract, and each step has to be where it is:
    /// <list type="number">
    /// <item>ATTACH, so the host owns the window before anything can move it.</item>
    /// <item>RESTORE PLACEMENT, while the window is still off screen. Avalonia realizes the native
    /// window in <c>Window</c>'s constructor, so the HWND — and therefore <c>Screens</c> — already
    /// exists here; if the platform reported no screens the host falls back to a centred default
    /// rather than throwing. Doing this after <c>Show()</c> (as it was until 0.12, from
    /// <c>MainWindow.OnOpened</c>) paints one frame at the OS cascade position and then jumps,
    /// measured at ~30 ms.</item>
    /// <item>SHOW, which synchronously runs <c>MainWindow.OnOpened</c>: the tray/hotkey wiring below
    /// and then <c>AppPage.BeginInitialize()</c>, the startup menu build. Round 2's Task 4 added a
    /// further, still-asynchronous step after that build completes — <c>WindowHost.FitToContent</c>,
    /// which resizes the window to what the build actually produced. Nothing below depends on THAT step
    /// specifically finishing before it runs; it is ordered only relative to the build, not to
    /// LauncherShown.</item>
    /// <item>RAISE LauncherShown, last. It must follow the startup build, not precede it: the view
    /// model's first-summon guard exists to suppress the redundant rescan the startup build already
    /// covers, and when the event arrived first (raised from <c>OnLauncherOpened</c>, i.e. inside
    /// <c>base.OnOpened</c>) the guard saw no fingerprint yet, skipped itself, and every cold start
    /// built the entire menu twice.</item>
    /// </list>
    /// </remarks>
    private static void ShowLauncherWindow()
    {
        var window = new MainWindow();

        var host = Services.GetRequiredService<WindowHost>();
        host.Attach(window);
        host.RestorePlacement();

        // The HWND-dependent wiring (message hook, global hotkey, tray) needs a realized window, so it
        // runs from Opened — the Avalonia analogue of WPF's SourceInitialized.
        window.Opened += OnLauncherOpened;
        window.Show();
        Log.Information("Launcher window shown");

        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;

        // A cold start must produce the same LauncherShown a tray or hotkey summon does: the search
        // box takes focus from it, and so does AppPage's taskbar-pin InfoBar refresh. Guarded like the
        // tray wiring above — losing the pin notice or the initial focus is bad but survivable, and
        // must not turn into StartupAsync's fatal-error-and-exit path.
        try
        {
            Services.GetRequiredService<AppEvents>().RaiseLauncherShown();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LauncherShown handlers faulted on cold start");
        }
    }

    private static void OnLauncherOpened(object? sender, EventArgs e)
    {
        // One catch-all for the whole block, as before: a failure in an earlier step deliberately
        // skips the later ones rather than taking the process down. Losing the tray is bad but
        // survivable — the hotkey can still resurface the launcher.
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

            // NOTE: LauncherShown is deliberately NOT raised here. This handler runs from inside
            // base.OnOpened — i.e. BEFORE MainWindow.OnOpened has started the menu build — and raising
            // it here made the view model's first-summon guard useless, so every cold start built the
            // whole menu twice. ShowLauncherWindow raises it after Show() returns instead.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize tray");
        }
    }

    /// <summary>
    /// The one <c>SettingsChanged</c> consequence that can BOTH fail and want a dialog:
    /// <c>GlobalHotkeyService.Apply</c> raises <c>RegistrationFailed</c> when Windows refuses the
    /// combination, and a rejected hotkey the user cannot see is a setting that silently did not take.
    /// </summary>
    /// <remarks>
    /// <b>THE OWNER IS <c>OptionsWindow.Current ?? host.Window</c>, and the fallback is not defensive
    /// tidiness.</b> Round 8, Task 2 made 设置 a MODAL dialog on the launcher, and a modal owner is a
    /// DISABLED owner — so owning this warning to <c>host.Window</c> while that dialog is up is a
    /// <c>ShowDialog</c> on a window Windows will not let take input. The timing settles which branch
    /// runs, deterministically rather than by luck: this method is <c>Dispatcher.UIThread.Post</c>ed
    /// (see the subscription in <see cref="StartupAsync"/>), so it lands AFTER
    /// <c>SettingsCommit.ApplyAsync</c> has returned. On 应用 the dialog is still open and
    /// <c>Current</c> is non-null; on 确定 the dialog has closed — 确定 awaits the commit before it
    /// closes, exactly so that this is not a race — <c>Current</c> is null, and the launcher is enabled
    /// again by then. Nesting is safe because <c>WindowHost.SuppressHide</c> is reference-counted, so
    /// the bracket below cannot release the one <c>SettingsLauncher</c> holds around the dialog, and
    /// <c>MessageBox.ShowCoreAsync</c>'s own hidden-owner fallback still covers both branches.
    /// </remarks>
    private static async Task WarnHotkeyRegistrationFailedAsync(string combo)
    {
        var host = Services.GetRequiredService<IWindowHost>();
        var language = Services.GetRequiredService<ConfigService>().Config.General.Language;
        host.SuppressHide(true);
        try
        {
            await MessageBox.ShowAsync(OptionsWindow.Current ?? host.Window,
                AppLanguage.Format("HotkeyRegistrationFailed", language, combo),
                "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        }
        finally
        {
            host.SuppressHide(false);
        }
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
        // Set BEFORE the lifetime is shut down: Shutdown() below synchronously closes MainWindow,
        // whose OnClosing checks this flag to tell a real exit from the close button (which hides).
        IsExiting = true;

        // 退出 must WAIT for the placement write, not merely start it: desktop.Shutdown() below ends
        // the dispatcher loop, so a fire-and-forget save's continuation would never run — losing any
        // geometry change made inside the 500 ms debounce window and potentially leaving an orphan
        // config.json.tmp behind. This blocks the UI thread for the duration of a few-KB write
        // (bounded at 5 s inside), which is safe only because ConfigService.SaveAsync awaits with
        // ConfigureAwait(false) throughout; see its remarks.
        Services.GetRequiredService<WindowHost>().FlushPlacementBlocking();

        Tray?.Dispose();
        (Services as IDisposable)?.Dispose(); // disposes singletons (unregisters the global hotkey, etc.)
        try { SingleInstanceMutex?.ReleaseMutex(); } catch { /* ignore: not owned / already released */ }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(); // required: ShutdownMode is OnExplicitShutdown
    }
}
