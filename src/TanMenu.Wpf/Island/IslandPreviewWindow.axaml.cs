using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Island;

/// <summary>
/// Fixed 320×72 DIP transparent host. Hover grows the capsule inside the host; the native window
/// size stays put. The leave timer runs only for the 80 ms delay and is stopped afterwards.
/// </summary>
public partial class IslandPreviewWindow : Window
{
    private readonly DispatcherTimer _leaveTimer;
    private readonly IslandRegionCoordinator _regions = new();
    private DoubleTransition? _widthTransition;
    private DoubleTransition? _heightTransition;
    private CornerRadiusTransition? _radiusTransition;
    private bool _expanded;
    private bool _hooksAttached;
    private bool _regionQueued;
    private bool _overlayVisible;
    private double _seenScale = double.NaN;
    private Win32Properties.CustomWindowStylesCallback? _styles;
    private Win32Properties.CustomWndProcHookCallback? _hook;

    public event EventHandler? TransparencyUnavailable;
    public event EventHandler? HitRegionFailed;

    /// <summary>Current animated capsule width. The Win32 probe reads this after real pointer input.</summary>
    public double PreviewCapsuleWidth => Capsule.Width;

    public IslandPreviewWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IslandMetrics.LeaveDelayMs) };
        _leaveTimer.Tick += OnLeaveDelayElapsed;
    }

    public void AttachPlatformHooks()
    {
        if (_hooksAttached)
            return;
        _hooksAttached = true;
        IslandWndProc.AddHooks(this, HitTestDip, out _styles, out _hook);
        Capsule.PropertyChanged += OnCapsulePropertyChanged;
        ScalingChanged += OnScalingChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>Queues one region rebuild after the current layout/scale change, without a timer.</summary>
    public void RequestHitRegion() => QueueRegion();

    /// <summary>Installs the capsule region before the window is shown. False means the rectangular host must stay hidden.</summary>
    public bool TryApplyHitRegion()
    {
        var scale = RenderScaling;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var kind = _regions.Apply(
            Width, Height, Capsule.Width, Capsule.Height, Capsule.CornerRadius.TopLeft, scale,
            region => IslandWndProc.TryApplyDeviceRegion(handle, region));
        if (IslandRegionCoordinator.ShouldCloseOverlay(kind, _overlayVisible))
        {
            Serilog.Log.Error("灵动岛命中区域设置失败 ({Kind})，关闭预览", kind);
            HitRegionFailed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        return kind is IslandRegionApplyKind.Applied or IslandRegionApplyKind.Unchanged;
    }

    public void StopIdleClock() => _leaveTimer.Stop();

    protected override void OnOpened(EventArgs e)
    {
        _overlayVisible = true;
        base.OnOpened(e);
        Dispatcher.UIThread.Post(VerifyPlatform, DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopIdleClock();
        DetachPlatformHooks();
        base.OnClosed(e);
    }

    private void VerifyPlatform()
    {
        TryApplyHitRegion();
        if (ActualTransparencyLevel == WindowTransparencyLevel.Transparent)
            return;

        Serilog.Log.Error("灵动岛预览透明度不可用: {Level}", ActualTransparencyLevel);
        TransparencyUnavailable?.Invoke(this, EventArgs.Empty);
    }

    private void OnCapsulePointerEntered(object? sender, PointerEventArgs e)
    {
        _leaveTimer.Stop();
        SetExpanded(true);
    }

    private void OnCapsulePointerExited(object? sender, PointerEventArgs e)
    {
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private void OnLeaveDelayElapsed(object? sender, EventArgs e)
    {
        _leaveTimer.Stop();
        SetExpanded(false);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;
        _expanded = expanded;

        var duration = TimeSpan.FromMilliseconds(
            expanded ? IslandMetrics.ExpandDurationMs : IslandMetrics.CollapseDurationMs);
        EnsureCapsuleTransitions(duration);
        Capsule.Width = expanded ? IslandMetrics.ExpandedWidth : IslandMetrics.CollapsedWidth;
        Capsule.Height = expanded ? IslandMetrics.ExpandedHeight : IslandMetrics.CollapsedHeight;
        Capsule.CornerRadius = new CornerRadius(
            expanded ? IslandMetrics.ExpandedRadius : IslandMetrics.CollapsedRadius);
    }

    private bool HitTestDip(double x, double y)
    {
        var capsule = IslandCapsuleGeometry.Layout(
            Width, Height, Capsule.Width, Capsule.Height, Capsule.CornerRadius.TopLeft);
        return IslandCapsuleGeometry.Contains(capsule, x, y);
    }

    private void EnsureCapsuleTransitions(TimeSpan duration)
    {
        var easing = new CubicEaseOut();
        if (_widthTransition is null || _heightTransition is null || _radiusTransition is null)
        {
            _widthTransition = new DoubleTransition { Property = Layoutable.WidthProperty, Easing = easing };
            _heightTransition = new DoubleTransition { Property = Layoutable.HeightProperty, Easing = easing };
            _radiusTransition = new CornerRadiusTransition { Property = Border.CornerRadiusProperty, Easing = easing };
            Capsule.Transitions = new Transitions { _widthTransition, _heightTransition, _radiusTransition };
        }

        _widthTransition.Duration = duration;
        _heightTransition.Duration = duration;
        _radiusTransition.Duration = duration;
    }

    private void OnScalingChanged(object? sender, EventArgs e) => QueueRegion();

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var scale = RenderScaling;
        if (!double.IsNaN(_seenScale) && Math.Abs(scale - _seenScale) <= 0.0001)
            return;
        _seenScale = scale;
        QueueRegion();
    }

    private void OnCapsulePropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Layoutable.WidthProperty &&
            e.Property != Layoutable.HeightProperty &&
            e.Property != Border.CornerRadiusProperty)
            return;
        QueueRegion();
    }

    private void QueueRegion()
    {
        if (_regionQueued || !_hooksAttached)
            return;
        _regionQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _regionQueued = false;
            if (_hooksAttached)
                TryApplyHitRegion();
        }, DispatcherPriority.Render);
    }

    private void DetachPlatformHooks()
    {
        if (!_hooksAttached)
            return;
        _hooksAttached = false;
        _overlayVisible = false;
        Capsule.PropertyChanged -= OnCapsulePropertyChanged;
        ScalingChanged -= OnScalingChanged;
        LayoutUpdated -= OnLayoutUpdated;
        IslandWndProc.RemoveHooks(this, _styles, _hook);
        _styles = null;
        _hook = null;
    }
}
