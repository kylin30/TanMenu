using System.Windows;

namespace TanMenu.Wpf.Services;

/// <summary>Opens the native settings window (single instance; reused if already open).</summary>
public interface ISettingsLauncher
{
    void Open();
}

public sealed class WpfSettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;
    private SettingsWindow? _window;

    public WpfSettingsLauncher(IWindowHost host) => _host = host;

    public void Open()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is not null)
            {
                _window.Activate();
                return;
            }

            // Keep the launcher visible while settings is open (it would otherwise hide-on-blur).
            _host.SuppressHide(true);
            _window = new SettingsWindow();
            _window.Closed += (_, _) =>
            {
                _window = null;
                _host.SuppressHide(false);
            };
            _window.Show();
            _window.Activate();
        });
    }
}
