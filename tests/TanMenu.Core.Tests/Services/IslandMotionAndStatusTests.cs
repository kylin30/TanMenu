using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandMotionAndStatusTests
{
    [Fact]
    public void Reversal_ContinuesFromTheCurrentProgress()
    {
        var expand = IslandMotionPlan.Toward(0.4, expand: true, reducedMotion: false);
        var collapse = IslandMotionPlan.Toward(0.4, expand: false, reducedMotion: false);

        Assert.Equal(1, expand.Target);
        Assert.Equal(180, expand.DurationMs);
        Assert.Equal(0, collapse.Target);
        Assert.Equal(100, collapse.DurationMs);
    }

    [Fact]
    public void ReducedMotion_JumpsWithoutADuration()
    {
        var motion = IslandMotionPlan.Toward(0.2, expand: true, reducedMotion: true);
        Assert.Equal(1, motion.Target);
        Assert.Equal(0, motion.DurationMs);
    }

    [Fact]
    public void FirstCpuAndNetworkSample_StaysUnknown_WhileMemoryShows()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        var frame = clock.Tick(Sample(idle: 10, kernel: 30, user: 20, memory: 0.46, bytesIn: 1000, bytesOut: 500), 1);

        Assert.NotNull(frame);
        var shown = frame.Value;
        Assert.False(shown.Cpu.Known);
        Assert.False(shown.Network.Known);
        Assert.True(shown.Memory.Known);
        Assert.Equal("46%", shown.Memory.Text);
        Assert.False(shown.Memory.Fill);
    }

    [Fact]
    public void SecondSample_MapsLoad_AndZeroTrafficStaysStill()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        clock.Tick(Sample(0, 100, 50, 0.40, 1000, 500), 1);
        var frame = clock.Tick(Sample(50, 180, 90, 0.40, 1000, 500), 1);

        Assert.NotNull(frame);
        var shown = frame.Value;
        Assert.True(shown.Cpu.Known);
        Assert.True(shown.Cpu.Pulse);
        Assert.False(shown.Memory.Fill);
        Assert.True(shown.Network.Known);
        Assert.False(shown.Network.Receive);
        Assert.False(shown.Network.Send);
        Assert.Contains("0.0 KB/s", shown.Network.Text);
    }

    [Fact]
    public void Traffic_AndMemoryChange_RequestOneFiniteEffect()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        clock.Tick(Sample(0, 100, 0, 0.20, 1000, 1000), 1);
        var frame = clock.Tick(Sample(10, 200, 10, 0.50, 1000 + 2048, 1000 + 512), 1);

        Assert.NotNull(frame);
        var shown = frame.Value;
        Assert.True(shown.Memory.Fill);
        Assert.True(shown.Network.Receive);
        Assert.True(shown.Network.Send);
    }

    [Fact]
    public void Stop_ClearsTheBaseline()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        clock.Tick(Sample(0, 100, 0, 0.2, 10, 10), 1);
        clock.Stop();

        Assert.False(clock.IsRunning);
        Assert.Null(clock.Tick(Sample(0, 200, 0, 0.2, 20, 20), 1));

        clock.Start();
        var again = clock.Tick(Sample(0, 200, 0, 0.2, 20, 20), 1);
        Assert.False(again!.Value.Cpu.Known);
        Assert.False(again.Value.Network.Known);
    }

    [Fact]
    public void CounterReset_ReturnsNetworkToUnknown()
    {
        var frame = IslandStatusReducer.Reduce(
            Sample(0, 10, 0, 0.2, 5000, 100),
            Sample(1, 20, 0, 0.2, 10, 100),
            1);

        Assert.False(frame.Network.Known);
        Assert.Equal("—", frame.Network.Text);
    }

    private static IslandRawSample Sample(ulong idle, ulong kernel, ulong user, double memory, ulong bytesIn, ulong bytesOut) =>
        new(true, idle, kernel, user, true, memory, true, bytesIn, bytesOut);
}
