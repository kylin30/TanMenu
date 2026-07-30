using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf;

/// <summary>
/// The launcher popup. All state lives in <see cref="LauncherViewModel"/>; this class is the thin
/// view layer: it wires clicks and keys to the view model, performs the one-shot reveal, and owns the
/// deferred search-box focus (both of which need the visual tree, so neither belongs in the VM).
/// </summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly IWindowHost _host;

    // One-shot latch: the reveal happens on the first layout pass in which the REAL content exists
    // (the menu, or the empty state after a failed build) — never on the loading splash.
    private bool _revealed;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        _viewModel = LauncherViewModel.Create(App.Services);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // The host owns show/hide/size/place and needs the window once its HWND exists. The DI
        // registration deliberately hands out the same instance for IWindowHost and WindowHost.
        (_host as WindowHost)?.Attach(this);

        // The build runs on a background STA worker; the window stays fully transparent until it
        // finishes and the reveal below runs.
        _ = _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Reveal + deferred focus
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LauncherViewModel.LoadedOnce)
            or nameof(LauncherViewModel.HasGroups)
            or nameof(LauncherViewModel.PendingFocus)))
            return;

        // Loaded priority sits one step below the priority Avalonia runs layout at, so by the time
        // this callback fires the property change has already been measured and arranged — i.e. the
        // window has its final desired size and the search box exists if it is going to.
        Dispatcher.UIThread.Post(ConsumePendingViewWork, DispatcherPriority.Loaded);
    }

    private void ConsumePendingViewWork()
    {
        if (!_revealed && _viewModel.LoadedOnce)
        {
            _revealed = true;
            // Size + place happen inside, BEFORE the window becomes visually present, so a cold start
            // never flashes an unsized rectangle at screen top-left.
            _host.ShowAndActivate();
        }

        // The search box lives inside the groups branch, so on a cold start it isn't rendered until
        // the build completes. Focusing here covers that race. The flag is deliberately left set when
        // there are no groups, so a later build that produces some still focuses it.
        if (_viewModel.PendingFocus && _viewModel.HasGroups)
        {
            _viewModel.PendingFocus = false;
            try
            {
                SearchBox.Focus();
            }
            catch
            {
                /* focus is a nicety; never let it take the reveal down */
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Title bar
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // Minimize and Close are the same action: hide. The launcher is shown/hidden, never closed.
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => _viewModel.HideWindow();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => _viewModel.HideWindow();

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Menu bar
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => _viewModel.OpenRoot();

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => _viewModel.OpenSettings();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _viewModel.RefreshAsync();

    private void OnAboutClick(object? sender, RoutedEventArgs e) => _viewModel.OpenAbout();

    private void OnExitClick(object? sender, RoutedEventArgs e) => _viewModel.ExitApp();

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Search box
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Enter and Escape, handled ONLY while the search box has focus — there is no
    /// window-level key handler, and deliberately no arrow-key navigation, no Tab handling and no
    /// selection cursor. The launcher has no concept of a "current item".</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.HandleSearchEnter();
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.HandleSearchEscape();
                e.Handled = true;
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Items, groups, update notice, overlays
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MenuItemVm item)
            return;
        _viewModel.PlayClickSound();
        _viewModel.LaunchItem(item);
    }

    private void OnItemPointerEntered(object? sender, PointerEventArgs e)
    {
        // Disabled items make no sound. (A disabled control gets no pointer input anyway; this keeps
        // the rule explicit rather than relying on that.)
        if ((sender as Control)?.DataContext is MenuItemVm { IsDisabled: false })
            _viewModel.PlayHoverSound();
    }

    private void OnGroupTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        // Primary button only. PointerPressed fires for EVERY button, so without this guard a
        // right-click on a group's title opened Explorer — the CSS build's click handler was
        // left-button-only and had no context menu of its own to shadow.
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (control.DataContext is MenuGroupVm group)
            _viewModel.OpenGroupFolder(group);
    }

    private void OnUpdateNoticeClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.HandleUpdateNoticeAsync();

    private void OnToastPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.DismissToast();
        e.Handled = true;
    }

    private void OnAboutPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CloseAbout();
        e.Handled = true;
    }
}
