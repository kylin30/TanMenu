using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using TanMenu.Core.Infrastructure;
using TanMenu.Wpf.Services;
using Velopack;

namespace TanMenu.Wpf;

/// <summary>
/// Process entry point (<c>&lt;StartupObject&gt;TanMenu.Wpf.Program&lt;/StartupObject&gt;</c>). The
/// ordering inside <see cref="Main"/> is load-bearing: Velopack's command-line hooks, the
/// single-instance handshake and the shell identity all have to be settled before the UI toolkit —
/// and therefore any file handle in the application directory — comes up.
/// </summary>
public static partial class Program
{
    private const string SingleInstanceMutexName = "TanMenuWpf";
    private const string SingleInstanceActivationEventName = "TanMenuWpf.ActivateFirstInstance";

    /// <summary>The first process's window-independent activation signal. App waits on it for the
    /// process lifetime, so a hidden window can be summoned without relying on its HWND.</summary>
    internal static EventWaitHandle? FirstInstanceActivationEvent { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Two of the three last-resort safety nets are process-wide rather than UI-thread bound, so
        // wire them before anything else can fault. They allocate nothing and open no file, which is
        // what lets them sit in front of the Velopack hook below. (The UI-thread handler needs a
        // dispatcher and is wired in App.OnFrameworkInitializationCompleted.)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            App.TryLogFatal(e.ExceptionObject as Exception, "AppDomain unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            App.TryLogFatal(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        var isPackaged = PackageRuntime.HasPackageIdentity;

        // Velopack command-line hooks must run before the UI toolkit, DI, logging, or any application
        // file is opened — the hook may install/uninstall/update and terminate the process, and it
        // fails on a locked file. Microsoft Store/MSIX builds remain entirely Store-managed.
        if (!isPackaged)
        {
            VelopackApp.Build()
                .SetArgs(args)
                .SetAutoApplyOnStartup(false)
                .Run();
        }

        // A taskbar-pin click starts the stable launcher again. Reject that second instance before
        // loading the UI toolkit or touching the taskbar identity: after a long idle those assemblies
        // may have been paged out, which used to add seconds before the existing launcher even
        // received its show message.
        // Create/open the activation event before taking the mutex. If two processes start almost
        // together, the loser can set this event even before the winner has finished creating its
        // window; AutoResetEvent retains that signal until App starts its listener.
        using var activationEvent = new EventWaitHandle(
            false, EventResetMode.AutoReset, SingleInstanceActivationEventName,
            out var createdNewActivationEvent);

        var mutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalFirstInstance(activationEvent, createdNewActivationEvent);
            mutex.Dispose();
            return 0;
        }

        FirstInstanceActivationEvent = activationEvent;

        if (!isPackaged)
        {
            // Give the unpackaged process the same stable identity as its Start-menu shortcut. This
            // must happen before any window exists so Explorer groups the running app with the
            // permanent taskbar pin instead of producing a second, transient icon. It runs AFTER the
            // mutex check so a rejected second instance never touches the shell identity.
            TaskbarPinService.InitializeProcessIdentity();
        }

        // The mutex is owned by this process for its whole life; App.ExitApp releases it.
        App.SingleInstanceMutex = mutex;

        // OnExplicitShutdown: hiding or closing the launcher must NOT exit the app — it lives on in
        // the tray until the user picks 退出 (or the shell/update path calls ExitApp).
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            FirstInstanceActivationEvent = null;
        }
    }

    /// <summary>Avalonia entry point, also used by the XAML previewer/designer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithBundledFonts()
            .LogToTrace();

    /// <summary>Broadcast message a second instance posts to resurface the first.
    /// <para>
    /// Registered by NAME rather than addressed to a window found by caption. The caption used to be
    /// protocol (<c>FindWindow(null, "TanMenuWpf")</c>), which forced the visible window title to be
    /// the internal name; now that the window lives in the taskbar and Alt+Tab that title is
    /// user-visible, and matching "TanMenu" by caption would be unsafe because the data folder is also
    /// called TanMenu and an Explorer window could match first.
    /// </para></summary>
    private const string ShowFirstInstanceMessageName = "TanMenuWpf.ShowFirstInstance";

    /// <summary>The registered message id, identical in every process that registers the same string.
    /// Zero if registration failed, in which case second-instance activation degrades to a no-op.</summary>
    public static uint ShowFirstInstanceMessage { get; } = RegisterWindowMessageW(ShowFirstInstanceMessageName);

    private const int HwndBroadcast = 0xFFFF;
    private const uint AsfwAny = uint.MaxValue;

    /// <summary>Ask an already-running instance to resurface. New versions use a named event that is
    /// independent of window visibility; the registered-window broadcast remains as compatibility for
    /// a pre-event version that is already running during an update.</summary>
    private static void SignalFirstInstance(EventWaitHandle activationEvent, bool createdNewActivationEvent)
    {
        // The process launched by the user's click owns foreground permission. Hand it to the
        // existing process before signalling; otherwise that process's SetForegroundWindow can be
        // rejected by Windows even though the activation was explicitly user initiated.
        AllowSetForegroundWindow(AsfwAny);

        if (!createdNewActivationEvent)
        {
            activationEvent.Set();
            return;
        }

        // If this process created the event, the existing instance predates the event protocol.
        if (ShowFirstInstanceMessage != 0)
            PostMessageW((IntPtr)HwndBroadcast, ShowFirstInstanceMessage, IntPtr.Zero, IntPtr.Zero);
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint dwProcessId);
}
