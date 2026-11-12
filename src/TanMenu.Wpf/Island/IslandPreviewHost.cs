using Avalonia;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Island;

/// <summary>
/// Creates one non-activating preview window on the primary screen. Screen changes reposition it.
/// Nothing here polls, and the window is destroyed on hide and on process exit.
/// </summary>
public sealed class IslandPreviewHost : IIslandPreviewController
{
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly SettingsDraft? _draft;
    private readonly Func<IslandPreviewWindow> _create;
    private IslandPreviewWindow? _window;
    private int _disposed;

    /// <summary>A missing primary screen hides the overlay and keeps the window so the next display
    /// change can place it again. Destroying it would drop the saved preview for the rest of the session.</summary>
    internal static bool DestroyOverlayOnScreenPlaceFailure => false;

    internal IslandPreviewWindow? CurrentWindow => _window;

    internal IslandPreviewHost(ConfigService config, AppEvents events, SettingsDraft draft)
        : this(config, events, static () => new IslandPreviewWindow(), draft)
    {
    }

    internal IslandPreviewHost(ConfigService config, AppEvents events, Func<IslandPreviewWindow> create, SettingsDraft? draft = null)
    {
        _config = config;
        _events = events;
        _draft = draft;
        _create = create;
        _events.SettingsChanged += OnSettingsChanged;
    }

    public void Show()
    {
        if (Volatile.Read(ref _disposed) != 0 || _window is not null)
            return;

        IslandPreviewWindow? window = null;
        try
        {
            window = _create();
            if (!TryPlace(window))
            {
                SafeClose(window);
                return;
            }

            window.TransparencyUnavailable += OnTransparencyUnavailable;
            window.HitRegionFailed += OnHitRegionFailed;
            window.Screens.Changed += OnScreensChanged;
            window.AttachPlatformHooks();
            if (!window.TryApplyHitRegion())
            {
                Serilog.Log.Error("灵动岛命中区域未能按胶囊设置，不显示预览");
                SafeClose(window);
                return;
            }

            _window = window;
            window = null;
            _window.Show();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "创建灵动岛预览失败");
            if (window is not null)
                SafeClose(window);
            Hide();
        }
    }

    public void Hide()
    {
        var window = _window;
        _window = null;
        if (window is null)
            return;

        window.TransparencyUnavailable -= OnTransparencyUnavailable;
        window.HitRegionFailed -= OnHitRegionFailed;
        window.Screens.Changed -= OnScreensChanged;
        SafeClose(window);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _events.SettingsChanged -= OnSettingsChanged;
        Hide();
    }

    private void OnSettingsChanged()
    {
        if (Volatile.Read(ref _disposed) != 0 || App.IsExiting)
        {
            Hide();
            return;
        }

        if (PreviewEnabled())
            Show();
        else
            Hide();
    }

    /// <summary>A pending settings edit wins over the last saved value, so a settings event during the
    /// save debounce cannot put the overlay back to the old switch.</summary>
    private bool PreviewEnabled() =>
        _draft is null
            ? _config.Config.General.ShowIslandPreview
            : _draft.Working.General.ShowIslandPreview;

    private void OnTransparencyUnavailable(object? sender, EventArgs e)
    {
        Serilog.Log.Error("灵动岛预览因透明度不可用而关闭");
        Hide();
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (_window is null || Volatile.Read(ref _disposed) != 0)
            return;
        if (!TryPlace(_window))
        {
            Serilog.Log.Error("主屏暂时不可用，隐藏灵动岛预览并等待下一次显示变化");
            if (DestroyOverlayOnScreenPlaceFailure)
                Hide();
            else
                _window.Hide();
            return;
        }

        if (!_window.IsVisible)
            _window.Show();
        _window.RequestHitRegion();
    }

    private void OnHitRegionFailed(object? sender, EventArgs e)
    {
        Serilog.Log.Error("灵动岛命中区域在显示后设置失败，关闭预览");
        Hide();
    }

    private bool TryPlace(IslandPreviewWindow window)
    {
        var screen = window.Screens?.Primary;
        if (screen is null)
        {
            Serilog.Log.Error("主屏不可用，不显示灵动岛预览");
            return false;
        }

        var (autoHide, thickness) = Win32TaskbarQuery.Query(screen.Scaling);
        var metrics = IslandPlacement.FromDevicePixels(
            screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height,
            screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height,
            screen.Scaling,
            autoHide,
            thickness);
        var placement = IslandPlacement.ResolveWindow(metrics);
        if (placement is null)
        {
            Serilog.Log.Error("无法计算灵动岛预览位置");
            return false;
        }

        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var (x, y) = IslandPlacement.ToDevicePosition(placement.Value, scale);
        window.Width = placement.Value.Width;
        window.Height = placement.Value.Height;
        window.Position = new PixelPoint(x, y);
        return true;
    }

    private static void SafeClose(IslandPreviewWindow window)
    {
        try
        {
            window.StopIdleClock();
            window.Close();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "关闭灵动岛预览窗口失败");
        }
    }
}
