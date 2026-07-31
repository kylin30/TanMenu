using Avalonia.Threading;

namespace TanMenu.Wpf.Services;

/// <summary>Shows the 设置 page (Task 13 absorbed the old standalone settings window into the shell).</summary>
public interface ISettingsLauncher
{
    /// <summary>Asynchronous for the callers that want to know the navigation actually landed — the
    /// implementation itself cannot fail in any way worth reporting (there is no window construction,
    /// font enumeration or resource load on this path any more), but the signature is kept so every
    /// existing call site (tray menu, launcher menu bar) needs no change.</summary>
    Task OpenAsync();

    /// <summary>Fire-and-forget form for the many call sites that only want the settings page on screen
    /// and have no result to wait for.</summary>
    void Open();
}

public sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;
    private readonly IShellNavigator _navigator;

    public SettingsLauncher(IWindowHost host, IShellNavigator navigator)
    {
        _host = host;
        _navigator = navigator;
    }

    public void Open() => _ = OpenAsync();

    /// <summary>Settings is a PAGE now, not a window: this selects it and brings the main window
    /// forward. Still async and still marshalled to the UI thread — the tray menu calls it from a
    /// shell thread.</summary>
    public Task OpenAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OpenCore();
            return Task.CompletedTask;
        }

        var marshalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                OpenCore();
                marshalled.TrySetResult();
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    private void OpenCore()
    {
        _navigator.Navigate(ShellPage.Settings);
        _host.ShowAndActivate();
    }
}
