using Avalonia;
using Avalonia.Threading;

namespace TanMenu.Wpf.Services;

/// <summary>App-level shell commands the launcher UI can invoke (e.g. from the top menu bar).</summary>
public interface IShellCommands
{
    /// <summary>Fully exit the application (tray + mutex torn down).</summary>
    void Exit();
}

public sealed class ShellCommands : IShellCommands
{
    public void Exit()
    {
        // Teardown touches the tray icon and the application lifetime, both UI-thread affine. Stays a
        // void, thread-safe call because there is no dialog and nothing to wait for.
        if (Dispatcher.UIThread.CheckAccess())
            (Application.Current as App)?.ExitApp();
        else
            Dispatcher.UIThread.Post(() => (Application.Current as App)?.ExitApp());
    }
}
