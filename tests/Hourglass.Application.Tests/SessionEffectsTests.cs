namespace Hourglass.Application.Tests;

using Hourglass.Platform;
using Hourglass.Timing;
using Xunit;

public sealed class SessionEffectsTests
{
    [Theory]
    [InlineData(false, "stop")]
    [InlineData(true, "stop")]
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
        Task work = audio ? effects.CompleteAsync(Request(session), new(false, false, false))
            : effects.AcquireInhibitionAsync(true, "timer");
        await services.Entered.Task;
        if (action == "stop") runtime.Invoke(() => session.Start(TimerStart.Zero, new DateTime(2026, 1, 1)));
        else if (action == "remove") runtime.Remove("timer");
        else if (audio) await effects.StopAudioAsync();
        else await effects.ReleaseInhibitionAsync();
        Assert.True(services.Token.IsCancellationRequested);
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

    private static ExpiryEffectRequest Request(TimerSession session) => new(session.Revision, true, "title", "body", true, "sound", false, false);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Lease : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { this.Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Services : INotificationService, IAudioAlertService, ISessionInhibitor, ISystemPowerService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IAsyncDisposable?> Acquired { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Notification { get; init; }
        public CancellationToken Token { get; private set; }
        public bool IsSupported => true;
        public bool IsShutdownSupported => true;
        public bool IsSoundAvailable(string id) => true;
        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default) => this.Notification?.Task ?? Task.CompletedTask;
        public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IAsyncDisposable?> PlayAlertAsync(string id, CancellationToken cancellationToken = default) => this.Acquire(cancellationToken);
        public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string id, CancellationToken cancellationToken = default) => this.Acquire(cancellationToken);
        public ValueTask<IAsyncDisposable?> InhibitAsync(string reason, bool inhibitSuspend, bool inhibitIdle, CancellationToken cancellationToken = default) => new(this.Acquire(cancellationToken));
        private Task<IAsyncDisposable?> Acquire(CancellationToken token)
        {
            this.Token = token;
            this.Entered.TrySetResult();
            return this.Acquired.Task;
        }
    }
}
