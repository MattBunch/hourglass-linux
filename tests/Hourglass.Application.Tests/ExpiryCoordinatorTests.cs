namespace Hourglass.Application.Tests;

using Hourglass.Timing;
using Xunit;

public sealed class ExpiryCoordinatorTests
{
    [Fact]
    public async Task ExpiryCompletesEffectsBeforeReturningRestart()
    {
        using TimerSession session = CreateSession();
        Effects effects = new() { IsShutdownRequested = true };

        ExpiryCompletion completion = await TimerExpiryCoordinator.CompleteAsync(
            session, session.Revision, new ExpiryDecision(true, false, true), effects);

        Assert.Equal(["release", "notify", "audio", "shutdown"], effects.Calls);
        Assert.Equal(ExpiryAction.Restart, completion.Action);
        Assert.Equal(TimerState.Expired, session.Countdown.State);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("notify")]
    [InlineData("audio")]
    [InlineData("shutdown")]
    public async Task ChangedSessionPreventsLaterEffectsAndCompletion(string changeAt)
    {
        using TimerSession session = CreateSession();
        Effects effects = new()
        {
            IsShutdownRequested = true,
            OnCall = name =>
            {
                if (name == changeAt)
                {
                    session.Stop();
                }
            }
        };

        ExpiryCompletion completion = await TimerExpiryCoordinator.CompleteAsync(
            session, session.Revision, new ExpiryDecision(false, true, false), effects);

        Assert.Equal(ExpiryAction.Superseded, completion.Action);
        Assert.Equal(changeAt, effects.Calls[^1]);
    }

    [Fact]
    public async Task RestoredExpiryOnlyNotifiesAndPlaysAudio()
    {
        using TimerSession session = CreateSession();
        Effects effects = new() { IsShutdownRequested = true };

        await TimerExpiryCoordinator.NotifyRestoredAsync(session, session.Revision, effects);

        Assert.Equal(["notify", "audio"], effects.Calls);
    }

    private static TimerSession CreateSession()
    {
        TimerSession session = new(new CountdownEngine(new Clock()));
        session.Start(TimerStart.Zero, new DateTime(2026, 1, 1));
        return session;
    }

    private sealed class Clock : IMonotonicClock
    {
        public TimeSpan Elapsed => TimeSpan.Zero;
    }

    private sealed class Effects : ITimerExpiryEffects
    {
        public List<string> Calls { get; } = [];
        public Action<string>? OnCall { get; init; }
        public bool IsShutdownRequested { get; init; }
        public Task ReleaseInhibitionAsync() => this.Call("release");
        public Task NotifyAsync() => this.Call("notify");
        public Task PlayAudioAsync(long revision) => this.Call("audio");
        public Task RequestShutdownAsync() => this.Call("shutdown");

        private Task Call(string name)
        {
            this.Calls.Add(name);
            this.OnCall?.Invoke(name);
            return Task.CompletedTask;
        }
    }
}
