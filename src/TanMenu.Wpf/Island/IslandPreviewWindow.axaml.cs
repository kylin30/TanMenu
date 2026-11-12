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
    private readonly IslandSamplingSession _sampling = new();
    private readonly IslandEffectGate _effects = new();
    private readonly List<IslandSampleRecord> _samples = [];
    private readonly object _sampleLock = new();
    private DoubleTransition? _progressTransition;
    private Timer? _sampler;
    private bool? _cpuKnown;
    private bool? _memoryKnown;
    private bool? _networkKnown;
    private bool _expanded;
    private bool _hooksAttached;
    private bool _regionQueued;
    private bool _overlayVisible;
    private bool _reducedMotion;
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
        PointerEntered += OnShapePointerEntered;
        PointerExited += OnShapePointerExited;
        _outline = IslandTrapezoidGeometry.Layout(0, Width, Height);
        DrawOutline();
    }

    static IslandPreviewWindow()
    {
        IslandProgressRegistration.Ensure();
    }

    internal int OutlineBuilds { get; private set; }

    internal IslandSampleRecord[] CopySamples()
    {
        lock (_sampleLock)
            return _samples.ToArray();
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
        lock (_sampleLock)
            _samples.Clear();
        _sampling.Start();
        _sampler = new Timer(_ => SampleOnce(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void StopSampling()
    {
        _sampler?.Dispose();
        _sampler = null;
        _sampling.Stop();
        _effects.CancelAll();
        RemoveHeightMotion(MemoryFill);
    }

    internal static void RemoveHeightMotion(Animatable target) => target.Transitions = null;

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

    internal void OnProgressChanged()
    {
        OutlineBuilds++;
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
        var showValues = ShapeProgress >= 0.65;
        CpuText.IsVisible = showValues;
        MemoryText.IsVisible = showValues;
        NetworkText.IsVisible = showValues;
    }

    private void SampleOnce()
    {
        try
        {
            if (!_sampling.TrySample(IslandWindowsCounters.Read, out var generation, out var frame))
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_sampling.IsCurrent(generation))
                    return;
                ApplyStatus(generation, frame);
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "灵动岛采样失败");
        }
    }

    private void ApplyStatus(int generation, IslandStatusFrame frame)
    {
        if (!_sampling.IsCurrent(generation))
            return;
        var started = _effects.Begin(frame, _reducedMotion);
        ApplySlot(CpuIcon, CpuText, frame.Cpu, ref _cpuKnown);
        if (ApplySlot(null, MemoryText, frame.Memory, ref _memoryKnown))
            MemoryFill.Background = new SolidColorBrush(frame.Memory.Known ? Color.Parse("#F5F5F5") : Color.Parse("#6A6A6E"));
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

        ApplySlot(ReceiveIcon, NetworkText, frame.Network, ref _networkKnown);
        if (ReceiveIcon.Fill != SendIcon.Fill)
            SendIcon.Fill = ReceiveIcon.Fill;
        if (started.Plan.StartCpuPulse)
            _ = Pulse(CpuIcon, frame.Cpu.Level, started.Generation);
        if (started.Plan.StartReceive)
            Nudge(ReceiveIcon, 3, started.Generation, receive: true);
        if (started.Plan.StartSend)
            Nudge(SendIcon, -3, started.Generation, send: true);
        Remember(generation, frame);
    }

    private void Remember(int generation, IslandStatusFrame frame)
    {
        lock (_sampleLock)
        {
            if (_samples.Count == 8)
                _samples.RemoveAt(0);
            _samples.Add(new IslandSampleRecord(
                _samples.Count + 1,
                generation,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                frame.Cpu.Known,
                frame.Cpu.Text,
                frame.Cpu.Level,
                frame.Memory.Known,
                frame.Memory.Text,
                frame.Network.Known,
                frame.Network.Text));
        }
    }

    private static bool ApplySlot(ShapePath? icon, TextBlock text, IslandSlotState slot, ref bool? seenKnown)
    {
        if (text.Text != slot.Text)
            text.Text = slot.Text;
        if (seenKnown == slot.Known)
            return false;
        seenKnown = slot.Known;
        var brush = new SolidColorBrush(slot.Known ? Color.Parse("#F5F5F5") : Color.Parse("#6A6A6E"));
        text.Foreground = brush;
        if (icon is not null)
            icon.Fill = brush;
        return true;
    }

    private async Task Pulse(ShapePath icon, double strength, int generation)
    {
        var token = _effects.Token;
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
            await animation.RunAsync(icon, token);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "灵动岛脉冲被打断");
        }
        finally
        {
            _effects.Finish(generation, cpu: true, receive: false, send: false);
        }
    }

    private void Nudge(ShapePath icon, double dy, int generation, bool receive = false, bool send = false)
    {
        var token = _effects.Token;
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
        _ = animation.RunAsync(transform, token).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() =>
                _effects.Finish(generation, cpu: false, receive, send)));
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

internal static class IslandProgressRegistration
{
    private static int _registered;

    public static int Count => Volatile.Read(ref _registered);

    public static void Ensure()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0)
            return;
        IslandPreviewWindow.ShapeProgressProperty.Changed.AddClassHandler<IslandPreviewWindow>((window, _) => window.OnProgressChanged());
    }
}
