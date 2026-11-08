namespace Hourglass.Application.Tests;

using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class ClientTests
{
    private static readonly DateTime Now = new(2026, 1, 1);

    [Fact]
    public async Task PreparedMetadataAndLifecycleAreAuthoritative()
    {
        Clock clock = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now);
        IHourglassClient client = runtime;
        TimerSessionSnapshot created = Value(await client.CreateSessionAsync(new("legacy-id", "2s", "Tea", new(), new())));
        Assert.Equal(TimerState.Stopped, created.Countdown.State);
        TimerSessionSnapshot running = Value(await client.ExecuteAsync(new SessionCommand.Start("legacy-id")));
        clock.Elapsed = TimeSpan.FromSeconds(1);
        await runtime.TickAsync();
        TimerSessionSnapshot ticked = Value(await client.GetSessionAsync("legacy-id"));
        Assert.Equal(running.Revision, ticked.Revision);
        Assert.Equal(TimeSpan.FromSeconds(1), ticked.Countdown.TimeLeft);
        TimerSessionSnapshot stopped = Value(await client.ExecuteAsync(new SessionCommand.Stop("legacy-id")));
        Assert.Equal("2s", stopped.TimerInput);
        Assert.Equal("Tea", stopped.TimerTitle);
        Assert.Equal(TimerState.Stopped, stopped.Countdown.State);
        Assert.Equal(TimerState.Stopped, created.Countdown.State);
        Value(await client.ExecuteAsync(new SessionCommand.Dismiss("legacy-id")));
        Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await client.GetSessionAsync("legacy-id"));
    }

    [Fact]
    public async Task InvalidAndConflictingEditsAreAtomicAndMetadataDoesNotRestart()
    {
        Clock clock = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now);
        Value(await runtime.CreateSessionAsync(new("id", "5s", "Original", new(), new())));
        TimerSessionSnapshot running = Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
        AssertError(ApplicationErrorCode.Validation, await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, "invalid", "Changed")));
        Assert.Equal(running, Value(await runtime.GetSessionAsync("id")));
        TimerSessionSnapshot renamed = Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, TimerTitle: "Changed")));
        Assert.Equal(running.Countdown, renamed.Countdown);
        AssertError(ApplicationErrorCode.Conflict, await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, TimerTitle: "Stale")));
        Assert.Equal(renamed, Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", renamed.Revision, TimerTitle: "Changed"))));
        clock.Elapsed = TimeSpan.FromSeconds(2);
        await runtime.TickAsync();
        TimerSessionSnapshot replaced = Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", renamed.Revision, TimerInput: "5s")));
        Assert.Equal(TimeSpan.FromSeconds(5), replaced.Countdown.TimeLeft);
        Assert.Equal("Original", running.TimerTitle);
    }

    [Fact]
    public async Task LockAllowsRenameAndExplicitUnlockButRejectsTimingAndOptions()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        Value(await runtime.CreateSessionAsync(new("id", "5s", "", new(LockInterface: true), new())));
        TimerSessionSnapshot running = Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
        AssertError(ApplicationErrorCode.Locked, await runtime.ExecuteAsync(new SessionCommand.Stop("id")));
        AssertError(ApplicationErrorCode.Locked, await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, TimerInput: "2s")));
        AssertError(ApplicationErrorCode.Locked, await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, Options: new())));
        TimerSessionSnapshot renamed = Value(await runtime.ExecuteAsync(new SessionCommand.Update("id", running.Revision, TimerTitle: "Allowed")));
        Assert.True(renamed.AllowedActions.HasFlag(SessionActions.Rename));
        Assert.False(renamed.AllowedActions.HasFlag(SessionActions.Update));
        TimerSessionSnapshot unlocked = Value(await runtime.ExecuteAsync(new SessionCommand.Unlock("id")));
        Assert.False(unlocked.Options.LockInterface);
        Value(await runtime.ExecuteAsync(new SessionCommand.Stop("id")));
    }

    [Fact]
    public async Task DuplicateUnknownInvalidTransitionsAndCancellationReturnWithoutMutation()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        TimerSessionSnapshot initial = Value(await runtime.CreateSessionAsync(new("id", "5s", "", new(), new())));
        AssertError(ApplicationErrorCode.Conflict, await runtime.CreateSessionAsync(new("id", "1s", "", new(), new())));
        AssertError(ApplicationErrorCode.NotFound, await runtime.ExecuteAsync(new SessionCommand.Stop("missing")));
        AssertError(ApplicationErrorCode.InvalidTransition, await runtime.ExecuteAsync(new SessionCommand.Pause("id")));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ExecuteAsync(new SessionCommand.Start("id"), cancellation.Token));
        Assert.Equal(initial, Value(await runtime.GetSessionAsync("id")));
    }

    [Fact]
    public async Task AbsoluteTimerPauseResumeAndRestartEligibilityUseCoreTransitions()
    {
        Clock clock = new();
        DateTime now = Now;
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => now);
        Value(await runtime.CreateSessionAsync(new("absolute", "12:01am", "", new(), new())));
        TimerSessionSnapshot running = Value(await runtime.ExecuteAsync(new SessionCommand.Start("absolute")));
        Assert.False(running.Countdown.SupportsRestart);
        AssertError(ApplicationErrorCode.InvalidTransition, await runtime.ExecuteAsync(new SessionCommand.Restart("absolute")));
        clock.Elapsed = TimeSpan.FromSeconds(10);
        TimerSessionSnapshot paused = Value(await runtime.ExecuteAsync(new SessionCommand.Pause("absolute")));
        Assert.Equal(TimerState.Paused, paused.Countdown.State);
        now = now.AddSeconds(20);
        Value(await runtime.ExecuteAsync(new SessionCommand.Resume("absolute")));
        clock.Elapsed = TimeSpan.FromSeconds(60);
        await runtime.TickAsync();
        await runtime.WaitForSessionEffectsAsync("absolute");
        Assert.Equal(TimerState.Expired, Value(await runtime.GetSessionAsync("absolute")).Countdown.State);
    }

    [Fact]
    public async Task PreparationReplacesMetadataWithoutStartingAndPreferencesAreImmutable()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        TimerSessionSnapshot original = Value(await runtime.CreateSessionAsync(new("id", "1s", "Original", new(), new())));
        Value(await runtime.ExecuteAsync(new SessionCommand.Start("id")));
        TimerSessionSnapshot prepared = Value(await runtime.ExecuteAsync(new SessionCommand.Prepare("id", "2s", "Saved", new(LoopTimer: true), new(NotificationsEnabled: false))));
        Assert.Equal(TimerState.Stopped, prepared.Countdown.State);
        Assert.Equal("2s", prepared.TimerInput);
        Assert.True(prepared.Options.LoopTimer);
        Assert.False(prepared.Preferences.NotificationsEnabled);
        Assert.True(original.Preferences.NotificationsEnabled);
        Assert.False(original.Options.LoopTimer);
    }

    private static TimerSessionSnapshot Value(ApplicationResult<TimerSessionSnapshot> result) => Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(result).Value;
    private static void AssertError(ApplicationErrorCode code, ApplicationResult<TimerSessionSnapshot> result) => Assert.Equal(code, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(result).Error.Code);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed { get; set; } }
}
