namespace Hourglass.Core.Tests.Timing;

using Hourglass.Serialization;
using Hourglass.Timing;
using Xunit;

public sealed class CountdownTransitionsTests
{
    private static readonly DateTime StartTime = new(2026, 6, 8, 10, 0, 0);

    [Fact]
    public void StartDurationCreatesImmutableRunningSnapshot()
    {
        CountdownState original = CountdownState.Stopped;

        CountdownTransition transition = CountdownTransitions.StartDuration(
            original,
            TimeSpan.FromSeconds(10),
            StartTime,
            TimeSpan.Zero);

        Assert.True(transition.Succeeded);
        Assert.Equal(TimerState.Stopped, original.State);
        Assert.Equal(TimerState.Running, transition.State.State);
        Assert.Equal(StartTime, transition.State.StartTime);
        Assert.Equal(StartTime.AddSeconds(10), transition.State.EndTime);
        Assert.Equal(TimeSpan.Zero, transition.State.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(10), transition.State.TimeLeft);
        Assert.Equal(CountdownEffect.Started, transition.Effects.First);
        Assert.Equal(CountdownEffect.Ticked, transition.Effects.Second);
    }

    [Fact]
    public void StartParsedTimerRejectsInvalidInput()
    {
        CountdownTransition transition = CountdownTransitions.Start(
            CountdownState.Stopped,
            TimerStart.FromString("not a timer"),
            StartTime,
            TimeSpan.Zero);

        Assert.False(transition.Succeeded);
        Assert.Equal(TimerState.Stopped, transition.State.State);
    }

    [Fact]
    public void StartAbsoluteRejectsPastEndTime()
    {
        CountdownTransition transition = CountdownTransitions.StartAbsolute(
            CountdownState.Stopped,
            StartTime,
            StartTime.AddSeconds(-1),
            TimeSpan.Zero);

        Assert.False(transition.Succeeded);
        Assert.Equal(TimerState.Stopped, transition.State.State);
    }

    [Fact]
    public void TickBeforeExpiryUpdatesElapsedAndRemaining()
    {
        CountdownState state = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition transition = CountdownTransitions.Tick(state, TimeSpan.FromSeconds(4));

        Assert.Equal(TimerState.Running, transition.State.State);
        Assert.Equal(TimeSpan.FromSeconds(4), transition.State.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(6), transition.State.TimeLeft);
        Assert.Equal(CountdownEffect.Ticked, transition.Effects.First);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(12, 2)]
    public void TickAtOrAfterExpiryTransitionsToExpired(int elapsedSeconds, int expiredSeconds)
    {
        CountdownState state = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition transition = CountdownTransitions.Tick(state, TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal(TimerState.Expired, transition.State.State);
        Assert.Equal(TimeSpan.FromSeconds(10), transition.State.TimeElapsed);
        Assert.Equal(TimeSpan.Zero, transition.State.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(expiredSeconds), transition.State.TimeExpired);
        Assert.Equal(CountdownEffect.Expired, transition.Effects.First);
        Assert.Equal(CountdownEffect.Ticked, transition.Effects.Second);
    }

    [Fact]
    public void ExpiryEffectIsEmittedOnlyWhenCrossingFromRunning()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));
        CountdownTransition expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(12));

        CountdownTransition later = CountdownTransitions.Tick(expired.State, TimeSpan.FromSeconds(15));

        Assert.Equal(TimerState.Expired, later.State.State);
        Assert.Equal(TimeSpan.FromSeconds(5), later.State.TimeExpired);
        Assert.Equal(CountdownEffect.Ticked, later.Effects.First);
        Assert.Equal(CountdownEffect.None, later.Effects.Second);
    }

    [Fact]
    public void PauseFreezesElapsedTime()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition paused = CountdownTransitions.Pause(running, TimeSpan.FromSeconds(3));
        CountdownTransition later = CountdownTransitions.Tick(paused.State, TimeSpan.FromSeconds(8));

        Assert.Equal(TimerState.Paused, paused.State.State);
        Assert.Equal(TimeSpan.FromSeconds(3), paused.State.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(7), paused.State.TimeLeft);
        Assert.Same(paused.State, later.State);
    }

    [Fact]
    public void PauseAfterExpiryKeepsExpiredState()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition transition = CountdownTransitions.Pause(running, TimeSpan.FromSeconds(12));

        Assert.Equal(TimerState.Expired, transition.State.State);
        Assert.Equal(CountdownEffect.Expired, transition.Effects.First);
        Assert.Equal(CountdownEffect.Ticked, transition.Effects.Second);
        Assert.Equal(CountdownEffect.None, transition.Effects.Third);
    }

    [Fact]
    public void ResumeContinuesFromPausedState()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));
        CountdownState paused = CountdownTransitions.Pause(running, TimeSpan.FromSeconds(3)).State;

        CountdownState resumed = CountdownTransitions.Resume(paused, StartTime.AddSeconds(8), TimeSpan.FromSeconds(8)).State;
        CountdownTransition later = CountdownTransitions.Tick(resumed, TimeSpan.FromSeconds(10));

        Assert.Equal(TimerState.Running, resumed.State);
        Assert.Equal(StartTime.AddSeconds(5), resumed.StartTime);
        Assert.Equal(StartTime.AddSeconds(15), resumed.EndTime);
        Assert.Equal(TimeSpan.FromSeconds(5), later.State.TimeLeft);
    }

    [Fact]
    public void StopReturnsCanonicalStoppedState()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition stopped = CountdownTransitions.Stop(running);

        Assert.Same(CountdownState.Stopped, stopped.State);
        Assert.Equal(CountdownEffect.Stopped, stopped.Effects.First);
    }

    [Fact]
    public void RestartUsesStoredDurationForSupportedTimer()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));

        CountdownTransition restarted = CountdownTransitions.Restart(
            running,
            StartTime.AddMinutes(1),
            TimeSpan.FromMinutes(1));

        Assert.True(restarted.Succeeded);
        Assert.Equal(TimerState.Running, restarted.State.State);
        Assert.Equal(StartTime.AddMinutes(1), restarted.State.StartTime);
        Assert.Equal(StartTime.AddMinutes(1).AddSeconds(10), restarted.State.EndTime);
        Assert.Equal(CountdownEffect.Stopped, restarted.Effects.First);
        Assert.Equal(CountdownEffect.Started, restarted.Effects.Second);
    }

    [Fact]
    public void RestartRejectsUnsupportedAbsoluteTimer()
    {
        CountdownState absolute = CountdownTransitions.StartAbsolute(
            CountdownState.Stopped,
            StartTime,
            StartTime.AddSeconds(10),
            TimeSpan.Zero).State;

        CountdownTransition restarted = CountdownTransitions.Restart(
            absolute,
            StartTime.AddSeconds(1),
            TimeSpan.FromSeconds(1));

        Assert.False(restarted.Succeeded);
        Assert.Same(absolute, restarted.State);
    }

    [Fact]
    public void RestoredRunningSnapshotUsesMonotonicElapsedTime()
    {
        var timerInfo = new TimerInfo
        {
            State = TimerState.Running,
            StartTime = StartTime,
            EndTime = StartTime.AddSeconds(10),
            TimeElapsed = TimeSpan.FromSeconds(4),
            TimeLeft = TimeSpan.FromSeconds(6),
            TimeExpired = TimeSpan.Zero,
            TotalTime = TimeSpan.FromSeconds(10)
        };

        CountdownState restored = CountdownState.FromTimerInfo(timerInfo, TimeSpan.FromSeconds(20));
        CountdownTransition ticked = CountdownTransitions.Tick(restored, TimeSpan.FromSeconds(22));

        Assert.Equal(TimeSpan.FromSeconds(6), ticked.State.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(4), ticked.State.TimeLeft);
    }

    [Fact]
    public void WallClockMovementDoesNotAffectMonotonicElapsedTime()
    {
        CountdownState running = StartDuration(TimeSpan.FromSeconds(10));
        CountdownState paused = CountdownTransitions.Pause(running, TimeSpan.FromSeconds(4)).State;

        CountdownState resumed = CountdownTransitions.Resume(paused, StartTime.AddHours(1), TimeSpan.FromSeconds(100)).State;
        CountdownTransition ticked = CountdownTransitions.Tick(resumed, TimeSpan.FromSeconds(102));

        Assert.Equal(TimeSpan.FromSeconds(6), ticked.State.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(4), ticked.State.TimeLeft);
    }

    private static CountdownState StartDuration(TimeSpan duration)
    {
        return CountdownTransitions.StartDuration(CountdownState.Stopped, duration, StartTime, TimeSpan.Zero).State;
    }
}
