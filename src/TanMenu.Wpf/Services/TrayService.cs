using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;

namespace TanMenu.Wpf.Services;

/// <summary>System-tray icon: left-click toggles the launcher; right-click menu = 显示 / 退出.</summary>
public sealed class TrayService : IDisposable
{
    private readonly IWindowHost _host;
    private readonly Action _exit;
    private readonly Action _openSettings;
    private TaskbarIcon? _icon;

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
        if (System.IO.File.Exists(icoPath))
            _icon.Icon = new System.Drawing.Icon(icoPath);

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

        _icon.ForceCreate();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
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
