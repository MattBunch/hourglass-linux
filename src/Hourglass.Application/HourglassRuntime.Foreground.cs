namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;
using Hourglass.Timing;

public enum SessionBatchCommand { Pause, Resume, Stop }
public enum ForegroundCompletion { Expired, Stopped, Dismissed }
public sealed record ForegroundOutcome(TimerSessionSnapshot Session, ForegroundCompletion Completion);

public sealed partial class HourglassRuntime
{
    /// <summary>Called under exclusive authority before the interim local frontend starts any sessions.</summary>
    public async Task<ApplicationResult<ApplicationDataSnapshot>> PrepareForegroundRuntimeAsync(CancellationToken cancellationToken = default)
    {
        ApplicationResult<ApplicationResult<ApplicationDataSnapshot>> result = await this.CatalogAsync(async () =>
        {
            ActiveTimerSessionsDocument? collection = await this.Data.LoadAsync<ActiveTimerSessionsDocument>("active-sessions", cancellationToken).ConfigureAwait(false);
            bool recovery = collection != null ? collection.Sessions.Length > 0
                : await this.Data.LoadAsync<ActiveTimerSessionDocument>("active-session", cancellationToken).ConfigureAwait(false) != null;
            if (recovery)
            {
                return (ApplicationResult<ApplicationDataSnapshot>)new ApplicationResult<ApplicationDataSnapshot>.Failure(
                    new(ApplicationErrorCode.RuntimeUnavailable, "Preserved sessions require GUI recovery before an exclusive terminal runtime can start."));
            }
            return new ApplicationResult<ApplicationDataSnapshot>.Success(await this.ReadApplicationDataCoreAsync(cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ApplicationResult<ApplicationResult<ApplicationDataSnapshot>>.Success success => success.Value,
            ApplicationResult<ApplicationResult<ApplicationDataSnapshot>>.Failure failure => new ApplicationResult<ApplicationDataSnapshot>.Failure(failure.Error),
            _ => throw new InvalidOperationException("Unknown application result.")
        };
    }

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ExecuteAllAsync(SessionBatchCommand command, CancellationToken cancellationToken = default) =>
        this.QueryAsync(() =>
        {
            if (!Enum.IsDefined(command))
            {
                return (ApplicationResult<ImmutableArray<TimerSessionSnapshot>>)new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure(
                    new(ApplicationErrorCode.Unsupported, "Unknown bulk command."));
            }
            string[] ids = this.sessions.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            SessionCommand Create(string id) => command switch
            {
                SessionBatchCommand.Pause => new SessionCommand.Pause(id),
                SessionBatchCommand.Resume => new SessionCommand.Resume(id),
                _ => new SessionCommand.Stop(id)
            };
            ApplicationError? error = null;
            List<string> rejected = [];
            foreach (string id in ids)
            {
                ApplicationError? invalid = ValidateLifecycle(this.sessions[id].Session.Snapshot(id), Create(id));
                if (invalid != null) { error ??= invalid; rejected.Add(id); }
            }
            if (error != null)
            {
                return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure(error with { Message = $"{error.Message} Sessions: {string.Join(", ", rejected)}" });
            }
            bool previousSuppression = this.suppressPersistence;
            this.suppressPersistence = true;
            try
            {
                var snapshots = ImmutableArray.CreateBuilder<TimerSessionSnapshot>();
                foreach (string id in ids)
                {
                    ApplicationResult<TimerSessionSnapshot> applied = this.Execute(Create(id));
                    if (applied is not ApplicationResult<TimerSessionSnapshot>.Success success) { throw new InvalidOperationException("A validated bulk command failed."); }
                    snapshots.Add(success.Value);
                }
                return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success(snapshots.ToImmutable());
            }
            finally
            {
                this.suppressPersistence = previousSuppression;
                if (ids.Length > 0) { this.QueuePersistence(); this.QueueWakeAlarm(); }
            }
        }, cancellationToken);

    public async Task<ApplicationResult<ForegroundOutcome>> RunForegroundAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplicationResult<bool> validated = await this.QueryAsync(() =>
        {
            if (request.TimerInput == null) { return (ApplicationResult<bool>)new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.Validation, "Timer input is required.")); }
            return TimerInputValidation.Parse(request.TimerInput, this.wallClockNow()) is ApplicationResult<TimerStart>.Failure invalid
                ? new ApplicationResult<bool>.Failure(invalid.Error) : new ApplicationResult<bool>.Success(true);
        }, cancellationToken).ConfigureAwait(false);
        if (validated is ApplicationResult<bool>.Failure rejected) { return new ApplicationResult<ForegroundOutcome>.Failure(rejected.Error); }
        ApplicationResult<TimerSessionSnapshot> created = await this.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
        if (created is ApplicationResult<TimerSessionSnapshot>.Failure failed) { return new ApplicationResult<ForegroundOutcome>.Failure(failed.Error); }

        TaskCompletionSource<ForegroundOutcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SessionSubscription? subscription = null;
        ApplicationResult<ForegroundOutcome>? result = null;
        ApplicationResult<bool>? flushed = null;
        try
        {
            ApplicationResult<SessionSubscription> subscribed = await this.SubscribeAsync(request.SessionId, ObserveAsync, cancellationToken).ConfigureAwait(false);
            if (subscribed is ApplicationResult<SessionSubscription>.Failure unavailable)
            {
                result = new ApplicationResult<ForegroundOutcome>.Failure(unavailable.Error);
            }
            else if (subscribed is ApplicationResult<SessionSubscription>.Success connected)
            {
                subscription = connected.Value;
                ApplicationResult<TimerSessionSnapshot> launch = await this.ExecuteAsync(new SessionCommand.Start(request.SessionId), cancellationToken).ConfigureAwait(false);
                started.TrySetResult();
                if (launch is ApplicationResult<TimerSessionSnapshot>.Failure invalid) { result = new ApplicationResult<ForegroundOutcome>.Failure(invalid.Error); }
                else
                {
                    ApplicationResult<bool> durable = await this.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
                    if (durable is ApplicationResult<bool>.Failure storage) { result = new ApplicationResult<ForegroundOutcome>.Failure(storage.Error); }
                    else
                    {
                        await Task.WhenAny(completion.Task, subscription.Completion).WaitAsync(cancellationToken).ConfigureAwait(false);
                        result = completion.Task.IsCompleted
                            ? new ApplicationResult<ForegroundOutcome>.Success(await completion.Task.ConfigureAwait(false))
                            : new ApplicationResult<ForegroundOutcome>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, "The runtime disconnected before completion."));
                    }
                }
            }
        }
        finally
        {
            started.TrySetResult();
            subscription?.Dispose();
            await this.RemoveAsync(request.SessionId).ConfigureAwait(false);
            flushed = await this.FlushPersistenceAsync().ConfigureAwait(false);
        }
        if (result is ApplicationResult<ForegroundOutcome>.Success && flushed is ApplicationResult<bool>.Failure durability)
        {
            return new ApplicationResult<ForegroundOutcome>.Failure(durability.Error);
        }
        return result ?? new ApplicationResult<ForegroundOutcome>.Failure(new(ApplicationErrorCode.InternalFailure, "Foreground operation produced no result."));

        async Task ObserveAsync(SessionNotification notification)
        {
            await started.Task.ConfigureAwait(false);
            if (notification.Removed)
            {
                completion.TrySetResult(new(notification.Snapshot, notification.Snapshot.Countdown.State == TimerState.Expired ? ForegroundCompletion.Expired : ForegroundCompletion.Dismissed));
            }
            else if (notification.Snapshot.Countdown.State is TimerState.Expired or TimerState.Stopped)
            {
                await this.WaitForSessionEffectsAsync(request.SessionId).ConfigureAwait(false);
                ApplicationResult<TimerSessionSnapshot> latest = await this.GetSessionAsync(request.SessionId).ConfigureAwait(false);
                if (latest is ApplicationResult<TimerSessionSnapshot>.Success current && current.Value.Countdown.State is TimerState.Expired or TimerState.Stopped)
                {
                    completion.TrySetResult(new(current.Value, current.Value.Countdown.State == TimerState.Expired ? ForegroundCompletion.Expired : ForegroundCompletion.Stopped));
                }
                else if (latest is ApplicationResult<TimerSessionSnapshot>.Failure missing && missing.Error.Code == ApplicationErrorCode.NotFound)
                {
                    completion.TrySetResult(new(notification.Snapshot, ForegroundCompletion.Expired));
                }
            }
        }
    }
}
