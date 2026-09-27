namespace Hourglass.Application;

using Hourglass.Platform;

internal sealed record ExpiryEffectRequest(long Revision, bool NotificationsEnabled, string Title, string Body,
    bool AudioEnabled, string SoundId, bool LoopSound, bool Shutdown);

/// <summary>Owns platform effects. Resource ownership decisions run on the runtime queue.</summary>
internal sealed class SessionEffects
{
    private readonly HourglassRuntime runtime;
    private readonly TimerSession session;
    private readonly INotificationService notifications;
    private readonly IAudioAlertService audio;
    private readonly ISessionInhibitor inhibitor;
    private readonly ISystemPowerService power;
    private readonly IDiagnosticSink diagnostics;
    private readonly LeaseSlot playback = new();
    private readonly LeaseSlot inhibition = new();
    private readonly object tasksGate = new();
    private readonly HashSet<Task> pending = [];
    private CancellationTokenSource revisionCancellation = new();
    private readonly List<Task> revisionWork = [];
    private long revision;
    private volatile bool closed;

    public SessionEffects(HourglassRuntime runtime, TimerSession session, INotificationService notifications,
        IAudioAlertService audio, ISessionInhibitor inhibitor, ISystemPowerService power, IDiagnosticSink diagnostics)
    {
        this.runtime = runtime;
        this.session = session;
        this.notifications = notifications;
        this.audio = audio;
        this.inhibitor = inhibitor;
        this.power = power;
        this.diagnostics = diagnostics;
        this.revision = session.Revision;
    }

    internal Task Pending
    {
        get { lock (this.tasksGate) { return Task.WhenAll(this.pending.ToArray()); } }
    }

    // Called only on the mutation queue, including after commands that change a session revision.
    public void SynchronizeRevision()
    {
        if (this.revision == this.session.Revision || this.closed)
        {
            return;
        }

        this.revision = this.session.Revision;
        this.Cancel(this.revisionCancellation, Task.WhenAll(this.revisionWork));
        this.revisionWork.Clear();
        this.revisionCancellation = new();
    }

    public Task StopAudioAsync() => this.ReplaceLeaseAsync(this.playback, null, "audio-alerts", "stop-active");
    public Task ReleaseInhibitionAsync() => this.ReplaceLeaseAsync(this.inhibition, null, "session-inhibition", "release");

    public Task AcquireInhibitionAsync(bool keepAwake, string reason) => this.ReplaceLeaseAsync(this.inhibition,
        keepAwake ? token => this.inhibitor.InhibitAsync(reason, true, true, token).AsTask() : null,
        "session-inhibition", "acquire");

    public Task<ExpiryCompletion> CompleteAsync(ExpiryEffectRequest request, ExpiryDecision decision, bool restored = false)
    {
        TaskCompletionSource<ExpiryCompletion> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        long audioGeneration;
        try
        {
            (token, audioGeneration) = this.runtime.Invoke(() =>
            {
                ObjectDisposedException.ThrowIf(this.closed, this);
                this.Track(completion.Task);
                this.revisionWork.Add(completion.Task);
                return (this.revisionCancellation.Token, this.playback.Generation);
            });
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(new ExpiryCompletion(request.Revision, ExpiryAction.Superseded));
        }

        _ = RunAsync();
        return completion.Task;

        async Task RunAsync()
        {
            ExpiryCompletion result = new(request.Revision, ExpiryAction.Superseded);
            try
            {
                Adapter adapter = new(this, request, token, audioGeneration);
                if (restored)
                {
                    await TimerExpiryCoordinator.NotifyRestoredAsync(this.session, request.Revision, adapter).ConfigureAwait(false);
                    result = new(request.Revision, ExpiryAction.None);
                }
                else
                {
                    result = await TimerExpiryCoordinator.CompleteAsync(this.session, request.Revision, decision, adapter).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (this.closed || this.runtime.IsStopping) { }
            catch (Exception exception)
            {
                this.Record("expiry", "complete", exception);
            }
            finally
            {
                completion.TrySetResult(result);
            }
        }
    }

    // Called on the queue. Cleanup runs outside it, and remains tracked after a drain timeout.
    public Task Close()
    {
        if (!this.closed)
        {
            this.closed = true;
            this.Cancel(this.revisionCancellation, Task.WhenAll(this.revisionWork));
            this.revisionWork.Clear();
            this.Clear(this.playback);
            this.Clear(this.inhibition);
        }

        lock (this.tasksGate)
        {
            return Task.WhenAll(this.pending.ToArray());
        }
    }

    private Task ReplaceLeaseAsync(LeaseSlot slot, Func<CancellationToken, Task<IAsyncDisposable?>>? acquire,
        string category, string operation, long? expectedGeneration = null, long? expectedRevision = null)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        (long Generation, long Revision, CancellationToken Token, Task Released) request;
        try
        {
            request = this.runtime.Invoke(() =>
            {
                ObjectDisposedException.ThrowIf(this.closed, this);
                if ((expectedGeneration.HasValue && slot.Generation != expectedGeneration.Value)
                    || (expectedRevision.HasValue && !this.session.IsCurrent(expectedRevision.Value)))
                {
                    throw new OperationCanceledException();
                }
                Task released = this.Clear(slot);
                slot.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.revisionCancellation.Token);
                slot.Work = completion.Task;
                this.Track(completion.Task);
                return (slot.Generation, this.session.Revision, slot.Cancellation.Token, released);
            });
        }
        catch (OperationCanceledException)
        {
            return Task.CompletedTask;
        }
        catch (ObjectDisposedException)
        {
            return Task.CompletedTask;
        }

        _ = RunAsync();
        return completion.Task;

        async Task RunAsync()
        {
            IAsyncDisposable? candidate = null;
            try
            {
                await request.Released.ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                if (acquire != null)
                {
                    candidate = await acquire(request.Token).ConfigureAwait(false);
                    bool adopted = this.runtime.Invoke(() =>
                    {
                        if (this.closed || request.Token.IsCancellationRequested || slot.Generation != request.Generation
                            || !this.session.IsCurrent(request.Revision))
                        {
                            return false;
                        }

                        slot.Lease = candidate;
                        return true;
                    });
                    if (adopted)
                    {
                        candidate = null;
                    }
                }
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (request.Token.IsCancellationRequested || this.closed || this.runtime.IsStopping) { }
            catch (Exception exception)
            {
                this.Record(category, operation, exception);
            }
            finally
            {
                if (candidate != null)
                {
                    await this.ReleaseAsync(candidate, category, operation).ConfigureAwait(false);
                }

                completion.TrySetResult();
            }
        }
    }

    private Task Clear(LeaseSlot slot)
    {
        slot.Generation++;
        if (slot.Cancellation != null)
        {
            this.Cancel(slot.Cancellation, slot.Work);
            slot.Cancellation = null;
        }

        IAsyncDisposable? lease = slot.Lease;
        slot.Lease = null;
        if (lease != null)
        {
            slot.Released = this.Track(Task.WhenAll(slot.Released,
                Task.Run(() => this.ReleaseAsync(lease, "session-effects", "release"))));
        }
        return slot.Released;
    }

    private void Cancel(CancellationTokenSource cancellation, Task work)
    {
        // CancelAsync marks the token immediately and schedules callbacks off the queue.
        Task canceled = cancellation.CancelAsync();
        this.Track(FinishAsync());
        async Task FinishAsync()
        {
            try { await Task.WhenAll(canceled, work).ConfigureAwait(false); }
            catch (Exception exception) { this.Record("session-effects", "cancel", exception); }
            finally { cancellation.Dispose(); }
        }
    }

    private Task Track(Task task)
    {
        lock (this.tasksGate) { this.pending.Add(task); }
        _ = ForgetAsync();
        return task;
        async Task ForgetAsync()
        {
            try { await task.ConfigureAwait(false); }
            finally { lock (this.tasksGate) { this.pending.Remove(task); } }
        }
    }

    private async Task ReleaseAsync(IAsyncDisposable lease, string category, string operation)
    {
        try { await lease.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { this.Record(category, operation, exception); }
    }

    private void Record(string category, string operation, Exception exception) => this.diagnostics.TryRecord(new(
        DiagnosticSeverity.Warning, category == "system-power" ? DiagnosticFailureClass.UserRequested : DiagnosticFailureClass.BestEffort,
        category, operation, "session-effects", "Session platform effect failed.", exception));

    private sealed class LeaseSlot
    {
        public Task Released { get; set; } = Task.CompletedTask;
        public Task Work { get; set; } = Task.CompletedTask;
        public long Generation { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public IAsyncDisposable? Lease { get; set; }
    }

    private sealed class Adapter(SessionEffects owner, ExpiryEffectRequest request, CancellationToken token, long audioGeneration) : ITimerExpiryEffects
    {
        public bool IsShutdownRequested => request.Shutdown && owner.power.IsShutdownSupported;
        public Task ReleaseInhibitionAsync() => owner.ReplaceLeaseAsync(owner.inhibition, null,
            "session-inhibition", "release", expectedRevision: request.Revision);
        public Task NotifyAsync() => request.NotificationsEnabled
            ? this.PerformAsync(() => owner.notifications.ShowTimerExpiredAsync(request.Title, request.Body, token), "notifications", "show-expired")
            : Task.CompletedTask;
        public Task PlayAudioAsync(long revision)
        {
            token.ThrowIfCancellationRequested();
            return owner.ReplaceLeaseAsync(owner.playback, request.AudioEnabled
                ? cancellation => request.LoopSound ? owner.audio.PlayAlertLoopingAsync(request.SoundId, cancellation)
                    : owner.audio.PlayAlertAsync(request.SoundId, cancellation)
                : null, "audio-alerts", "play-expired", audioGeneration, request.Revision);
        }

        public Task RequestShutdownAsync() => this.PerformAsync(() => owner.power.RequestShutdownAsync(token), "system-power", "shutdown");
        private async Task PerformAsync(Func<Task> action, string category, string operation)
        {
            token.ThrowIfCancellationRequested();
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { owner.Record(category, operation, exception); }
        }
    }
}
