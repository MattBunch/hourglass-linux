namespace Hourglass.Application.Tests;

using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class AuthoritativeLifecycleTests
{
    private static readonly DateTime Now = new(2026, 1, 1);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ExpiryWorksWithoutPresentation(bool loop, bool close)
    {
        Clock clock = new();
        Notifications notifications = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now, services: Services(notifications));
        Value(await runtime.CreateSessionAsync(new("id", "1s", "Tea", new(LoopTimer: loop, CloseWhenExpired: close, LockInterface: true), new())));
        Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
        clock.Elapsed = TimeSpan.FromSeconds(1);
        await runtime.TickAsync();
        await runtime.WaitForSessionEffectsAsync("id");
        Assert.Equal(1, notifications.Count);
        ApplicationResult<TimerSessionSnapshot> result = await runtime.GetSessionAsync("id");
        if (close) { Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(result); }
        else
        {
            TimerSessionSnapshot snapshot = Value(result);
            Assert.Equal(loop ? TimerState.Running : TimerState.Expired, snapshot.Countdown.State);
            Assert.False(snapshot.Options.LockInterface);
        }
    }

    [Fact]
    public async Task RenamingDuringNotificationDoesNotInvalidateExpiryButChangingLoopPolicyDoes()
    {
        Clock clock = new();
        Notifications notifications = new() { Blocked = true };
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now, services: Services(notifications));
        Value(await runtime.CreateSessionAsync(new("id", "1s", "Tea", new(LoopTimer: true), new())));
        Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
        clock.Elapsed = TimeSpan.FromSeconds(1);
        await runtime.TickAsync();
        await notifications.Started.Task;
        TimerSessionSnapshot expired = Value(await runtime.GetSessionAsync("id"));
        Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", expired.Revision, TimerTitle: "Renamed")));
        notifications.Release.SetResult();
        await runtime.WaitForSessionEffectsAsync("id");
        Assert.Equal(TimerState.Running, Value(await runtime.GetSessionAsync("id")).Countdown.State);

        notifications.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        notifications.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Elapsed = TimeSpan.FromSeconds(2);
        await runtime.TickAsync();
        await notifications.Started.Task;
        expired = Value(await runtime.GetSessionAsync("id"));
        Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", expired.Revision, Options: expired.Options with { LoopTimer = false })));
        notifications.Release.SetResult();
        await runtime.WaitForSessionEffectsAsync("id");
        Assert.Equal(TimerState.Expired, Value(await runtime.GetSessionAsync("id")).Countdown.State);
    }

    [Fact]
    public async Task SlowSubscriberCannotBlockCommandsOrExpiryAndSubscriptionStartsAtomically()
    {
        Clock clock = new();
        Notifications notifications = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now, services: Services(notifications));
        TimerSessionSnapshot initial = Value(await runtime.CreateSessionAsync(new("id", "0s", "", new(), new())));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SessionSubscription subscription = Assert.IsType<ApplicationResult<SessionSubscription>.Success>(await runtime.SubscribeAsync("id", update =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        })).Value;
        Assert.Equal(initial, subscription.InitialSnapshot);
        try
        {
            Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
            await entered.Task;
            await runtime.WaitForSessionEffectsAsync("id");
            Assert.Equal(TimerState.Expired, Value(await runtime.GetSessionAsync("id")).Countdown.State);
            Assert.Equal(1, notifications.Count);
        }
        finally { subscription.Dispose(); release.TrySetResult(); }
    }

    [Fact]
    public async Task RestoredExpiryNotifiesWithoutLoopingClosingOrShutdown()
    {
        Clock clock = new();
        Notifications notifications = new();
        Power power = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now, services: Services(notifications) with { Power = power });
        Value(await runtime.CreateSessionAsync(new("id", "1s", "", new(), new())));
        CountdownState running = CountdownTransitions.StartDuration(CountdownState.Stopped, TimeSpan.FromSeconds(1), Now, TimeSpan.Zero).State;
        CountdownState expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(2)).State;
        ActiveTimerSessionSnapshot restored = ActiveTimerSessionSnapshot.FromState("1s", "Restored", ActiveTimerPresentationMode.Status, expired, Now,
            SavedTimerOptions.FromSettings(LinuxAppSettings.Default with { LoopTimer = true, CloseWhenExpired = true, ShutDownWhenExpired = true, LockInterface = true })) with
        { SavedState = TimerState.Running };
        Value(await runtime.RestoreSessionAsync("id", restored, new()));
        await runtime.WaitForSessionEffectsAsync("id");
        TimerSessionSnapshot snapshot = Value(await runtime.GetSessionAsync("id"));
        Assert.Equal(TimerState.Expired, snapshot.Countdown.State);
        Assert.False(snapshot.Options.LockInterface);
        Assert.Equal(1, notifications.Count);
        Assert.Equal(0, power.Count);
    }

    private sealed class Power : ISystemPowerService
    {
        public bool IsShutdownSupported => true;
        public int Count { get; private set; }
        public Task RequestShutdownAsync(CancellationToken cancellationToken = default) { this.Count++; return Task.CompletedTask; }
    }

    private static SessionRuntimeServices Services(Notifications notifications) => SessionRuntimeServices.Unsupported with { Notifications = notifications };
    private static TimerSessionSnapshot Value(ApplicationResult<TimerSessionSnapshot> result) => Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(result).Value;
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed { get; set; } }
    private sealed class Notifications : INotificationService
    {
        public int Count { get; private set; }
        public bool Blocked { get; init; }
        public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            this.Count++;
            this.Started.TrySetResult();
            if (this.Blocked) { await this.Release.Task; }
        }
    }
}
