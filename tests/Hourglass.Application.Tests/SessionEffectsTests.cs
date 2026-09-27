namespace Hourglass.Application.Tests;

using Hourglass.Platform;
using Hourglass.Timing;
using Xunit;

public sealed class SessionEffectsTests
{
    [Theory]
    [InlineData(false, "stop")]
    [InlineData(true, "stop")]
    [InlineData(false, "pause")]
    [InlineData(true, "pause")]
    [InlineData(false, "restart")]
    [InlineData(true, "restart")]
    [InlineData(false, "replace")]
    [InlineData(true, "replace")]
    [InlineData(false, "remove")]
    [InlineData(true, "remove")]
    public async Task LateLeaseIsRejectedAndDisposedOnce(bool audio, string action)
    {
        await using HourglassRuntime runtime = new();
        Services services = new();
        TimerSession session = runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        runtime.Invoke(() => session.Start(TimerStart.FromString("1 minute"), new DateTime(2026, 1, 1)));
        Task work = audio ? effects.CompleteAsync(Request(session), new(false, false, false))
            : effects.AcquireInhibitionAsync(true, "timer");
        await services.Entered.Task;
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration cancellation = services.Token.Register(() => canceled.TrySetResult());
        if (action == "stop") runtime.Invoke(session.Stop);
        else if (action == "pause") runtime.Invoke(session.Pause);
        else if (action == "restart") runtime.Invoke(() => session.Restart(new DateTime(2026, 1, 1)));
        else if (action == "remove") runtime.Remove("timer");
        else if (audio) await effects.StopAudioAsync();
        else await effects.ReleaseInhibitionAsync();
        await canceled.Task;
        Assert.True(services.Token.IsCancellationRequested);
        Assert.True(services.Token.WaitHandle.WaitOne(0));
        Lease lease = new();
        services.Acquired.SetResult(lease);
        await work;
        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task ReverseAcquisitionsRetainOnlyNewestLease()
    {
        await using HourglassRuntime runtime = new();
        Services first = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", first, first, first, first);
        Task old = effects.AcquireInhibitionAsync(true, "timer");
        await first.Entered.Task;
        TaskCompletionSource<IAsyncDisposable?> oldResult = first.Acquired;
        first.Acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task current = effects.AcquireInhibitionAsync(true, "timer");
        Lease newest = new();
        first.Acquired.SetResult(newest);
        await current;
        Lease stale = new();
        oldResult.SetResult(stale);
        await old;
        Assert.Equal(1, stale.Disposals);
        Assert.Equal(0, newest.Disposals);
        await effects.ReleaseInhibitionAsync();
        Assert.Equal(1, newest.Disposals);
    }

    [Fact]
    public async Task AudioPreferenceChangeDuringNotificationPreventsLaterPlayback()
    {
        await using HourglassRuntime runtime = new();
        Services services = new() { Notification = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        TimerSession session = runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Task expiry = effects.CompleteAsync(Request(session), new(false, false, false));
        await effects.StopAudioAsync();
        services.Notification.SetResult();
        await expiry;
        Assert.False(services.Entered.Task.IsCompleted);
    }

    [Fact]
    public async Task SlowAcquisitionDoesNotBlockMutationQueue()
    {
        await using HourglassRuntime runtime = new();
        Services services = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Task work = effects.AcquireInhibitionAsync(true, "timer");
        Assert.Equal(42, await runtime.InvokeAsync(() => 42));
        services.Acquired.SetResult(null);
        await work;
    }

    [Fact]
    public async Task ShutdownWaitsForLateAcquisitionAndIsIdempotent()
    {
        Diagnostics diagnostics = new();
        HourglassRuntime runtime = new(diagnostics);
        Services services = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Task work = effects.AcquireInhibitionAsync(true, "timer");
        await services.Entered.Task;
        Task shutdown = runtime.DisposeAsync().AsTask();
        Assert.Same(shutdown, runtime.DisposeAsync().AsTask());
        Assert.False(shutdown.IsCompleted);
        Lease lease = new();
        services.Acquired.SetResult(lease);
        await work;
        await shutdown;
        Assert.Equal(1, lease.Disposals);
        Assert.True(runtime.EffectsCompletion.IsCompletedSuccessfully);
        Assert.Empty(diagnostics.Events);
    }

    [Fact]
    public async Task ShutdownDeadlineReportsIncompleteDrainAndKeepsLateCleanup()
    {
        DeadlineClock clock = new();
        Diagnostics diagnostics = new();
        HourglassRuntime runtime = new(diagnostics, clock);
        Services services = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Task work = effects.AcquireInhibitionAsync(true, "timer");
        await services.Entered.Task;
        Task shutdown = runtime.DisposeAsync().AsTask();
        DeadlineClock.Timer timer = await clock.Created.Task;
        Assert.Equal(TimeSpan.FromSeconds(5), timer.DueTime);
        Assert.False(shutdown.IsCompleted);
        timer.Fire();
        await shutdown;
        Assert.False(runtime.EffectsCompletion.IsCompleted);
        Assert.Contains("cleanup remains active", Assert.Single(diagnostics.Events).Message, StringComparison.Ordinal);
        Lease late = new();
        services.Acquired.SetResult(late);
        await work;
        await runtime.EffectsCompletion;
        Assert.Equal(1, late.Disposals);
    }

    [Fact]
    public async Task NotificationCancellationIsNotReportedAsFailure()
    {
        Diagnostics diagnostics = new();
        await using HourglassRuntime runtime = new(diagnostics);
        Services services = new() { Notification = new(TaskCreationOptions.RunContinuationsAsynchronously), ObserveNotificationCancellation = true };
        TimerSession session = runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Task expiry = effects.CompleteAsync(Request(session), new(false, false, false));
        await runtime.RemoveAsync("timer");
        await expiry;
        Assert.Empty(diagnostics.Events);
        Assert.False(services.Entered.Task.IsCompleted);
    }

    [Theory]
    [InlineData("notify", "notifications")]
    [InlineData("audio", "audio-alerts")]
    [InlineData("shutdown", "system-power")]
    public async Task PlatformFailureIsObservedWithoutLosingCompletion(string failure, string category)
    {
        Diagnostics diagnostics = new();
        await using HourglassRuntime runtime = new(diagnostics);
        Services services = new() { Failure = failure };
        services.Acquired.SetResult(null);
        TimerSession session = runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        ExpiryCompletion completion = await effects.CompleteAsync(Request(session) with { Shutdown = true }, new(false, true, false));
        Assert.Equal(ExpiryAction.Close, completion.Action);
        Assert.Equal(category, Assert.Single(diagnostics.Events).Category);
    }

    [Fact]
    public async Task ReplacementWaitsForPreviousDisposal()
    {
        await using HourglassRuntime runtime = new();
        Services services = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Lease lease = new() { Completion = released.Task };
        services.Acquired.SetResult(lease);
        await effects.AcquireInhibitionAsync(true, "timer");
        Task release = effects.ReleaseInhibitionAsync();
        Task replacement = effects.AcquireInhibitionAsync(true, "timer");
        Assert.Equal(1, services.Acquisitions);
        services.Acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        services.Acquired.SetResult(null);
        released.SetResult();
        await release;
        await replacement;
        Assert.Equal(2, services.Acquisitions);
        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task DisposalFailureIsObservedOnce()
    {
        Diagnostics diagnostics = new();
        await using HourglassRuntime runtime = new(diagnostics);
        Services services = new();
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => { });
        SessionEffects effects = runtime.AttachEffects("timer", services, services, services, services);
        Lease lease = new() { Completion = Task.FromException(new InvalidOperationException("release failed")) };
        services.Acquired.SetResult(lease);
        await effects.AcquireInhibitionAsync(true, "timer");
        await effects.ReleaseInhibitionAsync();
        await effects.ReleaseInhibitionAsync();
        Assert.Equal(1, lease.Disposals);
        Assert.Equal("release", Assert.Single(diagnostics.Events).Operation);
    }

    private sealed class Diagnostics : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];
        public void Record(DiagnosticEvent diagnosticEvent) => this.Events.Add(diagnosticEvent);
    }

    private sealed class DeadlineClock : TimeProvider
    {
        public TaskCompletionSource<Timer> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer timer = new(callback, state, dueTime);
            this.Created.TrySetResult(timer);
            return timer;
        }
        public sealed class Timer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            public TimeSpan DueTime { get; } = dueTime;
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static ExpiryEffectRequest Request(TimerSession session) => new(session.Revision, true, "title", "body", true, "sound", false, false);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Lease : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public Task Completion { get; init; } = Task.CompletedTask;
        public ValueTask DisposeAsync() { this.Disposals++; return new ValueTask(this.Completion); }
    }
    private sealed class Services : INotificationService, IAudioAlertService, ISessionInhibitor, ISystemPowerService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IAsyncDisposable?> Acquired { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Notification { get; init; }
        public bool ObserveNotificationCancellation { get; init; }
        public string? Failure { get; init; }
        public CancellationToken Token { get; private set; }
        public int Acquisitions { get; private set; }
        public bool IsSupported => true;
        public bool IsShutdownSupported => true;
        public bool IsSoundAvailable(string id) => true;
        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default) => this.Failure == "notify" ? Task.FromException(new InvalidOperationException("notification failed")) : this.ObserveNotificationCancellation
            ? (this.Notification?.Task ?? Task.CompletedTask).WaitAsync(cancellationToken) : this.Notification?.Task ?? Task.CompletedTask;
        public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => this.Failure == "shutdown"
            ? Task.FromException(new InvalidOperationException("shutdown failed")) : Task.CompletedTask;
        public Task<IAsyncDisposable?> PlayAlertAsync(string id, CancellationToken cancellationToken = default) => this.Acquire(cancellationToken);
        public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string id, CancellationToken cancellationToken = default) => this.Acquire(cancellationToken);
        public ValueTask<IAsyncDisposable?> InhibitAsync(string reason, bool inhibitSuspend, bool inhibitIdle, CancellationToken cancellationToken = default) => new(this.Acquire(cancellationToken));
        private Task<IAsyncDisposable?> Acquire(CancellationToken token)
        {
            this.Acquisitions++;
            this.Token = token;
            this.Entered.TrySetResult();
            return this.Failure == "audio" ? Task.FromException<IAsyncDisposable?>(new InvalidOperationException("audio failed")) : this.Acquired.Task;
        }
    }
}
