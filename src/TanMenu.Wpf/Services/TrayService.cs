using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>System-tray icon: left-click toggles the launcher; right-click menu = 显示 / 设置 / 退出.</summary>
public sealed class TrayService : IDisposable
{
    private readonly IWindowHost _host;
    private readonly Action _exit;
    private readonly Action _openSettings;
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private TaskbarIcon? _icon;
    private System.Drawing.Icon? _gdiIcon; // keep alive so its HICON stays valid
    private MenuItem? _showItem;
    private MenuItem? _settingsItem;
    private MenuItem? _exitItem;

    public TrayService(IWindowHost host, Action exit, Action openSettings, ConfigService config, AppEvents events)
    {
        _host = host;
        _exit = exit;
        _openSettings = openSettings;
        _config = config;
        _events = events;
        _events.SettingsChanged += ApplyMenuText;
    }

    public void Create(string icoPath)
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "TanMenu",
            NoLeftClickDelay = true,
        };

        _icon.LeftClickCommand = new RelayCommand(() => _host.Toggle());

        // WPF ContextMenu with Command-wired items (WPF MenuItem.Command fires reliably).
        _showItem = new MenuItem { Command = new RelayCommand(() => _host.ShowAndActivate()) };
        _settingsItem = new MenuItem { Command = new RelayCommand(_openSettings) };
        _exitItem = new MenuItem { Command = new RelayCommand(_exit) };
        ApplyMenuText();
        var menu = new ContextMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_exitItem);
        _icon.ContextMenu = menu;

        _icon.ForceCreate(enablesEfficiencyMode: false);

        // Set the tray image by handing the core TrayIcon a real HICON. H.NotifyIcon.Wpf's
        // IconSource (ImageSource) conversion renders BLANK in this code-behind setup, so we
        // bypass it and call the core UpdateIcon(HICON) directly after the icon is created.
        if (System.IO.File.Exists(icoPath))
        {
            try
            {
                _gdiIcon = new System.Drawing.Icon(icoPath, new System.Drawing.Size(32, 32));
                _icon.TrayIcon?.UpdateIcon(_gdiIcon.Handle);
            }
            catch (Exception ex)
            {
                // A corrupt/locked .ico must not take down the whole tray — the tray icon is the
                // primary way to re-summon a hidden launcher. Leave a blank icon; the menu still works.
                Serilog.Log.Warning(ex, "Failed to set tray icon from {Path}", icoPath);
            }
        }
    }

    private void ApplyMenuText()
    {
        try
        {
            var language = _config.Config.General.Language;
            if (_showItem != null) _showItem.Header = AppLanguage.Text("TrayShow", language);
            if (_settingsItem != null) _settingsItem.Header = AppLanguage.Text("TraySettings", language);
            if (_exitItem != null) _exitItem.Header = AppLanguage.Text("TrayExit", language);
        }
        catch { /* best effort; tray text is non-critical */ }
    }

    public void Dispose()
    {
        _events.SettingsChanged -= ApplyMenuText;
        _icon?.Dispose();
        _icon = null;
        _gdiIcon?.Dispose();
        _gdiIcon = null;
    }
}

/// <summary>Minimal relay command for tray + menu actions.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
