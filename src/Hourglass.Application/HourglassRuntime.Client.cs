namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;
using Hourglass.Timing;

public sealed partial class HourglassRuntime
{
    public Task<ApplicationResult<TimerSessionSnapshot>> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.QueryAsync(() =>
        {
            if (this.authorityDraining) { return Failure(ApplicationErrorCode.RuntimeUnavailable, "Runtime authority is shutting down."); }
            if (!Enum.IsDefined(request.Lifetime) || (request.OwnerLeaseId != null && !this.clientLeases.ContainsKey(request.OwnerLeaseId))) { return Failure(ApplicationErrorCode.Validation, "Invalid session ownership."); }
            if (string.IsNullOrWhiteSpace(request.SessionId) || request.TimerInput == null || request.TimerTitle == null || request.Options == null || request.Preferences == null)
            {
                return Failure(ApplicationErrorCode.Validation, "Session metadata is required.");
            }
            if (this.sessions.ContainsKey(request.SessionId))
            {
                return Failure(ApplicationErrorCode.Conflict, "The session ID already exists.");
            }
            TimerSession session = new(this.clock);
            session.CommitMetadata(request.TimerInput, request.TimerTitle, request.Options, request.Preferences);
            session.SetLifetime(request.Lifetime);
            SessionEffects effects = new(this, session, this.services.Notifications, this.services.Audio, this.services.Inhibitor, this.services.Power, this.diagnostics);
            this.sessions.Add(request.SessionId, new SessionRegistration(session, _ => { }, Effects: effects, Id: request.SessionId, Authoritative: true) { OwnerLeaseId = request.OwnerLeaseId });
            this.QueuePersistence();
            this.QueueWakeAlarm();
            return Success(session.Snapshot(request.SessionId));
        }, cancellationToken);
    }

    public Task<ApplicationResult<TimerSessionSnapshot>> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => this.QueryAsync(
        () => this.sessions.TryGetValue(sessionId, out SessionRegistration? registration) ? Success(registration.Session.Snapshot(sessionId))
            : Failure(ApplicationErrorCode.NotFound, "Unknown session."), cancellationToken);

    public async Task<ApplicationResult<bool>> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ApplicationResult<TimerSessionSnapshot> selected = await this.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (selected is ApplicationResult<TimerSessionSnapshot>.Failure failure)
        {
            return new ApplicationResult<bool>.Failure(failure.Error);
        }
        await this.RemoveAsync(sessionId).ConfigureAwait(false);
        return await this.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ListSessionsAsync(CancellationToken cancellationToken = default) => this.QueryAsync(
        () => (ApplicationResult<ImmutableArray<TimerSessionSnapshot>>)new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success(
            this.sessions.Select(pair => pair.Value.Session.Snapshot(pair.Key)).ToImmutableArray()), cancellationToken);

    public async Task<ApplicationResult<TimerSessionSnapshot>> ExecuteAsync(SessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Task recentWork = Task.CompletedTask;
        ApplicationResult<TimerSessionSnapshot> result = await this.QueryAsync(() =>
        {
            ApplicationResult<TimerSessionSnapshot> accepted = this.Execute(command);
            if (accepted is ApplicationResult<TimerSessionSnapshot>.Success success && command is SessionCommand.Start or SessionCommand.Update { TimerInput: not null })
            {
                recentWork = this.pendingRecentSave = this.RememberInputAfterAsync(this.pendingRecentSave, success.Value.TimerInput);
            }
            return accepted;
        }, cancellationToken).ConfigureAwait(false);
        await recentWork.ConfigureAwait(false);
        return result;
    }

    private ApplicationResult<TimerSessionSnapshot> Execute(SessionCommand command)
    {
        if (!this.sessions.TryGetValue(command.SessionId, out SessionRegistration? registration))
        {
            return Failure(ApplicationErrorCode.NotFound, "Unknown session.");
        }
        TimerSession session = registration.Session;
        TimerSessionSnapshot before = session.Snapshot(command.SessionId);
        if (command is SessionCommand.Update edit && edit.ExpectedRevision != before.Revision)
        {
            return Failure(ApplicationErrorCode.Conflict, "The session changed while editing.");
        }
        bool locked = before.AllowedActions.HasFlag(SessionActions.Unlock);
        if (locked && command is not SessionCommand.Unlock
            && command is not SessionCommand.Update { TimerInput: null, Options: null, Preferences: null })
        {
            return Failure(ApplicationErrorCode.Locked, "Unlock the session before changing its timer or options.");
        }

        DateTime now = this.wallClockNow();
        string input = session.TimerInput;
        string title = session.TimerTitle;
        TimerDefaults options = session.Options;
        ApplicationPreferences preferences = session.Preferences;
        TimerStart? start = null;
        bool replaceTimer = command is SessionCommand.Start || command is SessionCommand.Update { TimerInput: not null };
        if (command is SessionCommand.Start requested)
        {
            input = requested.TimerInput ?? input;
            title = requested.TimerTitle ?? title;
            options = requested.Options ?? options;
        }
        else if (command is SessionCommand.Update update)
        {
            input = update.TimerInput ?? input;
            title = update.TimerTitle ?? title;
            options = update.Options ?? options;
            preferences = update.Preferences ?? preferences;
        }
        else if (command is SessionCommand.Prepare prepare)
        {
            if (prepare.TimerInput == null || prepare.TimerTitle == null || prepare.Options == null || prepare.Preferences == null)
            {
                return Failure(ApplicationErrorCode.Validation, "Session metadata is required.");
            }
            input = prepare.TimerInput;
            title = prepare.TimerTitle;
            options = prepare.Options;
            preferences = prepare.Preferences;
        }
        if (replaceTimer)
        {
            if (TimerInputValidation.Parse(input, now) is not ApplicationResult<TimerStart>.Success parsed)
            {
                return Failure(ApplicationErrorCode.Validation, "Enter a valid current timer.");
            }
            start = parsed.Value;
        }
        if (ValidateLifecycle(before, command) is ApplicationError lifecycleError)
        {
            return new ApplicationResult<TimerSessionSnapshot>.Failure(lifecycleError);
        }

        CountdownTransition? transition = null;
        switch (command)
        {
            case SessionCommand.Start:
            case SessionCommand.Update when replaceTimer:
                transition = session.Start(start, now);
                break;
            case SessionCommand.Pause: transition = session.Pause(); break;
            case SessionCommand.Resume: transition = session.Resume(now); break;
            case SessionCommand.Stop: transition = session.Stop(); break;
            case SessionCommand.Restart: transition = session.Restart(now); break;
            case SessionCommand.Prepare:
                if (session.Countdown.State != TimerState.Stopped) { transition = session.Stop(); }
                break;
            case SessionCommand.Unlock: options = options with { LockInterface = false }; break;
            case SessionCommand.Dismiss:
                this.sessions.Remove(command.SessionId);
                this.Publish(registration, CountdownEffects.None, removed: true);
                session.Dispose();
                Task cleanup = registration.Effects?.Close() ?? Task.CompletedTask;
                this.retiredEffects.RemoveAll(task => task.IsCompleted);
                this.retiredEffects.Add(cleanup);
                return Success(before);
            case SessionCommand.Update: break;
            default: return Failure(ApplicationErrorCode.Unsupported, "Unknown session command.");
        }
        session.CommitMetadata(input, title, options, preferences);
        this.ProcessMetadataChange(registration, before);
        if (transition != null) { this.ProcessTransition(registration, transition); }
        if (before != session.Snapshot(command.SessionId)) { this.Publish(registration, transition?.Effects ?? CountdownEffects.None); }
        return Success(session.Snapshot(command.SessionId));
    }

    private async Task<ApplicationResult<T>> QueryAsync<T>(Func<ApplicationResult<T>> query, CancellationToken cancellationToken)
    {
        try { return await this.InvokeAsync(query, cancellationToken).ConfigureAwait(false); }
        catch (ObjectDisposedException)
        {
            return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, "The runtime has stopped."));
        }
    }

    private static ApplicationError? ValidateLifecycle(TimerSessionSnapshot before, SessionCommand command)
    {
        SessionActions required = command switch
        {
            SessionCommand.Start => SessionActions.Start,
            SessionCommand.Pause => SessionActions.Pause,
            SessionCommand.Resume => SessionActions.Resume,
            SessionCommand.Stop => SessionActions.Stop,
            SessionCommand.Restart => SessionActions.Restart,
            SessionCommand.Dismiss => SessionActions.Dismiss,
            _ => SessionActions.None
        };
        if (required != SessionActions.None && before.AllowedActions.HasFlag(SessionActions.Unlock))
        {
            return new(ApplicationErrorCode.Locked, "Unlock the session before changing its timer or options.");
        }
        if (required != SessionActions.None && !before.AllowedActions.HasFlag(required))
        {
            return new(ApplicationErrorCode.InvalidTransition, "The command is unavailable in this timer state.");
        }
        return null;
    }

    private static ApplicationResult<TimerSessionSnapshot> Success(TimerSessionSnapshot snapshot) => new ApplicationResult<TimerSessionSnapshot>.Success(snapshot);
    private static ApplicationResult<TimerSessionSnapshot> Failure(ApplicationErrorCode code, string message) => new ApplicationResult<TimerSessionSnapshot>.Failure(new(code, message));
}
