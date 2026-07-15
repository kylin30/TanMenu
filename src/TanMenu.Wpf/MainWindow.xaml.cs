using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Infrastructure;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

public partial class MainWindow : Window
{
    private GlobalHotkeyService? _hotkeys;

    public MainWindow()
    {
        InitializeComponent();
        if (App.IsPortable)
        {
            // BlazorWebView creates WebView2 after this constructor. Redirect its browser profile
            // before creation so cookies, GPU cache, and IndexedDB stay inside the portable Data
            // folder instead of leaking into the user's LocalAppData profile.
            WebView.BlazorWebViewInitializing += (_, args) =>
                args.UserDataFolder = PortableRuntime.GetWebView2DataFolder();
        }
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

        // Harden the WebView in Release: no browser accelerator keys (Ctrl+R/F5 reload, Ctrl+P, …)
        // and no default context menu (right-click inspect/save-as) on the trusted local UI.
        web.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess || web.CoreWebView2 is null)
                return;
#if !DEBUG
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
#endif
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

            // Global hotkey (configurable, off by default) — registers on this window handle.
            _hotkeys = App.Services.GetRequiredService<GlobalHotkeyService>();
            _hotkeys.Attach(hwnd);

            // System tray.
            var host = App.Services.GetRequiredService<IWindowHost>();
            App.Tray = new TrayService(
                host,
                () => ((App)Application.Current).ExitApp(),
                () => App.Services.GetRequiredService<ISettingsLauncher>().Open(),
                App.Services.GetRequiredService<TanMenu.Core.Services.ConfigService>(),
                App.Services.GetRequiredService<AppEvents>());
            App.Tray.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico"));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to initialize tray");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys?.ProcessMessage(msg, wParam) == true)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == App.WmShowFirstInstance)
        {
            App.Services.GetRequiredService<IWindowHost>().ShowAndActivate();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
