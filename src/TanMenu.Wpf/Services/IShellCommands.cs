using System.Windows;

namespace TanMenu.Wpf.Services;

/// <summary>App-level shell commands the Blazor UI can invoke (e.g. from the top menu bar).</summary>
public interface IShellCommands
{
    /// <summary>Fully exit the application (tray + mutex torn down).</summary>
    void Exit();
}

public sealed class WpfShellCommands : IShellCommands
{
    public void Exit()
    {
        var app = (App)Application.Current;
        app.Dispatcher.Invoke(app.ExitApp);
    }
}
