using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views;

namespace TanMenu.Wpf;

/// <summary>
/// The shell: the system title bar, the fixed navigation rail and the page host. All launcher state
/// and behaviour now live in <see cref="AppPage"/> (via <c>LauncherViewModel</c>); this class owns only
/// what genuinely belongs to the window — the Mica fallback, close-to-tray, and swapping the page
/// host's content between the two rail entries.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWindowHost _host;
    private readonly AppPage _appPage = new();
    private Control? _settingsPage;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        App.Services.GetRequiredService<ShellNavigator>().Attach(this);

        // AppLanguage/AppFonts have no change notification of their own, so both the rail labels and
        // the window font are re-applied on every SettingsChanged — the same imperative approach
        // SettingsWindow already uses for its own font.
        var events = App.Services.GetRequiredService<AppEvents>();
        events.SettingsChanged += ApplyWindowFont;
        events.SettingsChanged += ApplyRailText;
        ApplyWindowFont();
        ApplyRailText();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyBackdrop();

        // The host owns show/hide/placement and needs the window once its HWND exists. The DI
        // registration deliberately hands out the same instance for IWindowHost and WindowHost.
        var host = _host as WindowHost;
        host?.Attach(this);
        host?.RestorePlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        // AppPage's view model is disposed here, not on the page's own Unloaded: OnClosed only fires
        // at real application exit (OnClosing below cancels every other close), while the app page
        // itself is a field kept alive and reattached every time the rail switches back to 应用.
        _appPage.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Mica is a request, not a guarantee: Windows 10, "show transparency" turned off and
    /// remote desktop all refuse it, and the window would then be a hole rather than a surface. Paint
    /// the opaque Fluent surface colour when it was not granted. (Same guard SettingsWindow used.)</summary>
    private void ApplyBackdrop()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
            Background = this.FindResource("SurfaceBaseBrush") as IBrush ?? Brushes.White;
    }

    /// <summary>Closing hides to the tray; the app exits only through 退出 (tray menu or overflow
    /// menu), which calls <see cref="App.ExitApp"/> and shuts the lifetime down explicitly.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            _host.Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Swap the page host's content. Both pages are constructed lazily and then kept alive:
    /// the app page holds the built menu and the settings page holds staged control state, and
    /// rebuilding either on every rail click would be visible.</summary>
    public void ShowPage(ShellPage page)
    {
        NavRail.SelectedIndex = page == ShellPage.Settings ? 1 : 0;
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new TextBlock { Text = "…", Margin = new Thickness(24) } // Task 8 replaces this with SettingsPage
            : _appPage;
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // MUST read SelectedIndex off the sender, not the NavRail field: XAML's SelectedIndex="0"
        // raises this SAME event synchronously during InitializeComponent's EndInit, before the
        // generated populate code has assigned this.NavRail — reading the field there null-refs.
        if (sender is not ListBox rail)
            return;

        App.Services.GetRequiredService<ShellNavigator>()
            .Navigate(rail.SelectedIndex == 1 ? ShellPage.Settings : ShellPage.App);
    }

    /// <summary>AppLanguage has no change notification, so the rail is labelled imperatively — the same
    /// approach the settings UI uses.</summary>
    private void ApplyRailText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        NavAppsLabel.Text = AppLanguage.Text("NavApps", lang);
        NavSettingsLabel.Text = AppLanguage.Text("NavSettings", lang);
    }

    /// <summary>The 字体 setting used to reach the window through a DataContext binding. The shell has
    /// no view model of its own now, so it is applied imperatively — and re-applied on SettingsChanged,
    /// because AppLanguage/AppFonts have no change notification.</summary>
    private void ApplyWindowFont()
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        FontFamily = AppFonts.Resolve(cfg.Config.General.FontFamily);
    }
}
