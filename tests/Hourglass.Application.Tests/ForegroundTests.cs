namespace Hourglass.Application.Tests;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class ForegroundTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCloseRemovesOnlySelectedSessionAndPersists(bool locked)
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.CreateSessionAsync(Request("owned", "25 minutes", new(LockInterface: locked)));
        await runtime.CreateSessionAsync(Request("other", "5 minutes"));
        await runtime.ExecuteAsync(new SessionCommand.Start("owned"));
        Assert.IsType<ApplicationResult<bool>.Success>(await runtime.CloseSessionAsync("owned"));
        Assert.Equal("other", Assert.Single(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(
            await runtime.ListSessionsAsync()).Value).SessionId);
        Assert.Equal("other", Assert.Single(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions).SessionId);
        Assert.Equal(ApplicationErrorCode.NotFound, Assert.IsType<ApplicationResult<bool>.Failure>(await runtime.CloseSessionAsync("owned")).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCloseHandlesPausedAndExpiredSessions(bool expired)
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.CreateSessionAsync(Request("owned", expired ? "0 seconds" : "5 minutes"));
        await runtime.ExecuteAsync(new SessionCommand.Start("owned"));
        if (!expired) { await runtime.ExecuteAsync(new SessionCommand.Pause("owned")); }
        Assert.IsType<ApplicationResult<bool>.Success>(await runtime.CloseSessionAsync("owned"));
        Assert.Empty(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions);
    }

    [Theory]
    [InlineData("0 seconds", false)]
    [InlineData("0 seconds", true)]
    public async Task ImmediateExpiryReturnsSnapshotAndClearsCheckpoint(string input, bool close)
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        Assert.IsType<ApplicationResult<ApplicationDataSnapshot>.Success>(await runtime.PrepareForegroundRuntimeAsync());
        var result = Assert.IsType<ApplicationResult<ForegroundOutcome>.Success>(await runtime.RunForegroundAsync(Request("own", input, new(CloseWhenExpired: close))));
        Assert.Equal(ForegroundCompletion.Expired, result.Value.Completion);
        Assert.Equal(TimerState.Expired, result.Value.Session.Countdown.State);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
        Assert.Empty(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions);
        Assert.Contains(input, Assert.IsType<LinuxAppSettings>(store.Documents["app"]).RecentTimerInputs);
    }

    [Fact]
    public async Task InvalidStartLeavesNoSessionOrCheckpoint()
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        await runtime.PrepareForegroundRuntimeAsync();
        Assert.Equal(ApplicationErrorCode.Validation, Assert.IsType<ApplicationResult<ForegroundOutcome>.Failure>(await runtime.RunForegroundAsync(Request("own", "invalid"))).Error.Code);
        Assert.Empty(store.Documents);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
    }

    [Fact]
    public async Task TimedExpiryWaitsForEffectAndPersistsExpiredStateBeforeItCompletes()
    {
        Clock clock = new();
        Store store = new();
        HeldNotification notification = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now + clock.Elapsed,
            settingsStore: store, services: SessionRuntimeServices.Unsupported with { Notifications = notification });
        await runtime.PrepareForegroundRuntimeAsync();
        Task<ApplicationResult<ForegroundOutcome>> run = runtime.RunForegroundAsync(Request("own", "1 second", new(DoNotKeepComputerAwake: true)));
        try
        {
            await store.RunningSaved.Task;
            clock.Advance(TimeSpan.FromSeconds(1));
            await runtime.TickAsync();
            await notification.Entered.Task;
            Assert.False(run.IsCompleted);
            Assert.IsType<ApplicationResult<bool>.Success>(await runtime.FlushPersistenceAsync());
            Assert.Equal(TimerState.Expired,
                Assert.Single(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions).Session!.State);
            notification.Complete();
            Assert.Equal(ForegroundCompletion.Expired, Assert.IsType<ApplicationResult<ForegroundOutcome>.Success>(await run).Value.Completion);
            Assert.Empty(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions);
        }
        finally { notification.Complete(); }
    }

    [Fact]
    public async Task CancellationRemovesOnlyCommandOwnedLockedSession()
    {
        Inhibitor inhibitor = new();
        await using HourglassRuntime runtime = Runtime(new Clock(), inhibitor);
        await runtime.CreateSessionAsync(Request("other", "25 minutes", new(DoNotKeepComputerAwake: true)));
        await runtime.ExecuteAsync(new SessionCommand.Start("other"));
        using CancellationTokenSource cancellation = new();
        Task<ApplicationResult<ForegroundOutcome>> run = runtime.RunForegroundAsync(Request("own", "10 minutes", new(LockInterface: true)), cancellation.Token);
        await inhibitor.Entries.Reader.ReadAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var sessions = Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value;
        Assert.Equal("other", Assert.Single(sessions).SessionId);
        Assert.Equal(TimerState.Running, sessions[0].Countdown.State);
    }

    [Fact]
    public async Task LoopAndPauseKeepForegroundWaitingUntilStop()
    {
        Clock clock = new();
        Inhibitor inhibitor = new();
        await using HourglassRuntime runtime = Runtime(clock, inhibitor);
        Task<ApplicationResult<ForegroundOutcome>> run = runtime.RunForegroundAsync(Request("own", "1 second", new(LoopTimer: true)));
        await inhibitor.Entries.Reader.ReadAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await runtime.TickAsync();
        await inhibitor.Entries.Reader.ReadAsync();
        Assert.False(run.IsCompleted);
        await runtime.ExecuteAsync(new SessionCommand.Pause("own"));
        Assert.False(run.IsCompleted);
        await runtime.ExecuteAsync(new SessionCommand.Resume("own"));
        await inhibitor.Entries.Reader.ReadAsync();
        await runtime.ExecuteAsync(new SessionCommand.Stop("own"));
        Assert.Equal(ForegroundCompletion.Stopped, Assert.IsType<ApplicationResult<ForegroundOutcome>.Success>(await run).Value.Completion);
    }

    [Fact]
    public async Task FailedStartCheckpointReturnsPersistenceErrorAndCleansSession()
    {
        Store store = new() { FailSaves = true };
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        await runtime.PrepareForegroundRuntimeAsync();
        var failed = Assert.IsType<ApplicationResult<ForegroundOutcome>.Failure>(await runtime.RunForegroundAsync(Request("own", "10 minutes")));
        Assert.Equal(ApplicationErrorCode.PersistenceFailure, failed.Error.Code);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadinessPreservesRecoveryRecordsWithoutRestoring(bool legacy)
    {
        Store store = new();
        ActiveTimerSessionDocument session = ActiveTimerSessionSnapshot.FromState("1 minute", "", ActiveTimerPresentationMode.Status,
            CountdownTransitions.Start(CountdownState.Stopped, TimerStart.FromString("1 minute"), Now, TimeSpan.Zero).State, Now).ToDocument();
        object original = legacy ? session : new ActiveTimerSessionsDocument(sessions: [new("saved", session)]);
        string key = legacy ? "active-session" : "active-sessions";
        store.Documents[key] = original;
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        var failure = Assert.IsType<ApplicationResult<ApplicationDataSnapshot>.Failure>(await runtime.PrepareForegroundRuntimeAsync());
        Assert.Equal(ApplicationErrorCode.RuntimeUnavailable, failure.Error.Code);
        Assert.Same(original, store.Documents[key]);
        Assert.Equal(0, store.Writes);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
    }

    [Fact]
    public async Task ExplicitEmptyCollectionSuppressesLegacyRecoveryAndReadFailureIsTyped()
    {
        Store store = new();
        store.Documents["active-sessions"] = ActiveTimerSessionsDocument.Empty;
        store.Documents["active-session"] = new ActiveTimerSessionDocument();
        await using HourglassRuntime runtime = new(settingsStore: store);
        Assert.IsType<ApplicationResult<ApplicationDataSnapshot>.Success>(await runtime.PrepareForegroundRuntimeAsync());
        store.FailLoads = true;
        Assert.Equal(ApplicationErrorCode.PersistenceFailure, Assert.IsType<ApplicationResult<ApplicationDataSnapshot>.Failure>(await runtime.PrepareForegroundRuntimeAsync()).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkRejectionDoesNotPartiallyMutate(bool locked)
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.CreateSessionAsync(Request("a", "5 minutes"));
        await runtime.ExecuteAsync(new SessionCommand.Start("a"));
        await runtime.CreateSessionAsync(Request("b", "5 minutes", new(LockInterface: locked)));
        if (locked) { await runtime.ExecuteAsync(new SessionCommand.Start("b")); }
        var failed = Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure>(await runtime.ExecuteAllAsync(SessionBatchCommand.Pause));
        Assert.Equal(locked ? ApplicationErrorCode.Locked : ApplicationErrorCode.InvalidTransition, failed.Error.Code);
        Assert.Contains("b", failed.Error.Message);
        Assert.Equal(TimerState.Running, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("a")).Value.Countdown.State);
    }

    [Fact]
    public async Task BulkPauseResumeStopPublishesOrderedResultsAndSupportsEmptySelection()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ExecuteAllAsync(SessionBatchCommand.Stop)).Value);
        foreach (string id in new[] { "b", "a" })
        {
            await runtime.CreateSessionAsync(Request(id, "5 minutes"));
            await runtime.ExecuteAsync(new SessionCommand.Start(id));
        }
        foreach (var (command, expected) in new[] { (SessionBatchCommand.Pause, TimerState.Paused), (SessionBatchCommand.Resume, TimerState.Running), (SessionBatchCommand.Stop, TimerState.Stopped) })
        {
            var result = Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ExecuteAllAsync(command)).Value;
            Assert.Equal(new[] { "a", "b" }, result.Select(snapshot => snapshot.SessionId));
            Assert.All(result, snapshot => Assert.Equal(expected, snapshot.Countdown.State));
        }
    }

    private static CreateSessionRequest Request(string id, string input, TimerDefaults? options = null) => new(id, input, "Tea", options ?? new(), new());
    private static HourglassRuntime Runtime(Clock clock, Inhibitor inhibitor) => new(clock: clock, wallClockNow: () => Now + clock.Elapsed,
        services: SessionRuntimeServices.Unsupported with { Inhibitor = inhibitor });

    private sealed class Clock : IMonotonicClock
    {
        private long ticks;
        public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref this.ticks));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref this.ticks, duration.Ticks);
    }

    private sealed class Inhibitor : ISessionInhibitor
    {
        public Channel<bool> Entries { get; } = Channel.CreateUnbounded<bool>();
        public bool IsSupported => true;
        public ValueTask<IAsyncDisposable?> InhibitAsync(string reason, bool inhibitSuspend, bool inhibitIdle, CancellationToken cancellationToken = default)
        {
            this.Entries.Writer.TryWrite(true);
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
    }

    private sealed class HeldNotification : INotificationService
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => this.completion.TrySetResult();
        public async Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            this.Entered.TrySetResult();
            await this.completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class Store : ISettingsStore
    {
        public ConcurrentDictionary<string, object> Documents { get; } = new(StringComparer.Ordinal);
        public bool FailSaves { get; init; }
        public bool FailLoads { get; set; }
        private int writes;
        public TaskCompletionSource RunningSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Writes => Volatile.Read(ref this.writes);
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (this.FailLoads) { throw new IOException("Read failed."); }
            return Task.FromResult(this.Documents.TryGetValue(key, out object? value) ? (T?)value : default);
        }
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (this.FailSaves) { throw new IOException("Save failed."); }
            if (value != null)
            {
                this.Documents[key] = value;
                if (key == "active-sessions" && value is ActiveTimerSessionsDocument sessions
                    && sessions.Sessions.Any(item => item.Session?.State == TimerState.Running)) { this.RunningSaved.TrySetResult(); }
            }
            Interlocked.Increment(ref this.writes);
            return Task.CompletedTask;
        }
    }
}
