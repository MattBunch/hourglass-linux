using Hourglass.Application;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindowViewModel
{
    private long lastNotificationSequence;
    private readonly object presentationGate = new();

    private Task DispatchAsync(Action action) => this.uiDispatcher.InvokeAsync(() =>
    {
        lock (this.presentationGate) { action(); }
    });
    private async Task InitializeSessionAsync()
    {
        ApplicationResult<TimerSessionSnapshot> created = this.attachExistingSession
            ? await this.client.GetSessionAsync(this.SessionId).ConfigureAwait(false)
            : await this.client.CreateSessionAsync(new(this.SessionId,
                TimerViewState.DefaultTimerInput, string.Empty, TimerDefaults.FromSettings(this.settings), ApplicationPreferences.FromSettings(this.settings))).ConfigureAwait(false);
        if (created is not ApplicationResult<TimerSessionSnapshot>.Success success) { throw new InvalidOperationException(nameof(InitializeSessionAsync)); }
        ApplicationResult<SessionSubscription> subscribed = await this.client.SubscribeAsync(this.SessionId, notification => this.DispatchAsync(() => this.ApplyNotification(notification))).ConfigureAwait(false);
        if (subscribed is not ApplicationResult<SessionSubscription>.Success subscriptionResult) { throw new InvalidOperationException(nameof(InitializeSessionAsync)); }
        this.subscription = subscriptionResult.Value;
        _ = this.ObserveSubscriptionAsync(this.subscription);
        await this.DispatchAsync(() => { this.acknowledgedRevisions[0] = success.Value.Revision; this.ApplySnapshot(this.subscription.InitialSnapshot); if (this.attachExistingSession) { this.ReplaceViewState(this.viewState with { TimerInput = success.Value.TimerInput, TimerTitle = success.Value.TimerTitle }); } }).ConfigureAwait(false);
    }

    private async Task ObserveSubscriptionAsync(SessionSubscription subscription)
    {
        await subscription.Completion.ConfigureAwait(false);
        if (subscription.Failure != null && !this.isDisposed)
        {
            await this.DispatchAsync(() =>
            {
                this.isDisconnected = true;
                this.ReplaceViewState(this.viewState with { StatusText = ApplicationStrings.StatusRuntimeDisconnected, IsInputEnabled = false, HasValidationError = true });
                this.RefreshCommandAvailability();
            }).ConfigureAwait(false);
        }
    }

    private void QueueOperation(Func<Task> operation)
    {
        Task previous;
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (this.presentationGate)
        {
            previous = this.PendingCommands;
            Interlocked.Increment(ref this.pendingCommands);
            this.PendingCommands = RunAsync();
            this.RefreshCommandAvailability();
        }
        start.SetResult();
        async Task RunAsync()
        {
            try
            {
                await start.Task.ConfigureAwait(false);
                await previous.ConfigureAwait(false);
                Task current = Task.CompletedTask;
                await this.DispatchAsync(() => current = operation()).ConfigureAwait(false);
                await current.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                this.RecordBestEffort("application", "command", nameof(MainWindowViewModel), "Application command failed.", exception);
                await this.DispatchAsync(this.ShowValidationError).ConfigureAwait(false);
            }
            finally
            {
                await this.DispatchAsync(() =>
                {
                    Interlocked.Decrement(ref this.pendingCommands);
                    if (this.pendingCommands == 0)
                    {
                        this.acknowledgedRevisions.Clear();
                        this.ReplaceViewState(this.viewState with { TimerTitle = this.sessionSnapshot.TimerTitle });
                    }
                    this.RefreshCommandAvailability();
                }).ConfigureAwait(false);
            }
        }
    }

    private long ResolveAcknowledgedRevision(long revision)
    {
        while (this.acknowledgedRevisions.TryGetValue(revision, out long next) && next != revision) { revision = next; }
        return revision;
    }

    private void SubmitMetadataUpdate(string? title = null, TimerDefaults? options = null, ApplicationPreferences? preferences = null, LinuxAppSettings? previousSettings = null, LinuxAppSettings? requestedSettings = null)
    {
        long revision = this.sessionSnapshot.Revision;
        long? saveRevision = previousSettings != null && requestedSettings != null ? ++this.settingsSaveRevision : null;
        this.QueueOperation(async () =>
        {
            long expected = this.ResolveAcknowledgedRevision(revision);
            if (await this.ExecuteAndApplyAsync(new SessionCommand.Update(this.SessionId, expected, TimerTitle: title, Options: options, Preferences: preferences)).ConfigureAwait(false)
                && previousSettings != null && requestedSettings != null)
            {
                await this.DispatchAsync(() => this.QueueSettingsSave(previousSettings, requestedSettings, saveRevision)).ConfigureAwait(false);
            }
        });
    }

    private async Task<bool> ExecuteAndApplyAsync(SessionCommand command)
    {
        long previousRevision = command is SessionCommand.Update updateCommand ? updateCommand.ExpectedRevision : this.sessionSnapshot.Revision;
        ApplicationResult<TimerSessionSnapshot> result = await this.client.ExecuteAsync(command).ConfigureAwait(false);
        if (result is ApplicationResult<TimerSessionSnapshot>.Success success)
        {
            await this.DispatchAsync(() =>
            {
                long before = previousRevision;
                if (before != success.Value.Revision)
                {
                    this.acknowledgedRevisions[before] = success.Value.Revision;
                    if (this.editRevision == before) { this.editRevision = success.Value.Revision; }
                }
            }).ConfigureAwait(false);
        }
        return await this.ApplyResultAsync(result).ConfigureAwait(false);
    }

    private async Task<bool> ApplyResultAsync(ApplicationResult<TimerSessionSnapshot> result)
    {
        if (result is ApplicationResult<TimerSessionSnapshot>.Success success)
        {
            await this.DispatchAsync(() => this.ApplySnapshot(success.Value)).ConfigureAwait(false);
            return true;
        }
        ApplicationResult<TimerSessionSnapshot> latest = await this.client.GetSessionAsync(this.SessionId).ConfigureAwait(false);
        await this.DispatchAsync(() =>
        {
            if (latest is ApplicationResult<TimerSessionSnapshot>.Success current) { this.ApplySnapshot(current.Value, force: true); }
            this.ShowValidationError();
            if (result is ApplicationResult<TimerSessionSnapshot>.Failure failure)
            {
                if (failure.Error.Code is ApplicationErrorCode.TransportFailure or ApplicationErrorCode.RuntimeUnavailable) { this.isDisconnected = true; }
                this.RefreshDisplay(failure.Error.Code switch
                {
                    ApplicationErrorCode.Validation => ApplicationStrings.StatusInvalidTimer,
                    ApplicationErrorCode.Conflict => ApplicationStrings.StatusSessionConflict,
                    ApplicationErrorCode.Locked => ApplicationStrings.StatusSessionLocked,
                    ApplicationErrorCode.TransportFailure => ApplicationStrings.StatusRuntimeDisconnected,
                    ApplicationErrorCode.RuntimeUnavailable => ApplicationStrings.StatusRuntimeUnavailable,
                    ApplicationErrorCode.InvalidTransition => ApplicationStrings.StatusInvalidTransition,
                    _ => ApplicationStrings.StatusApplicationCommandFailed
                }, hasValidationError: true);
            }
        }).ConfigureAwait(false);
        return false;
    }

    private void ApplyNotification(SessionNotification notification)
    {
        if (this.isDisposed || this.automaticTicksSuspended || notification.Sequence <= this.lastNotificationSequence) { return; }
        this.lastNotificationSequence = notification.Sequence;
        this.ApplySnapshot(notification.Snapshot);
        if (notification.Removed) { PublishSafely(this.CloseRequested); return; }
        CountdownEffects effects = notification.Effects;
        bool expired = effects.First == CountdownEffect.Expired || effects.Second == CountdownEffect.Expired
            || effects.Third == CountdownEffect.Expired || effects.Fourth == CountdownEffect.Expired;
        if (expired || notification.RestoredExpiry)
        {
            if (this.sessionSnapshot.Countdown.State == TimerState.Expired)
            {
                this.editRevision = null;
                this.ReplaceViewState(this.viewState with { PresentationMode = TimerPresentationMode.Status, InputBeforeEdit = null, HasValidationError = false });
                this.RefreshDisplay(hasValidationError: false);
            }
            PublishSafely(this.ExpiryVisualFeedbackRequested);
            ExpiryDecision decision = ExpiryDecision.FromOptions(notification.Snapshot.Options, notification.Snapshot.Preferences, notification.Snapshot.Countdown.SupportsRestart);
            if (notification.RestoredExpiry ? notification.Snapshot.Preferences.PopUpWhenExpired : decision.RequestAttention) { PublishSafely(this.WindowAttentionRequested); }
        }
        this.QueueActiveSessionSave();
    }

    private void ApplySnapshot(TimerSessionSnapshot snapshot, bool force = false)
    {
        if (snapshot.PublicationSequence < this.sessionSnapshot.PublicationSequence || this.isDisposed || (!force && snapshot == this.sessionSnapshot)) { return; }
        CountdownState previous = this.sessionSnapshot.Countdown;
        TimerDefaults previousOptions = this.sessionSnapshot.Options;
        ApplicationPreferences previousPreferences = this.sessionSnapshot.Preferences;
        this.sessionSnapshot = snapshot;
        this.projectingSnapshot = true;
        try
        {
            LinuxAppSettings projected = new LinuxSettingsSnapshot(this.settings.RecentTimerInputs, snapshot.Preferences, snapshot.Options).ToSettings();
            this.ReplaceSettings(projected, save: this.settings.LockInterface && !projected.LockInterface);
            if (this.pendingCommands == 0) { this.ReplaceViewState(this.viewState with { TimerTitle = snapshot.TimerTitle }); }
            if (this.viewState.InputBeforeEdit == null && snapshot.Countdown.State != TimerState.Stopped)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = snapshot.TimerInput, PresentationMode = TimerPresentationMode.Status });
            }
            if (previous.State != snapshot.Countdown.State) { this.RefreshDisplay(hasValidationError: false); }
            else if (previous != snapshot.Countdown || previousOptions != snapshot.Options || previousPreferences != snapshot.Preferences) { this.RefreshDisplay(); }
        }
        finally { this.projectingSnapshot = false; }
    }

    private async Task DrainNotificationsAsync()
    {
        if (this.subscription != null) { await this.subscription.PendingDelivery.ConfigureAwait(false); }
        await this.DispatchAsync(() => { }).ConfigureAwait(false);
    }

    private async Task WaitForApplicationEffectsAsync()
    {
        await this.PendingCommands.ConfigureAwait(false);
        if (this.isDisposed) { await this.DisposeAsync().ConfigureAwait(false); if (this.subscription != null) { await this.subscription.Completion.ConfigureAwait(false); } return; }
        if (this.runtime != null) { await this.runtime.WaitForSessionEffectsAsync(this.SessionId).ConfigureAwait(false); }
        await this.DrainNotificationsAsync().ConfigureAwait(false);
        await this.PendingCommands.ConfigureAwait(false);
        if (this.isDisposed && this.subscription != null) { await this.subscription.Completion.ConfigureAwait(false); }
    }
}
