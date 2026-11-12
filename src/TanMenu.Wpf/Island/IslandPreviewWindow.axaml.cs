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
    private bool _expanded;
    private bool _hooksAttached;
    private Win32Properties.CustomWindowStylesCallback? _styles;
    private Win32Properties.CustomWndProcHookCallback? _hook;

    public event EventHandler? TransparencyUnavailable;

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
        UpdateHitRegion();
    }

    public void StopIdleClock() => _leaveTimer.Stop();

    protected override void OnOpened(EventArgs e)
    {
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
        UpdateHitRegion();
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
        var easing = new CubicEaseOut();
        Capsule.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = duration,
                Easing = easing,
            },
            new DoubleTransition
            {
                Property = Layoutable.HeightProperty,
                Duration = duration,
                Easing = easing,
            },
            new CornerRadiusTransition
            {
                Property = Border.CornerRadiusProperty,
                Duration = duration,
                Easing = easing,
            },
        };
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

    private void OnCapsulePropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Layoutable.WidthProperty &&
            e.Property != Layoutable.HeightProperty &&
            e.Property != Border.CornerRadiusProperty)
            return;
        UpdateHitRegion();
    }

    private void UpdateHitRegion()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        IslandWndProc.SetCapsuleRegion(
            handle,
            Width,
            Height,
            Capsule.Width,
            Capsule.Height,
            Capsule.CornerRadius.TopLeft,
            RenderScaling);
    }

    private void DetachPlatformHooks()
    {
        if (!_hooksAttached)
            return;
        _hooksAttached = false;
        Capsule.PropertyChanged -= OnCapsulePropertyChanged;
        IslandWndProc.RemoveHooks(this, _styles, _hook);
        _styles = null;
        _hook = null;
        IslandWndProc.ClearRegion(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }
}
