using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf;

/// <summary>
/// HeroUI-inspired application shell. Navigation and content live in one stable AppLayout so changing
/// pages never changes the window's dimensions or moves the user's pointer target.
/// </summary>
public partial class MainWindow : FAAppWindow
{
    private const double LauncherChromeWidthDip = 64;
    private static readonly IBrush WindowBackgroundBrush =
        new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

    private readonly WindowHost _host;
    private readonly AppEvents _events;
    private bool _hidingAfterFlush;
    private bool _launcherInitialized;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 60;

        _host = App.Services.GetRequiredService<WindowHost>();
        _events = App.Services.GetRequiredService<AppEvents>();

        LauncherPage.DataContext = LauncherContent.ViewModel;
        SearchBox.DataContext = LauncherContent.ViewModel;

        _events.SettingsChanged += ApplyWindowFont;
        _events.SettingsChanged += ApplyNavigationText;
        _events.LauncherPageRequested += ShowLauncherPage;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        LauncherContent.ViewModel.PropertyChanged += OnLauncherViewModelPropertyChanged;
        LauncherContent.ViewModel.ContentMetricsChanged += OnLauncherContentMetricsChanged;
        LauncherContent.NoticeVisibilityChanged += OnLauncherContentMetricsChanged;

        ApplyWindowFont();
        ApplyNavigationText();
        ShowLauncherPage();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateWindowBackdrop();
        ApplyNavigationText();
        _ = InitializeLauncherAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyWindowFont;
        _events.SettingsChanged -= ApplyNavigationText;
        _events.LauncherPageRequested -= ShowLauncherPage;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        LauncherContent.ViewModel.PropertyChanged -= OnLauncherViewModelPropertyChanged;
        LauncherContent.ViewModel.ContentMetricsChanged -= OnLauncherContentMetricsChanged;
        LauncherContent.NoticeVisibilityChanged -= OnLauncherContentMetricsChanged;
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
        LauncherTitleCommands.IsVisible = false;
        TitleBarHost.Background = Brushes.Transparent;
        Grid.SetRow(ContentHost, 0);
        Grid.SetRowSpan(ContentHost, 2);
        LauncherPage.IsVisible = false;
        SettingsContent.IsVisible = true;
        SetActiveNavigation(settings: true);
    }

    public void ShowLauncherPage()
    {
        if (SettingsContent.IsVisible)
            _ = FlushSettingsSafelyAsync();

        LauncherTitleCommands.IsVisible = true;
        TitleBarHost.Background = WindowBackgroundBrush;
        Grid.SetRow(ContentHost, 1);
        Grid.SetRowSpan(ContentHost, 1);
        SettingsContent.IsVisible = false;
        LauncherPage.IsVisible = true;
        SetActiveNavigation(settings: false);
    }

    private string CurrentLanguage =>
        App.Services.GetRequiredService<ConfigService>().Config.General.Language;

    private void ApplyNavigationText()
    {
        var lang = CurrentLanguage;
        ApplyNavigationLabel(LauncherNavButton, AppLanguage.Text("NavApps", lang));
        ApplyNavigationLabel(OpenRootNavButton, AppLanguage.Text("MenuOpenRoot", lang));
        ApplyNavigationLabel(RefreshNavButton, AppLanguage.Text("MenuRefresh", lang));
        ApplyNavigationLabel(SettingsNavButton, AppLanguage.Text("NavSettings", lang));
        ApplyNavigationLabel(ExitNavButton, AppLanguage.Text("NavExit", lang));
    }

    private static void ApplyNavigationLabel(Control control, string text)
    {
        control.SetValue(ToolTip.TipProperty, text);
        control.SetValue(AutomationProperties.NameProperty, text);
    }

    private void ApplyWindowFont()
    {
        var config = App.Services.GetRequiredService<ConfigService>();
        FontFamily = AppFonts.Resolve(config.Config.General.FontFamily);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => UpdateWindowBackdrop();

    private void UpdateWindowBackdrop()
    {
        Background = WindowBackgroundBrush;
        ClearValue(TransparencyBackgroundFallbackProperty);
        ClearValue(TransparencyLevelHintProperty);
    }

    private void OnLauncherNavClick(object? sender, RoutedEventArgs e) => ShowLauncherPage();

    private void OnSettingsNavClick(object? sender, RoutedEventArgs e) => ShowSettingsPage();

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) =>
        LauncherContent.ViewModel.OpenRoot();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) =>
        _ = LauncherContent.ViewModel.RefreshAsync();

    private async void OnExitClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SettingsContent.FlushAsync();
            (Application.Current as App)?.ExitApp();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "退出应用失败");
        }
    }

    private void SetActiveNavigation(bool settings)
    {
        LauncherNavButton.Classes.Set("is-active", !settings);
        SettingsNavButton.Classes.Set("is-active", settings);
    }

    private void OnLauncherViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.PendingFocus))
            Dispatcher.UIThread.Post(ConsumePendingFocus, DispatcherPriority.Loaded);
    }

    private async Task InitializeLauncherAsync()
    {
        try
        {
            await LauncherContent.BeginInitialize();
            _launcherInitialized = true;
            QueueLauncherFit();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "初始化应用失败");
        }
    }

    private void OnLauncherContentMetricsChanged() => QueueLauncherFit();

    private void QueueLauncherFit()
    {
        if (!_launcherInitialized || !LauncherPage.IsVisible)
            return;

        Dispatcher.UIThread.Post(RefitLauncherToContent, DispatcherPriority.Loaded);
    }

    private void RefitLauncherToContent()
    {
        if (!LauncherPage.IsVisible)
            return;

        _host.FitToContent(LauncherContent, LauncherChromeWidthDip);
    }

    private void ConsumePendingFocus()
    {
        if (!LauncherContent.ViewModel.PendingFocus || !LauncherPage.IsVisible)
            return;

        LauncherContent.ViewModel.PendingFocus = false;
        try { SearchBox.Focus(); }
        catch { /* Focus failure is non-fatal. */ }
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

    private async Task FlushSettingsSafelyAsync()
    {
        try
        {
            await SettingsContent.FlushAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "保存设置失败");
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
