namespace Hourglass.Linux.Avalonia.Tests;

using System.Collections.Immutable;
using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

/// <summary>Exercises the same client-only path used by remote GUI presentation.</summary>
public sealed class ClientAttachmentTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0);

    [Fact]
    public async Task ObservedViewDisposalPreservesSessionAndAuthority()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        Success(await runtime.CreateSessionAsync(new("observed", "5m", "Tea", new(), new())));
        Success(await runtime.ExecuteAsync(new SessionCommand.Start("observed")));
        MainWindowViewModel view = View(runtime, "observed", removeOnDispose: false);
        await view.LoadSettingsAsync();
        await view.PendingCommands;
        Assert.Equal("5m", view.TimerInput);
        Assert.Equal("Tea", view.TimerTitle);
        Assert.Single(Success(await runtime.ListSessionsAsync()));
        await view.DisposeAsync();
        Assert.Single(Success(await runtime.ListSessionsAsync()));
        Success(await runtime.ExecuteAsync(new SessionCommand.Pause("observed")));
    }

    [Fact]
    public async Task CreatedViewDisposalPreservesTimerDetachedAfterCreation()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.InitializeHostAsync();
        await using MainWindowViewModel view = View(runtime, null, removeOnDispose: true);
        await view.LoadSettingsAsync();
        await view.PendingCommands;
        TimerSessionSnapshot session = Assert.Single(Success(await runtime.ListSessionsAsync()));
        Success(await runtime.ExecuteAsync(new SessionCommand.Start(session.SessionId)));
        session = Success(await runtime.GetSessionAsync(session.SessionId));
        Success(await runtime.DetachSessionAsync(session.SessionId, session.Revision));
        await view.PendingCommands;
        Assert.False(view.ShouldPromptOnExit);
        await view.DisposeAsync();
        Assert.Equal(SessionLifetime.Detached, Assert.Single(Success(await runtime.ListSessionsAsync())).Lifetime);
    }

    [Fact]
    public async Task CreatedViewClosesOnlyItsSession()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        Success(await runtime.CreateSessionAsync(new("other", "5m", "Other", new(), new())));
        MainWindowViewModel view = View(runtime, null, removeOnDispose: true);
        await view.LoadSettingsAsync();
        await view.PendingCommands;
        Assert.Equal(2, Success(await runtime.ListSessionsAsync()).Length);
        await view.DisposeAsync();
        Assert.Equal("other", Assert.Single(Success(await runtime.ListSessionsAsync())).SessionId);
    }

    [Fact]
    public async Task ClientOnlyCommandsMutateExistingAuthoritativeSession()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        Success(await runtime.CreateSessionAsync(new("observed", "5m", "Tea", new(), new())));
        Success(await runtime.ExecuteAsync(new SessionCommand.Start("observed")));
        await using MainWindowViewModel view = View(runtime, "observed", removeOnDispose: false);
        await view.LoadSettingsAsync();
        await view.PendingCommands;
        view.PauseResumeCommand.Execute(null);
        await view.PendingCommands;
        Assert.Equal(Hourglass.Timing.TimerState.Paused, Success(await runtime.GetSessionAsync("observed")).Countdown.State);
        Assert.Equal(Hourglass.Timing.TimerState.Paused, view.State);
    }

    private static T Success<T>(ApplicationResult<T> result) => Assert.IsType<ApplicationResult<T>.Success>(result).Value;
    private static MainWindowViewModel View(IHourglassClient client, string? id, bool removeOnDispose) => new(
        new Clock(), () => Now, UnsupportedNotificationService.Instance, UnsupportedSessionInhibitor.Instance,
        new Store(), UnsupportedAudioAlertService.Instance, UnsupportedSystemPowerService.Instance,
        sessionId: id, persistActiveSessionDirectly: false, restoreActiveSessionOnLoad: false,
        client: client, removeSessionOnDispose: removeOnDispose);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Store : ISettingsStore
    {
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
