using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly AppEvents _events;
    private readonly AppPage _appPage = new();
    private Control? _settingsPage;

    // Guards OnNavSelectionChanged against the SelectionChanged events XAML's own SelectedIndex="0"
    // (and Attach's own ShowPage call, if a navigation arrived before the window existed) raise
    // synchronously WHILE this constructor is still running — see OnNavSelectionChanged.
    private bool _navReady;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        App.Services.GetRequiredService<ShellNavigator>().Attach(this);

        // AppLanguage/AppFonts have no change notification of their own, so both the rail labels and
        // the window font are re-applied on every SettingsChanged — the same imperative approach
        // SettingsWindow already uses for its own font.
        _events = App.Services.GetRequiredService<AppEvents>();
        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyRailText;
        ApplyWindowFont();
        ApplyRailText();

        _navReady = true;
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

        // The startup build begins here, after the window is actually shown — never from AppPage's
        // own constructor, which runs earlier (as MainWindow's field initializer, before Show()) and
        // would make the first-summon gate in LauncherViewModel.InitializeAsync/HandleLauncherShownAsync
        // timing-dependent on whether the build finishes before Show() instead of strictly ordered
        // after it, as it was before this class existed.
        _appPage.BeginInitialize();
    }

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyRailText;

        // AppPage's view model is disposed here, not on the page's own Unloaded: OnClosed only fires
        // at real application exit (OnClosing below cancels every other close), while the app page
        // itself is a field kept alive and reattached every time the rail switches back to 应用.
        _appPage.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Escape reaches here only when focus was NOT inside <see cref="AppPage"/> — e.g. a nav-rail row.
    /// AppPage's own <c>OnKeyDown</c> already handles Escape (and marks it handled) for the search box
    /// and the tile grid, matching the clear-then-hide behaviour it always had. Before the rail existed,
    /// this window's own OnKeyDown handled Escape identically regardless of where focus was — routing
    /// this fallback through the same view-model call (rather than an unconditional Hide) keeps that
    /// behaviour rather than merely approximating it.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape)
            return;

        _appPage.HandleGlobalEscape();
        e.Handled = true;
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

    /// <summary>Swap the page host's content. <see cref="_appPage"/> is built eagerly (a field
    /// initializer, before this constructor even runs) since it holds the launcher's real state; the
    /// settings placeholder — a bare <see cref="TextBlock"/> until Task 8 replaces it with a real
    /// settings page — is built lazily, the first time it is shown. Both are then kept alive rather
    /// than rebuilt on every rail click, which matters once the placeholder becomes a real page with
    /// its own staged control state.</summary>
    public void ShowPage(ShellPage page)
    {
        NavRail.SelectedIndex = page == ShellPage.Settings ? 1 : 0;
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new TextBlock { Text = "…", Margin = new Thickness(24) } // Task 8 replaces this with SettingsPage
            : _appPage;
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Ignored until the constructor finishes (_navReady): XAML's SelectedIndex="0" — and,
        // once a real navigation can arrive before the window exists (Task 13's tray path),
        // Attach's own ShowPage call — both raise this event synchronously during construction.
        // ShowPage already applies NavRail.SelectedIndex and PageHost.Content directly, so nothing
        // is lost by discarding a SelectionChanged that fires before the shell is ready; letting it
        // through would instead let Navigate(App) (this event's own read of a freshly-defaulted
        // SelectedIndex="0") clobber a Current a pre-construction Navigate(Settings) had already set.
        //
        // MUST read SelectedIndex off the sender, not the NavRail field: this event can fire before
        // the generated populate code has assigned this.NavRail — reading the field there null-refs.
        if (!_navReady || sender is not ListBox rail)
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
