using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;

namespace TanMenu.Wpf.Services;

/// <summary>System-tray icon: left-click toggles the launcher; right-click menu = 显示 / 设置 / 退出.</summary>
public sealed class TrayService : IDisposable
{
    private readonly IWindowHost _host;
    private readonly Action _exit;
    private readonly Action _openSettings;
    private TaskbarIcon? _icon;
    private System.Drawing.Icon? _gdiIcon; // keep alive so its HICON stays valid

    public TrayService(IWindowHost host, Action exit, Action openSettings)
    {
        _host = host;
        _exit = exit;
        _openSettings = openSettings;
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
        var show = new MenuItem { Header = "显示", Command = new RelayCommand(() => _host.ShowAndActivate()) };
        var settings = new MenuItem { Header = "设置", Command = new RelayCommand(_openSettings) };
        var exit = new MenuItem { Header = "退出", Command = new RelayCommand(_exit) };
        var menu = new ContextMenu();
        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(exit);
        _icon.ContextMenu = menu;

        _icon.ForceCreate(enablesEfficiencyMode: false);

        // Set the tray image by handing the core TrayIcon a real HICON. H.NotifyIcon.Wpf's
        // IconSource (ImageSource) conversion renders BLANK in this code-behind setup, so we
        // bypass it and call the core UpdateIcon(HICON) directly after the icon is created.
        if (System.IO.File.Exists(icoPath))
        {
            _gdiIcon = new System.Drawing.Icon(icoPath, new System.Drawing.Size(32, 32));
            _icon.TrayIcon?.UpdateIcon(_gdiIcon.Handle);
        }
    }

    public void Dispose()
    {
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
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
