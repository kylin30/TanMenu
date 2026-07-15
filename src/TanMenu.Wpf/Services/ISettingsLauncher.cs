using System.Windows;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>Opens the native settings window (single instance; reused if already open).</summary>
public interface ISettingsLauncher
{
    void Open();
}

public sealed class WpfSettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;
    private readonly ConfigService _config;
    private SettingsWindow? _window;

    public WpfSettingsLauncher(IWindowHost host, ConfigService config)
    {
        _host = host;
        _config = config;
    }

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
                MessageBox.Show(AppLanguage.Text("OpenSettingsFailed", _config.Config.General.Language), "TanMenu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }
}
