using Avalonia.Threading;

namespace TanMenu.Wpf.Services;

/// <summary>Navigates the existing launcher window to its full-page settings surface.</summary>
public interface ISettingsLauncher
{
    Task OpenAsync();
    void Open();
}

public sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;

    public SettingsLauncher(IWindowHost host)
    {
        _host = host;
    }

    public void Open() =>
        _ = OpenAsync().ContinueWith(
            task => Serilog.Log.Error(task.Exception, "打开设置页面失败"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    public Task OpenAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OpenCore();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                OpenCore();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private void OpenCore()
    {
        _host.ShowAndActivate();
        if (_host.Window is MainWindow window)
            window.ShowSettingsPage();
    }
}
