using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WebView.Services = App.Services;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The inner WebView2 is created when the BlazorWebView loads (null in the ctor).
        var web = WebView.WebView;
        if (web is null)
            return;

        // KEY for the borderless transparent shell: transparent WebView2 surface so the
        // CSS-drawn retro window shows through the AllowsTransparency window.
        web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        ((WindowHost)App.Services.GetRequiredService<IWindowHost>()).Attach(this, web);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

            // System tray.
            var host = App.Services.GetRequiredService<IWindowHost>();
            App.Tray = new TrayService(
                host,
                () => ((App)Application.Current).ExitApp(),
                () => App.Services.GetRequiredService<ISettingsLauncher>().Open());
            App.Tray.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico"));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to initialize tray/hotkey");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.WmShowFirstInstance)
        {
            App.Services.GetRequiredService<IWindowHost>().ShowAndActivate();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
