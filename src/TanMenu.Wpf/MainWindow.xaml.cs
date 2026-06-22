using System.Windows;

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
        // KEY for the borderless transparent shell: make the WebView2 surface transparent
        // so the CSS-drawn retro window shows through the AllowsTransparency window.
        var web = WebView.WebView;
        if (web is not null)
        {
            web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        }
    }
}
