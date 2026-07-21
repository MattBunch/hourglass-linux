namespace Hourglass.Core.Tests.Timing;

using Hourglass.Serialization;
using Hourglass.Timing;
using Xunit;

public sealed class CountdownEngineTests
{
    [Fact]
    public void StartInitializesRunningState()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);
        var start = new DateTime(2026, 6, 8, 10, 0, 0);

        engine.Start(TimeSpan.FromSeconds(10), start);

        Assert.Equal(TimerState.Running, engine.State);
        Assert.Equal(start, engine.StartTime);
        Assert.Equal(start.AddSeconds(10), engine.EndTime);
        Assert.Equal(TimeSpan.Zero, engine.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.TimeLeft);
    }

    [Fact]
    public void UpdateUsesMonotonicElapsedTime()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);

        engine.Start(TimeSpan.FromSeconds(10), new DateTime(2026, 6, 8, 10, 0, 0));
        clock.Advance(TimeSpan.FromSeconds(4));
        engine.Update();

        Assert.Equal(TimeSpan.FromSeconds(4), engine.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(6), engine.TimeLeft);
    }

    [Fact]
    public void ExpiredEventFiresOnceWhenCrossingZero()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);
        int expiredCount = 0;
        engine.Expired += (_, _) => expiredCount++;

        engine.Start(TimeSpan.FromSeconds(1), new DateTime(2026, 6, 8, 10, 0, 0));
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Update();
        engine.Update();

        Assert.Equal(TimerState.Expired, engine.State);
        Assert.Equal(1, expiredCount);
        Assert.Equal(TimeSpan.Zero, engine.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.TimeExpired);
    }

    [Fact]
    public void PauseFreezesRemainingTimeUntilResume()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);

        engine.Start(TimeSpan.FromSeconds(10), new DateTime(2026, 6, 8, 10, 0, 0));
        clock.Advance(TimeSpan.FromSeconds(3));
        engine.Pause();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimerState.Paused, engine.State);
        Assert.Equal(TimeSpan.FromSeconds(7), engine.TimeLeft);

        engine.Resume(new DateTime(2026, 6, 8, 10, 0, 8));
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Update();

        Assert.Equal(TimeSpan.FromSeconds(5), engine.TimeLeft);
    }

    [Fact]
    public void StopClearsActiveTimingFields()
    {
        var engine = new CountdownEngine(new ManualMonotonicClock());

        engine.Start(TimeSpan.FromSeconds(10), new DateTime(2026, 6, 8, 10, 0, 0));
        engine.Stop();

        Assert.Equal(TimerState.Stopped, engine.State);
        Assert.Null(engine.StartTime);
        Assert.Null(engine.EndTime);
        Assert.Null(engine.TimeLeft);
        Assert.Null(engine.TotalTime);
    }

    [Fact]
    public void RestartWorksForDirectDurationTimers()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);
        var firstStart = new DateTime(2026, 6, 8, 10, 0, 0);
        var secondStart = new DateTime(2026, 6, 8, 10, 1, 0);

        engine.Start(TimeSpan.FromSeconds(10), firstStart);
        clock.Advance(TimeSpan.FromSeconds(4));
        engine.Update();

        Assert.True(engine.Restart(secondStart));

        Assert.Equal(TimerState.Running, engine.State);
        Assert.Equal(secondStart, engine.StartTime);
        Assert.Equal(secondStart.AddSeconds(10), engine.EndTime);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.TimeLeft);
    }

    [Fact]
    public void RestartPublishesStatesThatMatchEvents()
    {
        var clock = new ManualMonotonicClock();
        var engine = new CountdownEngine(clock);
        var observed = new List<(string EventName, TimerState State)>();
        engine.Stopped += (_, _) => observed.Add(("Stopped", engine.State));
        engine.Started += (_, _) => observed.Add(("Started", engine.State));
        engine.Tick += (_, _) => observed.Add(("Tick", engine.State));

        engine.Start(TimeSpan.FromSeconds(10), new DateTime(2026, 6, 8, 10, 0, 0));
        observed.Clear();

        Assert.True(engine.Restart(new DateTime(2026, 6, 8, 10, 1, 0)));

        Assert.Equal(
            [
                ("Stopped", TimerState.Stopped),
                ("Started", TimerState.Running),
                ("Tick", TimerState.Running)
            ],
            observed);
    }

    [Fact]
    public void FailedRestartDoesNotStopTimer()
    {
        var engine = new CountdownEngine(new ManualMonotonicClock());
        var start = new DateTime(2026, 6, 8, 10, 0, 0);
        engine.Start(start, start.AddSeconds(10));

        Assert.False(engine.Restart(start.AddSeconds(1)));

        Assert.Equal(TimerState.Running, engine.State);
    }

    [Fact]
    public void RestartIsRejectedForAbsoluteTimers()
    {
        var engine = new CountdownEngine(new ManualMonotonicClock());
        var start = new DateTime(2026, 6, 8, 10, 0, 0);

        engine.Start(start, start.AddSeconds(10));

        Assert.False(engine.Restart(start.AddSeconds(1)));
    }

    [Fact]
    public void RestoreRunningSnapshotContinuesFromPersistedElapsedTime()
    {
        var clock = new ManualMonotonicClock();
        var snapshot = new TimerInfo
        {
            State = TimerState.Running,
            StartTime = new DateTime(2026, 6, 8, 10, 0, 0),
            EndTime = new DateTime(2026, 6, 8, 10, 0, 10),
            TimeElapsed = TimeSpan.FromSeconds(4),
            TimeLeft = TimeSpan.FromSeconds(6),
            TimeExpired = TimeSpan.Zero,
            TotalTime = TimeSpan.FromSeconds(10)
        };

        var engine = new CountdownEngine(clock, snapshot);
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Update();

        Assert.Equal(TimeSpan.FromSeconds(6), engine.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(4), engine.TimeLeft);
    }

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan elapsed)
        {
            this.Elapsed += elapsed;
        }
    }
}
