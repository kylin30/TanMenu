using System.Windows;

namespace TanMenu.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WebView.Services = App.Services;
        // KEY for the borderless transparent shell: make the WebView2 surface transparent
        // so the CSS-drawn retro window shows through the AllowsTransparency window.
        WebView.WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
    }
}
