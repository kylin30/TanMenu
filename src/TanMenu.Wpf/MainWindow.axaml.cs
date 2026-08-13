using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf;

/// <summary>
/// Windows 11-style single-window shell. The adaptive NavigationView owns application navigation;
/// settings are an in-window page and are applied as they change.
/// </summary>
public partial class MainWindow : FAAppWindow
{
    private const double NavigationContentDividerWidthDip = 1;
    private readonly IWindowHost _host;
    private readonly AppEvents _events;
    private bool _hidingAfterFlush;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;

        _host = App.Services.GetRequiredService<IWindowHost>();
        _events = App.Services.GetRequiredService<AppEvents>();

        LauncherPage.DataContext = LauncherContent.ViewModel;
        SearchBox.DataContext = LauncherContent.ViewModel;
        AppNavigation.SelectedItem = LauncherNavItem;

        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyNavigationText;
        _events.SettingsChanged += QueueRefitToContent;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        LauncherContent.ViewModel.PropertyChanged += OnLauncherViewModelPropertyChanged;
        LauncherContent.ViewModel.ContentMetricsChanged += QueueRefitToContent;
        LauncherContent.NoticeVisibilityChanged += QueueRefitToContent;
        AppNavigation.DisplayModeChanged += OnNavigationDisplayModeChanged;
        AppNavigation.PaneOpened += OnNavigationPaneLayoutChanged;
        AppNavigation.PaneClosed += OnNavigationPaneLayoutChanged;

        ApplyWindowFont();
        ApplyNavigationText();
        ShowLauncherPage();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateWindowBackdrop();
        ApplyNavigationText();
        _ = InitializeAndFitAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyNavigationText;
        _events.SettingsChanged -= QueueRefitToContent;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        LauncherContent.ViewModel.PropertyChanged -= OnLauncherViewModelPropertyChanged;
        LauncherContent.ViewModel.ContentMetricsChanged -= QueueRefitToContent;
        LauncherContent.NoticeVisibilityChanged -= QueueRefitToContent;
        AppNavigation.DisplayModeChanged -= OnNavigationDisplayModeChanged;
        AppNavigation.PaneOpened -= OnNavigationPaneLayoutChanged;
        AppNavigation.PaneClosed -= OnNavigationPaneLayoutChanged;
        LauncherContent.Dispose();
        SettingsContent.Dispose();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !LauncherPage.IsVisible)
            return;

        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            LauncherContent.ViewModel.PendingFocus = true;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            LauncherContent.ViewModel.HandleSearchEscape();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            if (!_hidingAfterFlush)
                _ = FlushAndHideAsync();
            return;
        }

        base.OnClosing(e);
    }

    public void ShowSettingsPage()
    {
        AppNavigation.SelectedItem = SettingsNavItem;
        SearchBox.IsVisible = false;
        LauncherPage.IsVisible = false;
        SettingsContent.IsVisible = true;
        QueuePageRefitToContent();
    }

    public void ShowLauncherPage()
    {
        AppNavigation.SelectedItem = LauncherNavItem;
        SearchBox.IsVisible = true;
        SettingsContent.IsVisible = false;
        LauncherPage.IsVisible = true;
        QueuePageRefitToContent();
    }

    private string CurrentLanguage =>
        App.Services.GetRequiredService<ConfigService>().Config.General.Language;

    private void ApplyNavigationText()
    {
        var lang = CurrentLanguage;
        TogglePaneNavItem.Content = AppLanguage.Text("NavTogglePane", lang);
        LauncherNavItem.Content = AppLanguage.Text("NavApps", lang);
        OpenRootNavItem.Content = AppLanguage.Text("MenuOpenRoot", lang);
        RefreshNavItem.Content = AppLanguage.Text("MenuRefresh", lang);
        SettingsNavItem.Content = AppLanguage.Text("NavSettings", lang);
        ExitNavItem.Content = AppLanguage.Text("NavExit", lang);
    }

    private void ApplyWindowFont()
    {
        var config = App.Services.GetRequiredService<ConfigService>();
        FontFamily = AppFonts.Resolve(config.Config.General.FontFamily);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => UpdateWindowBackdrop();

    private void UpdateWindowBackdrop()
    {
        if (IsWindows11 && ActualThemeVariant != FluentAvaloniaTheme.HighContrastTheme)
        {
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };
            return;
        }

        ClearValue(BackgroundProperty);
        ClearValue(TransparencyBackgroundFallbackProperty);
        ClearValue(TransparencyLevelHintProperty);
    }

    private async void OnNavigationSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        try
        {
            if (ReferenceEquals(e.SelectedItem, ExitNavItem))
            {
                if (SettingsContent.IsVisible)
                    await SettingsContent.FlushAsync();
                (Application.Current as App)?.ExitApp();
                return;
            }

            if (ReferenceEquals(e.SelectedItem, SettingsNavItem))
            {
                ShowSettingsPage();
                return;
            }

            if (SettingsContent.IsVisible)
                await SettingsContent.FlushAsync();

            ShowLauncherPage();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "切换应用页面失败");
        }
    }

    private void OnNavigationItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (ReferenceEquals(e.InvokedItemContainer, TogglePaneNavItem))
        {
            AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;
            return;
        }

        if (ReferenceEquals(e.InvokedItemContainer, OpenRootNavItem))
        {
            LauncherContent.ViewModel.OpenRoot();
            return;
        }

        if (ReferenceEquals(e.InvokedItemContainer, RefreshNavItem))
            _ = LauncherContent.ViewModel.RefreshAsync();
    }

    private void OnLauncherViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.PendingFocus))
            Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
    }

    private async Task InitializeAndFitAsync()
    {
        try
        {
            await LauncherContent.BeginInitialize();
            QueueRefitToContent();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "初始化应用并调整窗口大小失败");
        }
    }

    private void QueueRefitToContent() =>
        Dispatcher.UIThread.Post(() => RefitToContent(forcePageFit: false), DispatcherPriority.Loaded);

    private void QueuePageRefitToContent() =>
        Dispatcher.UIThread.Post(() => RefitToContent(forcePageFit: true), DispatcherPriority.Loaded);

    private void OnNavigationDisplayModeChanged(
        object? sender,
        FANavigationViewDisplayModeChangedEventArgs e) => QueueRefitToContent();

    private void OnNavigationPaneLayoutChanged(FANavigationView sender, EventArgs e) =>
        QueueRefitToContent();

    private double NavigationChromeWidthDip =>
        (AppNavigation.DisplayMode == FANavigationViewDisplayMode.Expanded && AppNavigation.IsPaneOpen
            ? AppNavigation.OpenPaneLength
            : AppNavigation.CompactPaneLength) + NavigationContentDividerWidthDip;

    private void RefitToContent(bool forcePageFit)
    {
        if (!IsVisible || _host is not WindowHost host)
            return;

        Control activePage = SettingsContent.IsVisible ? SettingsContent : LauncherPage;
        host.FitToContent(activePage, NavigationChromeWidthDip, forcePageFit);
    }

    private void ConsumePendingFocus()
    {
        if (!LauncherContent.ViewModel.PendingFocus || !LauncherPage.IsVisible)
            return;

        LauncherContent.ViewModel.PendingFocus = false;
        try { SearchBox.Focus(); }
        catch { /* focus failure is non-fatal */ }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                LauncherContent.ViewModel.HandleSearchEnter();
                e.Handled = true;
                break;
            case Key.Escape:
                LauncherContent.ViewModel.HandleSearchEscape();
                e.Handled = true;
                break;
            case Key.Down:
                e.Handled = LauncherContent.FocusFirstTile();
                break;
        }
    }

    private async Task FlushAndHideAsync()
    {
        _hidingAfterFlush = true;
        try
        {
            await SettingsContent.FlushAsync();
            _host.Hide();
        }
        finally
        {
            _hidingAfterFlush = false;
        }
    }
}
