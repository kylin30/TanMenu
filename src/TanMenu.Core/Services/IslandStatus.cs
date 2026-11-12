namespace TanMenu.Core.Services;

public readonly record struct IslandRawSample(
    bool CpuOk,
    ulong Idle,
    ulong Kernel,
    ulong User,
    bool MemoryOk,
    double MemoryUsed,
    bool NetworkOk,
    ulong BytesIn,
    ulong BytesOut);

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
        if (current.BytesIn < prior.BytesIn || current.BytesOut < prior.BytesOut)
            return Unknown();

        var receiveRate = (current.BytesIn - prior.BytesIn) / elapsedSeconds;
        var sendRate = (current.BytesOut - prior.BytesOut) / elapsedSeconds;
        var mega = Math.Max(receiveRate, sendRate) >= 1024 * 1024;
        var text = $"↓ {Format(receiveRate, mega)}  ↑ {Format(sendRate, mega)}";
        return new IslandSlotState(true, text, 0, false, false, receiveRate > 0, sendRate > 0);
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
