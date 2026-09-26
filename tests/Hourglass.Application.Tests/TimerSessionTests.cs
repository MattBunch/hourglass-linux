namespace Hourglass.Application.Tests;

using Hourglass.Timing;
using Xunit;

public sealed class TimerSessionTests
{
    private static readonly DateTime Now = new(2026, 6, 8, 12, 0, 0);

    [Fact]
    public void ExpiryIsReturnedOnceAndEarlierStateIsUnchanged()
    {
        Clock clock = new();
        using TimerSession session = new(new CountdownEngine(clock));
        CountdownTransition started = session.Start(TimerStart.FromString("1 second"), Now);
        clock.Elapsed = TimeSpan.FromSeconds(1);
        CountdownTransition expired = session.Tick();
        clock.Elapsed = TimeSpan.FromSeconds(2);
        CountdownTransition later = session.Tick();

        Assert.Equal(TimerState.Running, started.State.State);
        Assert.Contains(CountdownEffect.Expired, Effects(expired));
        Assert.DoesNotContain(CountdownEffect.Expired, Effects(later));
        Assert.Equal(TimeSpan.FromSeconds(1), started.State.TimeLeft);
    }

    [Fact]
    public void PauseAtExpiryReturnsExpiryInsteadOfPause()
    {
        Clock clock = new();
        using TimerSession session = new(new CountdownEngine(clock));
        session.Start(TimerStart.FromString("1 second"), Now);
        clock.Elapsed = TimeSpan.FromSeconds(1);

        CountdownTransition transition = session.Pause();

        Assert.Equal(TimerState.Expired, transition.State.State);
        Assert.Contains(CountdownEffect.Expired, Effects(transition));
        Assert.DoesNotContain(CountdownEffect.Paused, Effects(transition));
    }

    [Fact]
    public void PauseResumeUsesMonotonicElapsedDespiteWallClockChange()
    {
        Clock clock = new();
        using TimerSession session = new(new CountdownEngine(clock));
        session.Start(TimerStart.FromString("10 seconds"), Now);
        clock.Elapsed = TimeSpan.FromSeconds(3);
        session.Pause();
        clock.Elapsed = TimeSpan.FromHours(1);
        session.Resume(Now.AddDays(-1));
        clock.Elapsed += TimeSpan.FromSeconds(2);

        Assert.Equal(TimeSpan.FromSeconds(5), session.Tick().State.TimeLeft);
    }

    [Fact]
    public void AbsoluteRestartFailurePreservesState()
    {
        using TimerSession session = new(new CountdownEngine(new Clock()));
        session.Start(TimerStart.FromString("1pm"), Now);
        CountdownState before = session.Countdown;

        Assert.False(session.Restart(Now).Succeeded);
        Assert.Same(before, session.Countdown);
    }

    [Fact]
    public void DisposedSessionRejectsMutation()
    {
        TimerSession session = new(new CountdownEngine(new Clock()));
        session.Dispose();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Tick());
    }

    private static CountdownEffect[] Effects(CountdownTransition transition) =>
        [transition.Effects.First, transition.Effects.Second, transition.Effects.Third, transition.Effects.Fourth];

    private sealed class Clock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; set; }
    }
}
