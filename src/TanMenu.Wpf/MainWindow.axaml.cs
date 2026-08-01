using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
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
/// The shell: the title strip (app icon/name, search, overflow), the collapsible navigation rail and
/// the page host. All launcher STATE still lives in <see cref="AppPage"/>'s <c>LauncherViewModel</c> —
/// the search box and overflow menu here bind and dispatch to that SAME instance (exposed as
/// <see cref="AppPage.ViewModel"/>) rather than a second one, since AppPage still owns the keyboard
/// model (Escape, Down-into-the-grid) that the search box's key handling has to reach into. This class
/// owns what genuinely belongs to the window — the Mica fallback, close-to-tray, the caption-button
/// reservation, the rail's collapsed/expanded choice, swapping the page host's content between the two
/// rail entries, and kicking off the startup build + the one-time fit-to-content resize that follows it
/// (<see cref="InitializeAndFitAsync"/>) — the actual geometry math for the latter lives in
/// <c>WindowHost</c>, which owns every other piece of window placement too.
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

    // Guards WindowHost.FitToContent against running more than once per PROCESS. OnOpened re-enters on
    // every later un-hide (WindowHost.ShowAndActivate calls Show() again whenever the window was
    // hidden), which must NOT re-fit the window away from a resize the user made mid-session — see
    // InitializeAndFitAsync's own remarks.
    private bool _fitToContentDone;

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
        // NavRailExpanded has no settings-card counterpart to raise SettingsChanged for it specifically
        // (see the field's remarks in GeneralConfig), but the hamburger's tooltip/automation name is
        // still language-dependent, so this re-applies alongside the other two for the same reason they
        // do: AppLanguage/AppFonts carry no change notification of their own.
        _events.SettingsChanged += ApplyNavExpandedState;
        ApplyWindowFont();
        ApplyRailText();
        ApplyNavExpandedState();

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
        // Fire-and-forget (as BeginInitialize's own call always was): InitializeAndFitAsync's inner
        // await is on LauncherViewModel.InitializeAsync's memoized Task, which swallows its own
        // exceptions (see InitializeCoreAsync's catch), so there is nothing here that can escape unobserved.
        _ = InitializeAndFitAsync();
    }

    /// <summary>
    /// Await the startup build, THEN fit the window to what it actually built — never the other way
    /// around. AppPage's natural size is meaningless before its categories exist: InitializeAsync's
    /// ReloadAsync is an async disk scan (see AppPage.BeginInitialize's own remarks for why the build
    /// cannot start any earlier than OnOpened), so a fit attempted before this would measure the splash,
    /// not the menu.
    /// </summary>
    /// <remarks>
    /// Runs the FIT exactly once per PROCESS, not once per <c>Show()</c>: <see cref="_fitToContentDone"/>
    /// survives a later hide/tray-restore, which re-enters <see cref="OnOpened"/> (and therefore this
    /// method) but must not silently undo a resize the user made mid-session. <c>BeginInitialize</c>
    /// itself is still called on every entry — it is memoized, so every call after the first just awaits
    /// the already-completed Task — matching what the un-guarded, fire-and-forget call here did before
    /// this method existed.
    /// </remarks>
    private async Task InitializeAndFitAsync()
    {
        await _appPage.BeginInitialize();

        if (_fitToContentDone)
            return;
        _fitToContentDone = true;

        // Read straight from config, not NavRailPane's own live Width/Bounds — see
        // WindowHost.FitToContent's own <param> remarks on railWidth for why: a config value can never
        // be mid-animation.
        var expanded = App.Services.GetRequiredService<ConfigService>().Config.General.NavRailExpanded;
        (_host as WindowHost)?.FitToContent(_appPage, expanded ? 200.0 : 48.0);
    }

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyRailText;
        _events.SettingsChanged -= ApplyNavExpandedState;
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

        // Retry a focus request ConsumePendingFocus deferred because 设置 was showing when it arrived
        // (see that method's remarks) — the same "re-check what needs the visual tree on re-attach"
        // rule AppPage's pre-move version of this mechanism followed, just relocated to the one place
        // that now knows which page is active.
        if (page == ShellPage.App)
            Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
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
        var appsText = AppLanguage.Text("NavApps", lang);
        var settingsText = AppLanguage.Text("NavSettings", lang);

        NavAppsLabel.Text = appsText;
        NavSettingsLabel.Text = settingsText;

        // AutomationProperties.Name/ToolTip.Tip live on the ROW (the ListBoxItem), not on the label
        // TextBlock: Launcher.axaml's ".collapsed" rule hides NavAppsLabel/NavSettingsLabel entirely
        // (IsVisible=False) when the rail is collapsed, so an icon-only row needs an accessible name and
        // a tooltip that do not depend on that now-invisible label being there to read.
        NavAppsItem.SetValue(AutomationProperties.NameProperty, appsText);
        NavSettingsItem.SetValue(AutomationProperties.NameProperty, settingsText);
        ToolTip.SetTip(NavAppsItem, appsText);
        ToolTip.SetTip(NavSettingsItem, settingsText);
    }

    /// <summary>
    /// Apply the rail's current collapsed/expanded choice: the pane's width (animated by the
    /// <c>DoubleTransition</c> on <c>NavRailPane.Width</c> in MainWindow.axaml), the "collapsed" class
    /// that drives Launcher.axaml's icon-only layout, and the hamburger's own tooltip/automation name —
    /// which names the action pressing it performs NEXT (展开/Expand vs. 折叠/Collapse), so it must flip
    /// every time the state does; a static XAML value could only ever be half right. Reads live from
    /// config rather than a cached field, since <see cref="OnNavToggleClick"/> and this method are the
    /// only two places that ever need the value and neither benefits from a second copy that could drift.
    /// Called once from the constructor and again on every <see cref="AppEvents.SettingsChanged"/> (the
    /// hamburger's tooltip text is language-dependent, and AppLanguage has no change notification).
    /// </summary>
    private void ApplyNavExpandedState()
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        var expanded = cfg.Config.General.NavRailExpanded;

        NavRailPane.Width = expanded ? 200 : 48;
        NavRail.Classes.Set("collapsed", !expanded);

        var toggleText = AppLanguage.Text(expanded ? "NavCollapse" : "NavExpand", cfg.Config.General.Language);
        NavToggleButton.SetValue(AutomationProperties.NameProperty, toggleText);
        ToolTip.SetTip(NavToggleButton, toggleText);
    }

    /// <summary>
    /// The hamburger toggle. Persists straight through <see cref="ConfigService.SaveAsync"/> rather than
    /// <see cref="SettingsWrite.CommitAsync"/> — see <see cref="TanMenu.Core.Models.GeneralConfig.NavRailExpanded"/>'s
    /// remarks for the full justification: this is shell chrome the rail applies live and owns outright
    /// (the same category as <c>WindowMaximized</c>/<c>WindowLeft</c>/<c>WindowTop</c>, which
    /// <see cref="WindowHost.SavePlacement"/> already persists the same way), not a setting a
    /// 设置 card exposes. There is therefore no card to roll back or reload on a failed save, and no
    /// other subscriber that needs telling, so a failed save is logged and otherwise ignored — exactly
    /// like a failed placement flush — rather than surfaced as a blocking error dialog for what is a
    /// purely cosmetic layout choice.
    /// </summary>
    private async void OnNavToggleClick(object? sender, RoutedEventArgs e)
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        cfg.Config.General.NavRailExpanded = !cfg.Config.General.NavRailExpanded;
        ApplyNavExpandedState();

        try
        {
            await cfg.SaveAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to persist the nav rail expand/collapse choice");
        }
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
    /// this runs.</summary>
    private void OnLauncherViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LauncherViewModel.PendingFocus))
            return;

        Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Fix (Task 1 review, Important 3): a focus request that arrives while 设置 is the active page
    /// must NOT steal focus into a search box that filters a page the user cannot see —
    /// <c>ISettingsLauncher.OpenCore</c> navigates to 设置 and THEN calls
    /// <c>WindowHost.ShowAndActivate()</c>, which unconditionally raises <c>LauncherShown</c> and sets
    /// <c>PendingFocus</c> regardless of which page is showing (the tray's 选项 entry hits this path).
    /// Before the search box moved into this always-attached title strip, <c>AppPage</c> guarded the
    /// equivalent case with <c>IsAttachedToVisualTree()</c> — the whole page, search box included, was
    /// detached whenever 设置 was on screen, so a pending request there was simply invisible. The
    /// search box has no such attachment signal to reuse now, so the guard is asking the one thing that
    /// actually determines whether the user can see it: whether <see cref="_appPage"/> is the page
    /// <see cref="PageHost"/> is currently showing. Left SET otherwise; <see cref="ShowPage"/> retries
    /// this the next time the rail switches back to 应用, exactly the "re-check what needs the visual
    /// tree on re-attach" rule AppPage's own pre-move mechanism followed.
    /// </summary>
    private void ConsumePendingFocus()
    {
        if (!_appPage.ViewModel.PendingFocus)
            return;

        if (!ReferenceEquals(PageHost.Content, _appPage))
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
