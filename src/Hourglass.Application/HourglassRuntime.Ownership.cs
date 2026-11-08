namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;
using Hourglass.Timing;

public enum RuntimeClientKind { Control, Gui, Tui, Foreground }

public sealed class RuntimeClientLease(string id, IHourglassClient client, Func<ValueTask> dispose) : IAsyncDisposable
{
    private readonly object gate = new();
    private Task? disposal;
    public string Id { get; } = id;
    public IHourglassClient Client { get; } = client;
    public ValueTask DisposeAsync() { lock (this.gate) { return new(this.disposal ??= dispose().AsTask()); } }
}

public sealed partial class HourglassRuntime
{
    private readonly Dictionary<string, RuntimeClientKind> clientLeases = [];
    private readonly Dictionary<string, ActiveTimerSessionDefinition> deferredGui = [];
    private Task<ApplicationResult<bool>>? hostInitialization;
    private bool authorityDraining;

    public async Task<RuntimeClientLease> OpenClientAsync(RuntimeClientKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
        string id = await this.InvokeAsync(() =>
        {
            if (this.authorityDraining) { throw new ObjectDisposedException(nameof(HourglassRuntime)); }
            string created = Guid.NewGuid().ToString("N");
            this.clientLeases.Add(created, kind);
            return created;
        }, cancellationToken).ConfigureAwait(false);
        return new(id, new ScopedHourglassClient(this, id, kind), async () => await this.ReleaseClientAsync(id).ConfigureAwait(false));
    }

    public Task<bool> RequiresAuthorityAsync(CancellationToken cancellationToken = default, string? ignoreClientId = null) => this.InvokeAsync(
        () => this.sessions.Count > 0 || this.clientLeases.Keys.Any(id => id != ignoreClientId) || this.retiredEffects.Any(task => !task.IsCompleted), cancellationToken);

    public Task<bool> TryBeginIdleShutdownAsync(CancellationToken cancellationToken = default, string? ignoreClientId = null) => this.InvokeAsync(() =>
    {
        if (this.authorityDraining || this.sessions.Count > 0 || this.clientLeases.Keys.Any(id => id != ignoreClientId) || this.retiredEffects.Any(task => !task.IsCompleted)) { return false; }
        this.authorityDraining = true;
        return true;
    }, cancellationToken);

    internal async Task<ApplicationResult<bool>> ClosePresentationSessionAsync(string sessionId, string ownerId, CancellationToken token)
    {
        Task cleanup = await this.InvokeAsync(() =>
        {
            if (!this.sessions.TryGetValue(sessionId, out SessionRegistration? registration) || registration.OwnerLeaseId != ownerId
                || registration.Session.Lifetime == SessionLifetime.Detached) { return Task.CompletedTask; }
            this.sessions.Remove(sessionId);
            this.Publish(registration, CountdownEffects.None, removed: true);
            registration.Session.Dispose();
            Task effects = registration.Effects?.Close() ?? Task.CompletedTask;
            this.retiredEffects.RemoveAll(task => task.IsCompleted);
            this.retiredEffects.Add(effects);
            return effects;
        }, token).ConfigureAwait(false);
        await this.DrainAsync(cleanup, "close-presentation").ConfigureAwait(false);
        return await this.FlushPersistenceAsync(token).ConfigureAwait(false);
    }

    private async Task ReleaseClientAsync(string id)
    {
        Task[] cleanup = await this.InvokeAsync(() =>
        {
            if (!this.clientLeases.Remove(id, out RuntimeClientKind kind)) { return Array.Empty<Task>(); }
            string[] ids = this.sessions.Where(pair => pair.Value.OwnerLeaseId == id && pair.Value.Session.Lifetime != SessionLifetime.Detached)
                .Select(pair => pair.Key).ToArray();
            foreach (RestoredSession captured in this.CapturePersistedSessions().Where(item => ids.Contains(item.SessionId) && item.Lifetime == SessionLifetime.Gui))
            { this.deferredGui[captured.SessionId] = new(captured.SessionId, captured.Session.ToDocument(), SessionLifetime.Gui); }
            var work = new List<Task>();
            foreach (string sessionId in ids)
            {
                SessionRegistration registration = this.sessions[sessionId];
                this.sessions.Remove(sessionId);
                this.Publish(registration, CountdownEffects.None, removed: true);
                registration.Session.Dispose();
                Task effects = registration.Effects?.Close() ?? Task.CompletedTask;
                this.retiredEffects.Add(effects);
                work.Add(effects);
            }
            if (kind == RuntimeClientKind.Gui)
            {
                lock (this.initializationGate)
                {
                    Task<ApplicationResult<RuntimeStartupSnapshot>>? current = this.sessionInitialization;
                    if (current?.IsCompleted != false) { this.sessionInitialization = null; }
                    else
                    {
                        _ = current.ContinueWith(_ =>
                        {
                            lock (this.initializationGate) { if (ReferenceEquals(this.sessionInitialization, current)) { this.sessionInitialization = null; } }
                        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                    }
                }
            }
            this.retiredEffects.RemoveAll(task => task.IsCompleted);
            this.QueuePersistence();
            return work.ToArray();
        }).ConfigureAwait(false);
        await this.DrainAsync(Task.WhenAll(cleanup), "release-client").ConfigureAwait(false);
        await this.FlushPersistenceAsync().ConfigureAwait(false);
    }

    public Task<ApplicationResult<bool>> InitializeHostAsync(CancellationToken cancellationToken = default) => this.InitializeHostCoreAsync(true, cancellationToken);

    private Task<ApplicationResult<bool>> InitializeHostCoreAsync(bool saveInitialState, CancellationToken cancellationToken)
    {
        lock (this.initializationGate)
        {
            this.hostInitialization ??= InitializeAsync();
            return this.hostInitialization.WaitAsync(cancellationToken);
        }
        async Task<ApplicationResult<bool>> InitializeAsync()
        {
            await this.InvokeAsync(() => this.suppressPersistence = true).ConfigureAwait(false);
            bool ready = false;
            try
            {
                ApplicationDataSnapshot data = await this.LoadApplicationDataAsync().ConfigureAwait(false);
                ActiveTimerSessionsDocument records = await this.Data.ActiveSessions.LoadDocumentAsync(CancellationToken.None).ConfigureAwait(false);
                foreach (ActiveTimerSessionDefinition record in records.Sessions)
                {
                    if (record.Lifetime == SessionLifetime.Gui)
                    {
                        await this.InvokeAsync(() => this.deferredGui[record.SessionId] = record).ConfigureAwait(false);
                    }
                    else if (record.Lifetime == SessionLifetime.Detached && data.Settings.RestoreActiveSessionOnStartup)
                    {
                        ActiveTimerSessionSnapshot? restored = record.Session == null ? null : ActiveTimerSessionSnapshot.FromDocument(record.Session, this.wallClockNow(), TimeSpan.Zero);
                        if (restored != null) { await this.RestoreRecordAsync(new(record.SessionId, restored, restored.ExpiredWhileClosed, record.Lifetime), data, null).ConfigureAwait(false); }
                    }
                }
                ready = true;
                return new ApplicationResult<bool>.Success(true);
            }
            finally
            {
                await this.InvokeAsync(() => { this.suppressPersistence = false; this.persistenceReady = ready; if (ready && saveInitialState) { this.QueuePersistence(); this.QueueWakeAlarm(); } return true; }).ConfigureAwait(false);
            }
        }
    }

    public Task<ApplicationResult<RuntimeStartupSnapshot>> InitializeGuiAsync(string? launchInput = null, string? launchTitle = null, CancellationToken cancellationToken = default) =>
        this.InitializeSessionsCoreAsync(null, launchInput, launchTitle, cancellationToken);

    private async Task<bool> RestoreRecordAsync(RestoredSession restored, ApplicationDataSnapshot data, string? ownerId)
    {
        ApplicationResult<TimerSessionSnapshot> created = await this.CreateSessionAsync(new(restored.SessionId, restored.Session.TimerStartInput, restored.Session.TimerTitle,
            TimerDefaults.FromSettings(data.Settings), ApplicationPreferences.FromSettings(data.Settings), restored.Lifetime)
        { OwnerLeaseId = ownerId }).ConfigureAwait(false);
        if (created is ApplicationResult<TimerSessionSnapshot>.Failure) { return false; }
        await this.RestoreSessionAsync(restored.SessionId, restored.Session,
            ApplicationPreferences.FromSettings(NormalizeThemeSelection(restored.Session.HasOptions ? restored.Session.Options.ApplyTo(data.Settings) : data.Settings, data.CustomThemes))).ConfigureAwait(false);
        await this.UpdatePresentationAsync(restored.SessionId, new(restored.Session.TimerInput, restored.Session.PresentationMode, restored.Session.WindowGeometry)).ConfigureAwait(false);
        return true;
    }

    public async Task<ApplicationResult<TimerSessionSnapshot>> StartDetachedAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TimerInput == null) { return Failure(ApplicationErrorCode.Validation, "Timer input is required."); }
        if (TimerInputValidation.Parse(request.TimerInput, this.wallClockNow()) is ApplicationResult<TimerStart>.Failure invalid) { return new ApplicationResult<TimerSessionSnapshot>.Failure(invalid.Error); }
        ApplicationResult<TimerSessionSnapshot> created = await this.CreateSessionAsync(request with { Lifetime = SessionLifetime.Detached, OwnerLeaseId = null }, cancellationToken).ConfigureAwait(false);
        if (created is ApplicationResult<TimerSessionSnapshot>.Failure) { return created; }
        ApplicationResult<TimerSessionSnapshot> started = await this.ExecuteAsync(new SessionCommand.Start(request.SessionId), CancellationToken.None).ConfigureAwait(false);
        if (started is ApplicationResult<TimerSessionSnapshot>.Failure) { await this.RemoveAsync(request.SessionId).ConfigureAwait(false); return started; }
        return await this.DurableSessionAsync(started, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<ApplicationResult<TimerSessionSnapshot>> DetachSessionAsync(string sessionId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        ApplicationResult<TimerSessionSnapshot> result = await this.QueryAsync(() =>
        {
            if (!this.sessions.TryGetValue(sessionId, out SessionRegistration? registration)) { return Failure(ApplicationErrorCode.NotFound, "Unknown session."); }
            TimerSessionSnapshot before = registration.Session.Snapshot(sessionId);
            if (before.Revision != expectedRevision) { return Failure(ApplicationErrorCode.Conflict, "The session changed before detachment."); }
            if (before.AllowedActions.HasFlag(SessionActions.Unlock)) { return Failure(ApplicationErrorCode.Locked, "Unlock the session before detaching it."); }
            if (before.Lifetime == SessionLifetime.Foreground) { return Failure(ApplicationErrorCode.InvalidTransition, "Use start --detach instead of detaching an active foreground operation."); }
            registration.Session.SetLifetime(SessionLifetime.Detached);
            registration.OwnerLeaseId = null;
            this.Publish(registration, CountdownEffects.None);
            return Success(registration.Session.Snapshot(sessionId));
        }, cancellationToken).ConfigureAwait(false);
        return await this.DurableSessionAsync(result, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<ApplicationResult<TimerSessionSnapshot>> DurableSessionAsync(ApplicationResult<TimerSessionSnapshot> result, CancellationToken token)
    {
        if (result is not ApplicationResult<TimerSessionSnapshot>.Success success) { return result; }
        ApplicationResult<bool> durable = await this.FlushPersistenceAsync(token).ConfigureAwait(false);
        return durable is ApplicationResult<bool>.Failure failure ? new ApplicationResult<TimerSessionSnapshot>.Failure(failure.Error with
        { Message = $"Session {success.Value.SessionId} is live but persistence failed: {failure.Error.Message}" }) : result;
    }

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> StartSavedDetachedAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) =>
        this.StartSavedSessionsCoreAsync(selection, SessionLifetime.Detached, null, cancellationToken);
}
