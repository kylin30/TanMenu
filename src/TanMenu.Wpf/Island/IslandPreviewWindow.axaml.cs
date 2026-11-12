using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Island;

/// <summary>
/// Fixed 320×72 DIP host. The concave trapezoid animates inside it; the native window does not resize.
/// Status sampling is a one-second background timer and stops when the window hides.
/// </summary>
public partial class IslandPreviewWindow : Window
{
    public static readonly StyledProperty<double> ShapeProgressProperty =
        AvaloniaProperty.Register<IslandPreviewWindow, double>(nameof(ShapeProgress));

    private readonly DispatcherTimer _leaveTimer;
    private readonly IslandRegionCoordinator _regions = new();
    private readonly IslandStatusClock _status = new();
    private DoubleTransition? _progressTransition;
    private Timer? _sampler;
    private long _sampledAt;
    private bool _expanded;
    private bool _hooksAttached;
    private bool _regionQueued;
    private bool _overlayVisible;
    private bool _reducedMotion;
    private bool _pulseRunning;
    private double _seenScale = double.NaN;
    private IslandOutline _outline;
    private Win32Properties.CustomWindowStylesCallback? _styles;
    private Win32Properties.CustomWndProcHookCallback? _hook;

    public event EventHandler? TransparencyUnavailable;
    public event EventHandler? HitRegionFailed;

    public double ShapeProgress
    {
        get => GetValue(ShapeProgressProperty);
        set => SetValue(ShapeProgressProperty, value);
    }

    /// <summary>Bottom width of the current outline. The Win32 probe reads this after real pointer input.</summary>
    public double PreviewCapsuleWidth => _outline.BottomWidth;

    public double PreviewShapeProgress => ShapeProgress;

    public IslandPreviewWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IslandMetrics.LeaveDelayMs) };
        _leaveTimer.Tick += OnLeaveDelayElapsed;
        _outline = IslandTrapezoidGeometry.Layout(0, Width, Height);
        ShapeProgressProperty.Changed.AddClassHandler<IslandPreviewWindow>((window, _) => window.OnProgressChanged());
        DrawOutline();
    }

    public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

    public void AttachPlatformHooks()
    {
        if (_hooksAttached)
            return;
        _hooksAttached = true;
        IslandWndProc.AddHooks(this, HitTestDip, out _styles, out _hook);
        ScalingChanged += OnScalingChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    public void RequestHitRegion() => QueueRegion();

    public bool TryApplyHitRegion()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var kind = _regions.Apply(ShapeProgress, Width, Height, RenderScaling, region => IslandWndProc.TryApplyDeviceRegion(handle, region));
        if (IslandRegionCoordinator.ShouldCloseOverlay(kind, _overlayVisible))
        {
            Serilog.Log.Error("灵动岛命中区域设置失败 ({Kind})，关闭预览", kind);
            HitRegionFailed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        return kind is IslandRegionApplyKind.Applied or IslandRegionApplyKind.Unchanged;
    }

    public void StartSampling()
    {
        StopSampling();
        _status.Start();
        _sampledAt = Environment.TickCount64;
        _sampler = new Timer(_ => SampleOnce(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void StopSampling()
    {
        _sampler?.Dispose();
        _sampler = null;
        _status.Stop();
        _pulseRunning = false;
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
        StopSampling();
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

    private void OnShapePointerEntered(object? sender, PointerEventArgs e)
    {
        _leaveTimer.Stop();
        SetExpanded(true);
    }

    private void OnShapePointerExited(object? sender, PointerEventArgs e)
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
        if (_expanded == expanded && Math.Abs(ShapeProgress - (expanded ? 1 : 0)) < 0.001)
            return;
        _expanded = expanded;
        var motion = IslandMotionPlan.Toward(ShapeProgress, expanded, _reducedMotion);
        EnsureProgressTransition(TimeSpan.FromMilliseconds(motion.DurationMs));
        ShapeProgress = motion.Target;
    }

    private void EnsureProgressTransition(TimeSpan duration)
    {
        if (_progressTransition is null)
        {
            _progressTransition = new DoubleTransition
            {
                Property = ShapeProgressProperty,
                Easing = new CubicEaseOut(),
            };
            Transitions = [_progressTransition];
        }

        _progressTransition.Duration = duration;
    }

    private bool HitTestDip(double x, double y) => IslandTrapezoidGeometry.Contains(_outline, x, y);

    private void OnProgressChanged()
    {
        DrawOutline();
        QueueRegion();
    }

    private void DrawOutline()
    {
        _outline = IslandTrapezoidGeometry.Layout(ShapeProgress, Width, Height);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var points = _outline.Points;
            if (points.Length == 0)
                return;
            context.BeginFigure(new Point(points[0].X, points[0].Y), true);
            for (var i = 1; i < points.Length; i++)
                context.LineTo(new Point(points[i].X, points[i].Y));
            context.EndFigure(true);
        }

        Shape.Data = geometry;
        var offsetX = (Width - _outline.BottomWidth) / 2;
        var offsetY = Height - _outline.Height;
        Slots.Width = _outline.BottomWidth;
        Slots.Height = _outline.Height;
        Slots.Margin = new Thickness(0, offsetY, 0, 0);
        CpuText.Opacity = ShapeProgress;
        MemoryText.Opacity = ShapeProgress;
        NetworkText.Opacity = ShapeProgress;
    }

    private void SampleOnce()
    {
        var now = Environment.TickCount64;
        var elapsed = Math.Max(0, now - _sampledAt) / 1000d;
        _sampledAt = now;
        var frame = _status.Tick(IslandWindowsCounters.Read(), elapsed);
        if (frame is not { } status)
            return;
        Dispatcher.UIThread.Post(() => ApplyStatus(status));
    }

    private void ApplyStatus(IslandStatusFrame frame)
    {
        ApplySlot(CpuIcon, CpuText, frame.Cpu, animatePulse: true);
        ApplySlot(null, MemoryText, frame.Memory, animatePulse: false);
        var memoryHeight = 2 + 8 * frame.Memory.Level;
        if (frame.Memory.Known && frame.Memory.Fill && !_reducedMotion)
        {
            MemoryFill.Transitions = [new DoubleTransition { Property = Avalonia.Layout.Layoutable.HeightProperty, Duration = TimeSpan.FromMilliseconds(160) }];
            MemoryFill.Height = memoryHeight;
        }
        else
        {
            MemoryFill.Transitions = null;
            MemoryFill.Height = frame.Memory.Known ? memoryHeight : 2;
        }

        ApplySlot(ReceiveIcon, NetworkText, frame.Network, animatePulse: false);
        SendIcon.Opacity = frame.Network.Known ? 1 : 0.45;
        if (!_reducedMotion && !_pulseRunning && frame.Network.Receive)
            Nudge(ReceiveIcon, 3);
        if (!_reducedMotion && frame.Network.Send)
            Nudge(SendIcon, -3);
    }

    private void ApplySlot(ShapePath? icon, TextBlock text, IslandSlotState slot, bool animatePulse)
    {
        text.Text = slot.Text;
        var brush = new SolidColorBrush(slot.Known ? Color.Parse("#5EEAD4") : Color.Parse("#8E8E93"));
        if (icon is not null)
        {
            icon.Fill = brush;
            icon.Opacity = slot.Known ? 1 : 0.45;
        }

        if (animatePulse && slot.Pulse && !_reducedMotion && !_pulseRunning)
            _ = Pulse(icon!, slot.Level);
    }

    private async Task Pulse(ShapePath icon, double strength)
    {
        _pulseRunning = true;
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(280),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(0.45), Setters = { new Setter(OpacityProperty, 1 - 0.45 * strength) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
                },
            };
            await animation.RunAsync(icon);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "灵动岛脉冲被打断");
        }
        finally
        {
            _pulseRunning = false;
        }
    }

    private static void Nudge(ShapePath icon, double dy)
    {
        var transform = new TranslateTransform();
        icon.RenderTransform = transform;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
                new KeyFrame { Cue = new Cue(0.4), Setters = { new Setter(TranslateTransform.YProperty, dy) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        _ = animation.RunAsync(transform);
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
        ScalingChanged -= OnScalingChanged;
        LayoutUpdated -= OnLayoutUpdated;
        IslandWndProc.RemoveHooks(this, _styles, _hook);
        _styles = null;
        _hook = null;
    }
}
