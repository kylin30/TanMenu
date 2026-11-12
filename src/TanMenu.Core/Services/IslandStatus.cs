namespace TanMenu.Core.Services;

public readonly record struct IslandNicSample(string Id, ulong BytesIn, ulong BytesOut);

public readonly record struct IslandRawSample(
    bool CpuOk,
    ulong Idle,
    ulong Kernel,
    ulong User,
    bool MemoryOk,
    double MemoryUsed,
    bool NetworkOk,
    IslandNicSample[] Nics);

public readonly record struct IslandSampleRecord(
    int Index,
    int Generation,
    long UtcMs,
    bool CpuKnown,
    string CpuText,
    double CpuLevel,
    bool MemoryKnown,
    string MemoryText,
    bool NetworkKnown,
    string NetworkText);

public readonly record struct IslandSlotState(
    bool Known,
    string Text,
    double Level,
    bool Pulse,
    bool Fill,
    bool Receive,
    bool Send);

public readonly record struct IslandStatusFrame(
    IslandSlotState Cpu,
    IslandSlotState Memory,
    IslandSlotState Network);

public static class IslandStatusReducer
{
    public static IslandStatusFrame Reduce(IslandRawSample? previous, IslandRawSample current, double elapsedSeconds)
    {
        return new IslandStatusFrame(
            Cpu(previous, current),
            Memory(previous, current),
            Network(previous, current, elapsedSeconds));
    }

    private static IslandSlotState Cpu(IslandRawSample? previous, IslandRawSample current)
    {
        if (previous is not { CpuOk: true } prior || !current.CpuOk)
            return Unknown();

        var idle = Delta(current.Idle, prior.Idle);
        var total = Delta(current.Kernel + current.User, prior.Kernel + prior.User);
        if (total <= 0 || idle > total)
            return Unknown();

        var level = (total - idle) / (double)total;
        level = Math.Clamp(level, 0, 1);
        return new IslandSlotState(true, $"{Math.Round(level * 100):0}%", level, level >= 0.02, false, false, false);
    }

    private static IslandSlotState Memory(IslandRawSample? previous, IslandRawSample current)
    {
        if (!current.MemoryOk || !double.IsFinite(current.MemoryUsed))
            return Unknown();

        var level = Math.Clamp(current.MemoryUsed, 0, 1);
        var fill = previous is { MemoryOk: true } prior && Math.Abs(prior.MemoryUsed - level) >= 0.005;
        return new IslandSlotState(true, $"{Math.Round(level * 100):0}%", level, false, fill, false, false);
    }

    private static IslandSlotState Network(IslandRawSample? previous, IslandRawSample current, double elapsedSeconds)
    {
        if (previous is not { NetworkOk: true } prior || !current.NetworkOk || elapsedSeconds <= 0)
            return Unknown();
        if (!SameNicSet(prior.Nics, current.Nics))
            return Unknown();

        ulong receive = 0;
        ulong send = 0;
        foreach (var nic in current.Nics)
        {
            var before = prior.Nics.First(item => item.Id == nic.Id);
            if (nic.BytesIn < before.BytesIn || nic.BytesOut < before.BytesOut)
                return Unknown();
            receive += nic.BytesIn - before.BytesIn;
            send += nic.BytesOut - before.BytesOut;
        }

        var receiveRate = receive / elapsedSeconds;
        var sendRate = send / elapsedSeconds;
        var mega = Math.Max(receiveRate, sendRate) >= 1024 * 1024;
        var text = $"↓ {Format(receiveRate, mega)}\n↑ {Format(sendRate, mega)}";
        return new IslandSlotState(true, text, 0, false, false, receiveRate > 0, sendRate > 0);
    }

    private static bool SameNicSet(IslandNicSample[]? previous, IslandNicSample[]? current)
    {
        if (previous is null || current is null || previous.Length == 0 || current.Length == 0)
            return false;
        if (previous.Select(nic => nic.Id).Distinct().Count() != previous.Length)
            return false;
        if (current.Select(nic => nic.Id).Distinct().Count() != current.Length)
            return false;
        return previous.Select(nic => nic.Id).OrderBy(id => id).SequenceEqual(current.Select(nic => nic.Id).OrderBy(id => id));
    }

    private static string Format(double bytesPerSecond, bool mega) =>
        mega ? $"{bytesPerSecond / (1024 * 1024):0.0} MB/s" : $"{bytesPerSecond / 1024:0.0} KB/s";

    private static ulong Delta(ulong current, ulong previous) => current >= previous ? current - previous : 0;

    private static IslandSlotState Unknown() => new(false, "—", 0, false, false, false, false);
}

/// <summary>One-hertz samples while running. Stop drops the baseline so the next open starts unknown.</summary>
public sealed class IslandStatusClock
{
    private IslandRawSample? _previous;
    private bool _running;

    public bool IsRunning => _running;

    public void Start()
    {
        _running = true;
        _previous = null;
    }

    public void Stop()
    {
        _running = false;
        _previous = null;
    }

    public IslandStatusFrame? Tick(IslandRawSample sample, double elapsedSeconds)
    {
        if (!_running)
            return null;

        var frame = IslandStatusReducer.Reduce(_previous, sample, elapsedSeconds);
        _previous = sample;
        return frame;
    }
}

public readonly record struct IslandEffectPlan(bool StartCpuPulse, bool StartReceive, bool StartSend);

/// <summary>Each finite icon effect has its own latch. A CPU pulse does not block receive or send.</summary>
public sealed class IslandEffectGate
{
    private int _generation = 1;
    private CancellationTokenSource _cancel = new();

    public int Generation => _generation;

    public CancellationToken Token => _cancel.Token;

    public (IslandEffectPlan Plan, int Generation) Begin(IslandStatusFrame frame, bool reducedMotion)
    {
        var plan = reducedMotion
            ? default
            : new IslandEffectPlan(
                frame.Cpu.Pulse && !CpuBusy,
                frame.Network.Receive && !ReceiveBusy,
                frame.Network.Send && !SendBusy);
        if (plan.StartCpuPulse)
            CpuBusy = true;
        if (plan.StartReceive)
            ReceiveBusy = true;
        if (plan.StartSend)
            SendBusy = true;
        return (plan, _generation);
    }

    public void Finish(int generation, bool cpu, bool receive, bool send)
    {
        if (generation != _generation)
            return;
        if (cpu)
            CpuBusy = false;
        if (receive)
            ReceiveBusy = false;
        if (send)
            SendBusy = false;
    }

    public void CancelAll()
    {
        _generation++;
        CpuBusy = false;
        ReceiveBusy = false;
        SendBusy = false;
        _cancel.Cancel();
        _cancel.Dispose();
        _cancel = new CancellationTokenSource();
    }

    private bool CpuBusy { get; set; }
    private bool ReceiveBusy { get; set; }
    private bool SendBusy { get; set; }
}

/// <summary>Serial samples. Stop invalidates a read that is already inside the callback and a result still waiting for the UI.</summary>
public sealed class IslandSamplingSession
{
    private readonly IslandStatusClock _clock = new();
    private readonly object _serial = new();
    private int _generation;

    public int Generation => Volatile.Read(ref _generation);

    public bool IsCurrent(int generation) =>
        Volatile.Read(ref _generation) == generation && _clock.IsRunning;

    public int Start()
    {
        lock (_serial)
        {
            _lastTick = 0;
            _clock.Start();
            return Interlocked.Increment(ref _generation);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        lock (_serial)
            _clock.Stop();
    }

    public bool TrySample(Func<IslandRawSample> read, out int generation, out IslandStatusFrame frame)
    {
        var gen = Volatile.Read(ref _generation);
        frame = default;
        generation = gen;
        if (!_clock.IsRunning)
            return false;

        lock (_serial)
        {
            if (Volatile.Read(ref _generation) != gen || !_clock.IsRunning)
                return false;
            var sample = read();
            if (Volatile.Read(ref _generation) != gen || !_clock.IsRunning)
                return false;
            var now = Environment.TickCount64;
            var elapsed = _lastTick == 0 ? 0 : Math.Max(0, now - _lastTick) / 1000d;
            var result = _clock.Tick(sample, elapsed);
            if (result is null || Volatile.Read(ref _generation) != gen)
                return false;
            _lastTick = now;
            frame = result.Value;
            generation = gen;
            return true;
        }
    }

    private long _lastTick;
}
