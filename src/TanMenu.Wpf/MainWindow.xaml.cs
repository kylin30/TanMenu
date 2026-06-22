using System.Windows;
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The inner WebView2 is created when the BlazorWebView loads (null in the ctor).
        var web = WebView.WebView;
        if (web is null)
            return;

        // KEY for the borderless transparent shell: make the WebView2 surface transparent
        // so the CSS-drawn retro window shows through the AllowsTransparency window.
        web.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        // Give the WindowHost this window + WebView2 so it can measure/place and hide-on-blur.
        ((WindowHost)App.Services.GetRequiredService<IWindowHost>()).Attach(this, web);
    }
}
