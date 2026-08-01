using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;
using TanMenu.Wpf.Views;

namespace TanMenu.Wpf;

/// <summary>
/// The shell: the title strip (app icon/name, search, overflow), the fixed navigation rail and the
/// page host. All launcher STATE still lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c> — the
/// search box and overflow menu here bind and dispatch to that SAME instance (exposed as
/// <see cref="AppPage.ViewModel"/>) rather than a second one, since AppPage still owns the keyboard
/// model (Escape, Down-into-the-grid) that the search box's key handling has to reach into. This class
/// owns what genuinely belongs to the window — the Mica fallback, close-to-tray, the caption-button
/// reservation, and swapping the page host's content between the two rail entries.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWindowHost _host;
    private readonly AppEvents _events;
    private readonly AppPage _appPage = new();
    private SettingsPage? _settingsPage;

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
        // the window font are re-applied on every SettingsChanged — the same imperative approach every
        // settings card group uses for its own labels (AppLanguage has no change notification there
        // either).
        _events = App.Services.GetRequiredService<AppEvents>();
        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyRailText;
        ApplyWindowFont();
        ApplyRailText();

        // The search field and overflow menu bind straight to AppPage's own LauncherViewModel — see
        // the class remarks above for why this is not a second view model.
        TitleStripContent.DataContext = _appPage.ViewModel;
        _appPage.ViewModel.PropertyChanged += OnLauncherViewModelPropertyChanged;

        _navReady = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyBackdrop();
        ApplyCaptionSpacer();

        // The host owns show/hide/placement and needs the window once its HWND exists. Idempotent, and
        // App.ShowLauncherWindow has already attached on the cold-start path; kept so a window opened
        // by any other route is still owned. The DI registration deliberately hands out the same
        // instance for IWindowHost and WindowHost.
        //
        // RestorePlacement is deliberately NOT called here any more. It ran here until 0.12, which
        // meant the window was painted at the OS cascade position and then jumped ~30 ms later to its
        // restored geometry. App.ShowLauncherWindow now applies the placement between Attach and
        // Show(), i.e. before the window is ever on screen.
        (_host as WindowHost)?.Attach(this);

        // The startup build begins here, after the window is actually shown — never from AppPage's
        // own constructor, which runs earlier (as MainWindow's field initializer, before Show()) and
        // would make the first-summon gate in LauncherViewModel.InitializeAsync/HandleLauncherShownAsync
        // timing-dependent on whether the build finishes before Show() instead of strictly ordered
        // after it, as it was before this class existed. It also has to precede the cold start's
        // LauncherShown, which App raises once Show() has returned — see ShowLauncherWindow's remarks.
        _appPage.BeginInitialize();
    }

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyRailText;
        _appPage.ViewModel.PropertyChanged -= OnLauncherViewModelPropertyChanged;

        // AppPage's view model is disposed here, not on the page's own Unloaded: OnClosed only fires
        // at real application exit (OnClosing below cancels every other close), while the app page
        // itself is a field kept alive and reattached every time the rail switches back to 应用.
        _appPage.Dispose();
        // _settingsPage may be null: it is built lazily, the first time 设置 is shown, so a session
        // that never visited it has nothing to unsubscribe.
        _settingsPage?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Escape reaches here only when focus was NOT inside the search box or <see cref="AppPage"/>'s own
    /// tile grid — e.g. a nav-rail row. Both of those already handle Escape (and mark it handled)
    /// themselves, matching the clear-then-hide behaviour the launcher always had. Before the rail
    /// existed, this window's own OnKeyDown handled Escape identically regardless of where focus was —
    /// calling the SAME view-model method here (rather than an unconditional Hide) keeps that behaviour
    /// rather than merely approximating it.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape)
            return;

        _appPage.ViewModel.HandleSearchEscape();
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
    /// initializer, before this constructor even runs) since it holds the launcher's real state;
    /// <see cref="_settingsPage"/> is built lazily, the first time 设置 is shown. Both are then kept
    /// alive rather than rebuilt on every rail click, since each card group owns its own live control
    /// state (e.g. the font combo's selection guard).</summary>
    public void ShowPage(ShellPage page)
    {
        NavRail.SelectedIndex = page == ShellPage.Settings ? 1 : 0;
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new SettingsPage()
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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Title strip: caption-button reservation
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // The most caption-area buttons Windows 11 renders for a resizable window: Close, Maximize/
    // Restore, Minimize, and the fullscreen toggle — confirmed present on this machine (see the
    // window's own UI Automation tree: AutomationId "AvaloniaWindowChrome" exposes Fullscreen,
    // Minimize, Maximize and Close as four distinct buttons). Reserving for the maximum rather than
    // whatever is actually showing costs a few DIPs of otherwise-empty strip when fewer buttons are
    // present, which is harmless; reserving for too few reproduces the overlap this exists to prevent.
    private const int MaxCaptionButtons = 4;

    // Measured on this machine at 150% scaling: each real caption button is ~45 DIP wide against a
    // 48 DIP title bar (WindowDecorationMargin.Top) — close to, but not exactly, 1:1. This factor pads
    // the Top-derived estimate rather than risk reproducing the exact overlap ApplyCaptionSpacer exists
    // to prevent if the true ratio runs slightly higher on a different Windows build or DPI.
    private const double CaptionButtonSafetyFactor = 1.15;

    /// <summary>
    /// Reserve the room the system caption buttons need, from <see cref="Window.WindowDecorationMargin"/>
    /// as the round-2 plan named — but from its Top component, not Right. Avalonia 12.1's own Win32
    /// backend (<c>WindowImpl.UpdateExtendMargins</c>) hardcodes Left/Right/Bottom to zero and only ever
    /// populates Top (the title bar height): confirmed by reading that source, and by this method's own
    /// first run on this machine, which logged <c>WindowDecorationMargin observed: 0,48,0,0</c> despite
    /// four real caption buttons rendering at the top-right corner. <c>GetSystemMetricsForDpi(SM_CXSIZE)</c>
    /// — the metric Microsoft's own "extend into the title bar" samples use — was tried first and
    /// measured 36 DIP/button against the ~45 DIP the buttons actually render at here: Windows 11's
    /// caption buttons are wider than the decades-old SM_CXSIZE metric reports, a known mismatch on this
    /// OS version. Top correlates far more closely (title bar height and button width are close to 1:1
    /// on this machine), so the reservation is Top × the button count × a small safety factor, still
    /// entirely DPI-live rather than a hardcoded pixel width. Not hooked to a live "system setting
    /// changed" notification (Windows' caption-button-size accessibility setting can change this while
    /// running); every other imperative re-apply in this class has the same limitation (AppLanguage/
    /// AppFonts have none either) and this one is rarer still, so it is computed once, from OnOpened.
    /// </summary>
    private void ApplyCaptionSpacer()
    {
        var margin = WindowDecorationMargin;
        CaptionSpacer.Width = margin.Top * MaxCaptionButtons * CaptionButtonSafetyFactor;
        Serilog.Log.Information(
            "Caption spacer: WindowDecorationMargin={Margin} (Top used) x {Count} buttons x {Factor} " +
            "safety factor = {Total} DIP reserved",
            margin, MaxCaptionButtons, CaptionButtonSafetyFactor, CaptionSpacer.Width);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Title strip: search box
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Focus is drawn on the FIELD (the bordered box), not on the bare TextBox inside it, so
    /// the accent outline frames the glyph too. The TextBox has no border of its own to light up.</summary>
    private void OnSearchFocusChanged(object? sender, RoutedEventArgs e) =>
        SearchField.Classes.Set("focused", SearchBox.IsFocused);

    /// <summary>Enter launches the first match, Escape clears then hides, Down enters the tile grid —
    /// exactly AppPage's old header behaviour. Enter/Escape are one-line calls straight into the shared
    /// LauncherViewModel; Down has to reach AppPage's own tile grid, which only AppPage's code-behind
    /// can walk, so it is forwarded through <see cref="AppPage.FocusFirstTile"/> rather than duplicated
    /// here.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _appPage.ViewModel.HandleSearchEnter();
                e.Handled = true;
                break;
            case Key.Escape:
                _appPage.ViewModel.HandleSearchEscape();
                e.Handled = true;
                break;
            case Key.Down:
                e.Handled = _appPage.FocusFirstTile();
                break;
        }
    }

    /// <summary>Deferred focus: <see cref="LauncherViewModel.PendingFocus"/> is set on every summon (and
    /// by AppPage's own "清除筛选"/Up-off-the-grid actions) and consumed here, on the next Loaded pass —
    /// one priority below layout, so the search box has already been measured and arranged by the time
    /// this runs. Unlike AppPage's pre-move version of this same mechanism, there is no attach-retry:
    /// the search box now lives in the shell, which stays in the visual tree for the window's whole
    /// life regardless of which rail entry is showing, so it is never detached the way AppPage itself
    /// can be when 设置 is on screen.</summary>
    private void OnLauncherViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LauncherViewModel.PendingFocus))
            return;

        Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
    }

    private void ConsumePendingFocus()
    {
        if (!_appPage.ViewModel.PendingFocus)
            return;

        _appPage.ViewModel.PendingFocus = false;
        try
        {
            SearchBox.Focus();
        }
        catch
        {
            /* focus is a nicety; never let it take startup down */
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Title strip: overflow menu
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _appPage.ViewModel.RefreshAsync();

    private void OnAboutClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.OpenAbout();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _appPage.ViewModel.ExitApp();
}
