using TanMenu.Core.Services;
using TanMenu.Wpf.Island;
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

    [Fact]
    public void ProgressHandler_IsRegisteredOnceForTheClass()
    {
        IslandProgressRegistration.Ensure();
        IslandProgressRegistration.Ensure();
        Assert.Equal(1, IslandProgressRegistration.Count);
    }

    [Fact]
    public void NoUpInterfaces_StayUnknown()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        var first = clock.Tick(EmptyNetwork(), 1)!.Value;
        var second = clock.Tick(EmptyNetwork(), 1)!.Value;

        Assert.False(first.Network.Known);
        Assert.False(second.Network.Known);
        Assert.Equal("—", second.Network.Text);
    }

    [Fact]
    public void AddedNic_DoesNotTreatHistoricalBytesAsCurrentTraffic()
    {
        var clock = new IslandStatusClock();
        clock.Start();
        clock.Tick(Nics(("a", 1000, 1000)), 1);
        var changed = clock.Tick(Nics(("a", 1000, 1000), ("b", 5_000_000, 0)), 1)!.Value;
        var settled = clock.Tick(Nics(("a", 1100, 1000), ("b", 5_000_100, 0)), 1)!.Value;

        Assert.False(changed.Network.Known);
        Assert.True(settled.Network.Known);
        Assert.DoesNotContain("488", settled.Network.Text);
        Assert.Contains("0.2 KB/s", settled.Network.Text);
        Assert.True(settled.Network.Receive);
        Assert.False(settled.Network.Send);
    }

    [Fact]
    public void SimultaneousEffects_StartIndependently_AndStaleFinishDoesNotClearTheNewOne()
    {
        var gate = new IslandEffectGate();
        var frame = new IslandStatusFrame(
            new IslandSlotState(true, "20%", 0.2, true, false, false, false),
            new IslandSlotState(true, "10%", 0.1, false, false, false, false),
            new IslandSlotState(true, "↓ 1.0 KB/s\n↑ 1.0 KB/s", 0, false, false, true, true));

        var first = gate.Begin(frame, reducedMotion: false);
        Assert.True(first.Plan.StartCpuPulse);
        Assert.True(first.Plan.StartReceive);
        Assert.True(first.Plan.StartSend);

        gate.CancelAll();
        var restarted = gate.Begin(frame, reducedMotion: false);
        gate.Finish(first.Generation, cpu: true, receive: true, send: true);
        var blocked = gate.Begin(frame, reducedMotion: false);

        Assert.True(restarted.Plan.StartCpuPulse);
        Assert.False(blocked.Plan.StartCpuPulse);
        Assert.False(blocked.Plan.StartReceive);
        Assert.False(blocked.Plan.StartSend);
    }

    [Fact]
    public void SlowRead_DiscardedWhenStopped_AndRestartBeginsUnknown()
    {
        var session = new IslandSamplingSession();
        session.Start();
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        IslandStatusFrame? applied = null;
        var worker = Task.Run(() => session.TrySample(() =>
        {
            entered.Set();
            release.Wait();
            return Sample(0, 10, 0, 0.2, 10, 10);
        }, out _, out var frame) ? frame : (IslandStatusFrame?)null);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var stopper = Task.Run(() => session.Stop());
        Assert.True(stopper.Wait(TimeSpan.FromMilliseconds(300)));
        release.Set();
        applied = worker.Result;
        Assert.Null(applied);

        session.Start();
        Assert.True(session.TrySample(() => Sample(0, 10, 0, 0.2, 10, 10), out _, out var first));
        Assert.False(first.Cpu.Known);
        Assert.False(first.Network.Known);
        Thread.Sleep(5);
        Assert.True(session.TrySample(() => Sample(5, 30, 5, 0.2, 20, 10), out _, out var second));
        Assert.True(second.Cpu.Known);
        Assert.True(second.Network.Known);
    }

    [Fact]
    public void OverlappingReads_RunOneAtATime()
    {
        var session = new IslandSamplingSession();
        session.Start();
        var started = 0;
        var release = new ManualResetEventSlim(false);
        var first = Task.Run(() => session.TrySample(() =>
        {
            Interlocked.Increment(ref started);
            release.Wait();
            return Sample(0, 10, 0, 0.2, 10, 10);
        }, out _, out _));
        var second = Task.Run(() =>
        {
            while (Volatile.Read(ref started) == 0)
                Thread.Sleep(5);
            return session.TrySample(() =>
            {
                Interlocked.Increment(ref started);
                return Sample(5, 40, 5, 0.2, 30, 10);
            }, out _, out var frame) ? frame : default;
        });

        Thread.Sleep(50);
        Assert.Equal(1, Volatile.Read(ref started));
        release.Set();
        Assert.True(first.Result);
        Assert.True(second.Result.Cpu.Known);
        Assert.Equal(2, Volatile.Read(ref started));
    }

    private static IslandRawSample EmptyNetwork() =>
        new(true, 0, 10, 0, true, 0.2, true, []);

    private static IslandRawSample Nics(params (string Id, ulong In, ulong Out)[] nics) =>
        new(true, 0, 10, 0, true, 0.2, true, nics.Select(nic => new IslandNicSample(nic.Id, nic.In, nic.Out)).ToArray());

    private static IslandRawSample Sample(ulong idle, ulong kernel, ulong user, double memory, ulong bytesIn, ulong bytesOut) =>
        new(true, idle, kernel, user, true, memory, true, [new IslandNicSample("a", bytesIn, bytesOut)]);
}
