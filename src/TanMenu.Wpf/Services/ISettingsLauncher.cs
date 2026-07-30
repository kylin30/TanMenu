using Avalonia.Threading;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;

namespace TanMenu.Wpf.Services;

/// <summary>Opens the native settings window (single instance; reused if already open).</summary>
public interface ISettingsLauncher
{
    /// <summary>Asynchronous because the failure path shows a modal dialog, and Avalonia modals cannot
    /// block the caller. The window itself is still opened modelessly, exactly as before.</summary>
    Task OpenAsync();

    /// <summary>Fire-and-forget form for the many call sites (tray menu, launcher menu bar) that only
    /// want the window on screen and have no result to wait for.</summary>
    void Open();
}

public sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;
    private readonly ConfigService _config;
    private SettingsWindow? _window;

    public SettingsLauncher(IWindowHost host, ConfigService config)
    {
        _host = host;
        _config = config;
    }

    public void Open() => _ = OpenAsync();

    public Task OpenAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
            return OpenCoreAsync();

        // Callable from any thread (tray menu, launcher UI, settings-driven flows).
        var marshalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await OpenCoreAsync();
                marshalled.TrySetResult();
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    private async Task OpenCoreAsync()
    {
        if (_window is not null)
        {
            _window.Activate(); // single-instance reuse
            return;
        }

        // Keep the launcher visible while settings is open (it would otherwise hide-on-blur). The
        // suppression is released on Closed, not here: the settings window is modeless and has no
        // owner, so it can outlive this method by minutes.
        _host.SuppressHide(true);
        try
        {
            _window = new SettingsWindow();
            _window.Closed += (_, _) =>
            {
                _window = null;
                _host.SuppressHide(false);
            };
            _window.Show();
            _window.Activate();
        }
        catch (Exception ex)
        {
            // If the window can't be built (e.g. font enumeration / resource fault), undo the
            // hide-suppression — otherwise the launcher would never hide-on-blur again this
            // session — and tell the user instead of leaving a half-applied state.
            _host.SuppressHide(false);
            _window = null;
            Serilog.Log.Error(ex, "Failed to open settings window");

            // Re-suppressed only for the duration of the error dialog, which would otherwise steal
            // focus from the launcher and hide it behind itself.
            _host.SuppressHide(true);
            try
            {
                await MessageBox.ShowAsync(_host.Window,
                    AppLanguage.Text("OpenSettingsFailed", _config.Config.General.Language),
                    "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Error);
            }
            finally
            {
                _host.SuppressHide(false);
            }
        }
    }
}
