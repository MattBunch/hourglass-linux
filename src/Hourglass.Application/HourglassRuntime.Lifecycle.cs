namespace Hourglass.Application;

using Hourglass.Settings;
using Hourglass.Timing;

public sealed partial class HourglassRuntime
{
    public Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Action<SessionNotification> publish, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        return this.SubscribeAsync(sessionId, notification => { publish(notification); return Task.CompletedTask; }, cancellationToken);
    }

    public Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Func<SessionNotification, Task> publish, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        return this.QueryAsync(() =>
        {
            if (!this.sessions.TryGetValue(sessionId, out Registration? registration))
            {
                return (ApplicationResult<SessionSubscription>)new ApplicationResult<SessionSubscription>.Failure(new(ApplicationErrorCode.NotFound, "Unknown session."));
            }
            SessionSubscription subscription = new(registration.Session.Snapshot(sessionId), registration.Sequence, publish, this.diagnostics);
            registration.Subscriptions.RemoveAll(item => item.IsDisposed);
            registration.Subscriptions.Add(subscription);
            return new ApplicationResult<SessionSubscription>.Success(subscription);
        }, cancellationToken);
    }

    public Task<ApplicationResult<TimerSessionSnapshot>> RestoreSessionAsync(string sessionId, ActiveTimerSessionSnapshot restored,
        ApplicationPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restored);
        ArgumentNullException.ThrowIfNull(preferences);
        return this.QueryAsync(() =>
        {
            if (!this.sessions.TryGetValue(sessionId, out Registration? registration)) { return Failure(ApplicationErrorCode.NotFound, "Unknown session."); }
            TimerSession session = registration.Session;
            if (session.Countdown.State != TimerState.Stopped) { return Failure(ApplicationErrorCode.InvalidTransition, "Restoration requires an unstarted session."); }
            LinuxAppSettings settings = restored.Options.ApplyTo(LinuxAppSettings.Default);
            session.CommitMetadata(restored.TimerStartInput, restored.TimerTitle, restored.HasOptions ? TimerDefaults.FromSettings(settings) : session.Options, preferences);
            session.Restore(restored.ToTimerInfo());
            registration.Effects?.SynchronizeRevision();
            if (session.Countdown.State == TimerState.Running)
            {
                registration.LifecycleWork = registration.Effects?.AcquireInhibitionAsync(!session.Options.DoNotKeepComputerAwake, this.services.InhibitionReason) ?? Task.CompletedTask;
            }
            else if (restored.ExpiredWhileClosed)
            {
                session.CommitMetadata(session.TimerInput, session.TimerTitle, session.Options with { LockInterface = false }, session.Preferences);
                registration.LifecycleWork = this.CompleteExpiryAsync(registration, restored: true);
            }
            this.Publish(registration, CountdownEffects.None, restoredExpiry: restored.ExpiredWhileClosed);
            return Success(session.Snapshot(sessionId));
        }, cancellationToken);
    }

    /// <summary>Drains currently committed work without preventing new commands from being processed.</summary>
    public async Task WaitForSessionEffectsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Task work = await this.InvokeAsync(() => this.sessions.TryGetValue(sessionId, out Registration? registration)
            ? Task.WhenAll(registration.LifecycleWork, registration.Effects?.Pending ?? Task.CompletedTask)
            : Task.WhenAll(this.retiredEffects), cancellationToken).ConfigureAwait(false);
        await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Publish(Registration registration, CountdownEffects effects, bool removed = false, bool restoredExpiry = false)
    {
        registration.Session.PublicationSequence = ++registration.Sequence;
        SessionNotification notification = new(registration.Session.Snapshot(registration.Id), registration.Sequence, effects, removed, restoredExpiry);
        registration.Subscriptions.RemoveAll(item => item.IsDisposed);
        foreach (SessionSubscription subscription in registration.Subscriptions) { subscription.Publish(notification); if (removed) { subscription.Complete(); } }
    }

    private void ProcessMetadataChange(Registration registration, TimerSessionSnapshot before)
    {
        TimerSession session = registration.Session;
        if (before.Options.LoopTimer != session.Options.LoopTimer || before.Options.CloseWhenExpired != session.Options.CloseWhenExpired
            || before.Options.LoopSound != session.Options.LoopSound || before.Options.ShutDownWhenExpired != session.Options.ShutDownWhenExpired)
        {
            registration.CompletionGeneration++;
            if (session.Countdown.State == TimerState.Expired) { session.InvalidateLifecycle(); registration.Effects?.SynchronizeRevision(); }
        }
        if (before.Options.AudioAlertsEnabled != session.Options.AudioAlertsEnabled || before.Options.AudioAlertSoundId != session.Options.AudioAlertSoundId
            || before.Options.LoopSound != session.Options.LoopSound)
        {
            _ = registration.Effects?.StopAudioAsync();
        }
        if (before.Options.DoNotKeepComputerAwake != session.Options.DoNotKeepComputerAwake && session.Countdown.State == TimerState.Running)
        {
            registration.LifecycleWork = registration.Effects?.AcquireInhibitionAsync(!session.Options.DoNotKeepComputerAwake, this.services.InhibitionReason) ?? Task.CompletedTask;
        }
    }

    private static bool HasEffect(CountdownEffects effects, CountdownEffect effect) =>
        effects.First == effect || effects.Second == effect || effects.Third == effect || effects.Fourth == effect;

    private void ProcessTransition(Registration registration, CountdownTransition transition)
    {
        if (!registration.Authoritative || registration.Effects == null) { return; }
        TimerSession session = registration.Session;
        SessionEffects effects = registration.Effects;
        effects.SynchronizeRevision();
        if (HasEffect(transition.Effects, CountdownEffect.Expired))
        {
            session.CommitMetadata(session.TimerInput, session.TimerTitle, session.Options with { LockInterface = false }, session.Preferences);
            registration.LifecycleWork = this.CompleteExpiryAsync(registration, restored: false);
        }
        else if (HasEffect(transition.Effects, CountdownEffect.Started) || HasEffect(transition.Effects, CountdownEffect.Resumed))
        {
            registration.LifecycleWork = Task.WhenAll(effects.StopAudioAsync(), effects.AcquireInhibitionAsync(!session.Options.DoNotKeepComputerAwake, this.services.InhibitionReason));
        }
        else if (HasEffect(transition.Effects, CountdownEffect.Stopped) || HasEffect(transition.Effects, CountdownEffect.Paused))
        {
            registration.LifecycleWork = Task.WhenAll(effects.StopAudioAsync(), effects.ReleaseInhibitionAsync());
        }
    }

    private Task CompleteExpiryAsync(Registration registration, bool restored)
    {
        TimerSession session = registration.Session;
        long revision = session.Revision;
        long generation = registration.CompletionGeneration;
        ExpiryDecision decision = ExpiryDecision.FromOptions(session.Options, session.Preferences, session.Countdown.SupportsRestart);
        ExpiryEffectRequest request = new(revision, session.Preferences.NotificationsEnabled,
            string.IsNullOrWhiteSpace(session.TimerTitle) ? this.services.ApplicationTitle : session.TimerTitle,
            this.services.CompletionBody, session.Options.AudioAlertsEnabled, session.Options.AudioAlertSoundId, session.Options.LoopSound, session.Options.ShutDownWhenExpired);
        Task<ExpiryCompletion> completion = registration.Effects?.CompleteAsync(request, decision, restored) ?? Task.FromResult(new ExpiryCompletion(revision, ExpiryAction.None));
        return FinishAsync();

        async Task FinishAsync()
        {
            ExpiryCompletion result = await completion.ConfigureAwait(false);
            if (restored || result.Action is ExpiryAction.None or ExpiryAction.Superseded) { return; }
            try
            {
                await this.InvokeAsync(() =>
                {
                    if (!session.IsCurrent(revision) || registration.CompletionGeneration != generation) { return false; }
                    if (result.Action == ExpiryAction.Restart)
                    {
                        CountdownTransition transition = session.Restart(this.wallClockNow());
                        this.ProcessTransition(registration, transition);
                        this.Publish(registration, transition.Effects);
                    }
                    else if (result.Action == ExpiryAction.Close && this.sessions.Remove(registration.Id))
                    {
                        this.Publish(registration, CountdownEffects.None, removed: true);
                        session.Dispose();
                        this.retiredEffects.Add(registration.Effects?.Close() ?? Task.CompletedTask);
                    }
                    return true;
                }).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (this.IsStopping) { }
        }
    }
}
