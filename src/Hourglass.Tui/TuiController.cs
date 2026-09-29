namespace Hourglass.Tui;

using System.Collections.Immutable;
using Hourglass.Application;
using Hourglass.Settings;
using Hourglass.Timing;

public enum TuiMode { Dashboard, New, Edit, Save, Conflict, Help, ConfirmQuit, Saved, Recent, Settings, Options, ConfirmChange }

public sealed record TuiDraft(string TimerInput, string TimerTitle, string? SessionId = null, long Revision = 0);

public sealed record TuiState(
    ImmutableArray<TimerSessionSnapshot> Sessions,
    string? SelectedId,
    TuiMode Mode,
    TuiDraft? Draft,
    string? Error,
    bool Busy = false)
{
    public TimerSessionSnapshot? Selected => this.Sessions.FirstOrDefault(session => session.SessionId == this.SelectedId);
}

/// <summary>Frontend state only. Every timer operation is delegated to the shared client.</summary>
public sealed class TuiController(IHourglassClient client)
{
    private readonly HashSet<string> ownedIds = new(StringComparer.Ordinal);
    private TuiState state = new([], null, TuiMode.Dashboard, null, null);
    private int polling;
    private ImmutableArray<SavedTimerDefinition> savedTimers = [];
    private ImmutableArray<string> recentInputs = [];
    private LinuxAppSettings settings = LinuxAppSettings.Default;
    private int menuIndex;
    private string? pendingChange;
    private TuiMode returnMode = TuiMode.Dashboard;
    private CancellationTokenSource? previewCancellation;
    private Task previewTask = Task.CompletedTask;
    private long observedDiagnosticSequence;

    public TuiState State => this.state;
    public ImmutableArray<SavedTimerDefinition> SavedTimers => this.savedTimers;
    public ImmutableArray<string> RecentInputs => this.recentInputs;
    public LinuxAppSettings Settings => this.settings;
    public int MenuIndex => this.menuIndex;
    public string? PendingChange => this.pendingChange;
    public event Action<TuiState>? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref this.polling, 1) != 0) { return; }
        try
        {
            ApplicationResult<ImmutableArray<TimerSessionSnapshot>> result = await client.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure failure)
            {
                this.Change(this.state with { Error = failure.Error.Message });
                return;
            }
            ImmutableArray<TimerSessionSnapshot> sessions = ((ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success)result).Value
                .OrderBy(session => session.SessionId, StringComparer.Ordinal).ToImmutableArray();
            string? selected = this.state.SelectedId;
            if (!sessions.Any(session => session.SessionId == selected))
            {
                int previousIndex = -1;
                for (int index = 0; index < this.state.Sessions.Length; index++)
                {
                    if (this.state.Sessions[index].SessionId == selected) { previousIndex = index; break; }
                }
                selected = sessions.IsEmpty ? null : sessions[Math.Clamp(previousIndex, 0, sessions.Length - 1)].SessionId;
            }
            this.Change(this.state with { Sessions = sessions, SelectedId = selected });
            var diagnostics = await client.ListDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics is ApplicationResult<ImmutableArray<ApplicationDiagnostic>>.Success journal &&
                !journal.Value.IsEmpty && journal.Value[^1].Sequence > this.observedDiagnosticSequence)
            {
                this.observedDiagnosticSequence = journal.Value[^1].Sequence;
                this.Change(this.state with { Error = journal.Value[^1].Message });
            }
        }
        finally { Volatile.Write(ref this.polling, 0); }
    }

    public void SelectNext(int direction)
    {
        if (this.state.Mode != TuiMode.Dashboard || this.state.Sessions.IsEmpty) { return; }
        int current = -1;
        for (int index = 0; index < this.state.Sessions.Length; index++)
        {
            if (this.state.Sessions[index].SessionId == this.state.SelectedId) { current = index; break; }
        }
        int next = (current + direction + this.state.Sessions.Length) % this.state.Sessions.Length;
        this.Change(this.state with { SelectedId = this.state.Sessions[next].SessionId, Error = null });
    }

    public void OpenNew(string input = "") => this.Change(this.state with { Mode = TuiMode.New, Draft = new(input, string.Empty), Error = null });

    public void OpenEdit()
    {
        TimerSessionSnapshot? selected = this.state.Selected;
        if (selected == null) { this.ShowError("Select a session to edit."); return; }
        if (!selected.AllowedActions.HasFlag(SessionActions.Update) && !selected.AllowedActions.HasFlag(SessionActions.Rename))
        {
            this.ShowError("Unlock this session before editing its timer.");
            return;
        }
        this.Change(this.state with { Mode = TuiMode.Edit, Draft = new(selected.TimerInput, selected.TimerTitle, selected.SessionId, selected.Revision), Error = null });
    }

    public void SetDraft(string input, string title)
    {
        if (this.state.Draft is TuiDraft draft) { this.Change(this.state with { Draft = draft with { TimerInput = input, TimerTitle = title } }); }
    }

    public void ShowHelp() => this.Change(this.state with { Mode = TuiMode.Help, Error = null });
    public void Back()
    {
        this.previewCancellation?.Cancel();
        this.pendingChange = null;
        this.Change(this.state with { Mode = TuiMode.Dashboard, Draft = null, Error = null });
    }
    public void ShowError(string message) => this.Change(this.state with { Error = message });

    public void ResolveConflict(bool reload)
    {
        if (this.state.Mode != TuiMode.Conflict) { return; }
        TimerSessionSnapshot? selected = this.state.Sessions.FirstOrDefault(session => session.SessionId == this.state.Draft?.SessionId);
        if (reload && selected != null)
        {
            this.Change(this.state with { Mode = TuiMode.Edit, Draft = new(selected.TimerInput, selected.TimerTitle, selected.SessionId, selected.Revision), Error = null });
        }
        else { this.Change(this.state with { Mode = TuiMode.Edit, Error = null }); }
    }

    public async Task SubmitDraftAsync(CancellationToken cancellationToken = default)
    {
        if (this.state.Busy || this.state.Draft is not TuiDraft draft) { return; }
        this.Change(this.state with { Busy = true, Error = null });
        try
        {
            if (draft.SessionId == null)
            {
                ApplicationResult<LinuxAppSettings> settingsResult = await client.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                if (settingsResult is ApplicationResult<LinuxAppSettings>.Failure settingsFailure) { this.ShowError(settingsFailure.Error.Message); return; }
                LinuxAppSettings settings = ((ApplicationResult<LinuxAppSettings>.Success)settingsResult).Value;
                string id = Guid.NewGuid().ToString("N");
                ApplicationResult<TimerSessionSnapshot> created = await client.CreateSessionAsync(new(id, draft.TimerInput.Trim(), draft.TimerTitle,
                    TimerDefaults.FromSettings(settings), ApplicationPreferences.FromSettings(settings)), cancellationToken).ConfigureAwait(false);
                if (created is ApplicationResult<TimerSessionSnapshot>.Failure createFailure) { this.ShowError(createFailure.Error.Message); return; }
                this.ownedIds.Add(id);
                ApplicationResult<TimerSessionSnapshot> started = await client.ExecuteAsync(new SessionCommand.Start(id), cancellationToken).ConfigureAwait(false);
                if (started is ApplicationResult<TimerSessionSnapshot>.Failure startFailure)
                {
                    ApplicationResult<bool> cleanup = await client.CloseSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
                    if (cleanup is ApplicationResult<bool>.Success || cleanup is ApplicationResult<bool>.Failure { Error.Code: ApplicationErrorCode.NotFound })
                    {
                        this.ownedIds.Remove(id);
                    }
                    this.ShowError(cleanup is ApplicationResult<bool>.Failure cleanupFailure
                        ? $"{startFailure.Error.Message} Cleanup: {cleanupFailure.Error.Message}" : startFailure.Error.Message);
                    return;
                }
                ApplicationResult<bool> saved = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
                if (saved is ApplicationResult<bool>.Failure saveFailure) { this.ShowError(saveFailure.Error.Message); return; }
                await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
                this.Change(this.state with { Mode = TuiMode.Dashboard, Draft = null, SelectedId = id, Error = null });
            }
            else
            {
                TimerSessionSnapshot? selected = this.state.Sessions.FirstOrDefault(session => session.SessionId == draft.SessionId);
                string? replacement = selected?.TimerInput == draft.TimerInput ? null : draft.TimerInput.Trim();
                ApplicationResult<TimerSessionSnapshot> updated = await client.ExecuteAsync(new SessionCommand.Update(draft.SessionId,
                    draft.Revision, replacement, draft.TimerTitle), cancellationToken).ConfigureAwait(false);
                if (updated is ApplicationResult<TimerSessionSnapshot>.Failure failure)
                {
                    this.Change(this.state with
                    {
                        Mode = failure.Error.Code == ApplicationErrorCode.Conflict ? TuiMode.Conflict : TuiMode.Edit,
                        Error = failure.Error.Message
                    });
                    return;
                }
                ApplicationResult<bool> saved = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
                if (saved is ApplicationResult<bool>.Failure saveFailure) { this.ShowError(saveFailure.Error.Message); return; }
                await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
                this.Change(this.state with { Mode = TuiMode.Dashboard, Draft = null, Error = null });
            }
        }
        finally { this.Change(this.state with { Busy = false }); }
    }

    public async Task ActAsync(string action, CancellationToken cancellationToken = default)
    {
        if (this.state.Busy || this.state.Selected is not TimerSessionSnapshot selected) { return; }
        SessionCommand? command = action switch
        {
            "start" when selected.AllowedActions.HasFlag(SessionActions.Start) => new SessionCommand.Start(selected.SessionId),
            "toggle" when selected.AllowedActions.HasFlag(SessionActions.Pause) => new SessionCommand.Pause(selected.SessionId),
            "toggle" when selected.AllowedActions.HasFlag(SessionActions.Resume) => new SessionCommand.Resume(selected.SessionId),
            "stop" when selected.AllowedActions.HasFlag(SessionActions.Stop) => new SessionCommand.Stop(selected.SessionId),
            "restart" when selected.AllowedActions.HasFlag(SessionActions.Restart) => new SessionCommand.Restart(selected.SessionId),
            "dismiss" when selected.AllowedActions.HasFlag(SessionActions.Dismiss) => new SessionCommand.Dismiss(selected.SessionId),
            "unlock" when selected.AllowedActions.HasFlag(SessionActions.Unlock) => new SessionCommand.Unlock(selected.SessionId),
            _ => null
        };
        if (command == null) { this.ShowError("That action is unavailable for this session."); return; }
        this.Change(this.state with { Busy = true, Error = null });
        try
        {
            ApplicationResult<TimerSessionSnapshot> result = await client.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<TimerSessionSnapshot>.Failure failure) { this.ShowError(failure.Error.Message); return; }
            if (command is SessionCommand.Dismiss) { this.ownedIds.Remove(selected.SessionId); }
            ApplicationResult<bool> saved = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
            if (saved is ApplicationResult<bool>.Failure saveFailure) { this.ShowError(saveFailure.Error.Message); return; }
            await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { this.Change(this.state with { Busy = false }); }
    }

    public async Task<bool> RequestQuitAsync(CancellationToken cancellationToken = default)
    {
        if (this.state.Mode != TuiMode.ConfirmQuit && this.state.Sessions.Any(session => this.ownedIds.Contains(session.SessionId)
            && session.Preferences.PromptOnExit && session.Countdown.State is TimerState.Running or TimerState.Paused))
        {
            this.Change(this.state with { Mode = TuiMode.ConfirmQuit });
            return false;
        }
        return await this.CloseOwnedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CloseOwnedAsync(CancellationToken cancellationToken = default)
    {
        this.previewCancellation?.Cancel();
        try { await this.previewTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        ApplicationError? firstFailure = null;
        foreach (string id in this.ownedIds.Order(StringComparer.Ordinal).ToArray())
        {
            ApplicationResult<bool> closed = await client.CloseSessionAsync(id, cancellationToken).ConfigureAwait(false);
            if (closed is ApplicationResult<bool>.Failure failure && failure.Error.Code != ApplicationErrorCode.NotFound)
            {
                firstFailure ??= failure.Error;
            }
            else { this.ownedIds.Remove(id); }
        }
        if (firstFailure != null)
        {
            this.Change(this.state with { Mode = TuiMode.Dashboard, Error = firstFailure.Message });
            return false;
        }
        return true;
    }

    public async Task OpenSavedAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.ListSavedTimersAsync(cancellationToken).ConfigureAwait(false);
        if (result is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure failed) { this.ShowError(failed.Error.Message); return; }
        this.savedTimers = ((ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success)result).Value;
        this.menuIndex = 0;
        this.Change(this.state with { Mode = TuiMode.Saved, Error = null });
    }

    public async Task OpenRecentAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.ListRecentInputsAsync(cancellationToken).ConfigureAwait(false);
        if (result is ApplicationResult<ImmutableArray<string>>.Failure failed) { this.ShowError(failed.Error.Message); return; }
        this.recentInputs = ((ApplicationResult<ImmutableArray<string>>.Success)result).Value;
        this.menuIndex = 0;
        this.Change(this.state with { Mode = TuiMode.Recent, Error = null });
    }

    public async Task OpenSettingsAsync(bool sessionOptions, CancellationToken cancellationToken = default)
    {
        if (sessionOptions && this.state.Selected == null) { this.ShowError("Select a session first."); return; }
        if (!sessionOptions)
        {
            var result = await client.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<LinuxAppSettings>.Failure failed) { this.ShowError(failed.Error.Message); return; }
            this.settings = ((ApplicationResult<LinuxAppSettings>.Success)result).Value;
        }
        this.menuIndex = 0;
        this.Change(this.state with { Mode = sessionOptions ? TuiMode.Options : TuiMode.Settings, Error = null });
    }

    public void SelectMenu(int direction)
    {
        int count = this.state.Mode switch
        {
            TuiMode.Saved => this.savedTimers.Length,
            TuiMode.Recent => this.recentInputs.Length,
            TuiMode.Settings or TuiMode.Options => SharedOptionRegistry.Keys.Length,
            _ => 0
        };
        if (count == 0) { return; }
        this.menuIndex = (this.menuIndex + direction + count) % count;
        this.Change(this.state with { Error = null });
    }

    public void UseRecent()
    {
        if (this.state.Mode == TuiMode.Recent && this.menuIndex < this.recentInputs.Length)
        {
            this.OpenNew(this.recentInputs[this.menuIndex]);
        }
    }

    public void OpenSaveDraft()
    {
        TimerSessionSnapshot? session = this.state.Selected;
        this.Change(this.state with
        {
            Mode = TuiMode.Save,
            Draft = new(session?.TimerInput ?? string.Empty, session?.TimerTitle ?? string.Empty),
            Error = null
        });
    }

    public async Task SaveDraftAsync(CancellationToken cancellationToken = default)
    {
        if (this.state.Draft is not TuiDraft draft) { return; }
        var loaded = await client.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (loaded is ApplicationResult<LinuxAppSettings>.Failure failed) { this.ShowError(failed.Error.Message); return; }
        LinuxAppSettings settings = ((ApplicationResult<LinuxAppSettings>.Success)loaded).Value;
        if (this.state.Selected is TimerSessionSnapshot selected && selected.TimerInput == draft.TimerInput)
        {
            settings = new LinuxSettingsSnapshot([], selected.Preferences, selected.Options).ToSettings();
        }
        SavedTimerDefinition template = SavedTimerDefinition.Create(draft.TimerInput, draft.TimerTitle, settings, draft.TimerTitle);
        var changed = await client.ChangeSavedTimersAsync(new SavedTimerChange.Add(template), cancellationToken).ConfigureAwait(false);
        if (changed is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure invalid) { this.ShowError(invalid.Error.Message); return; }
        var durable = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
        if (durable is ApplicationResult<bool>.Failure storage) { this.ShowError(storage.Error.Message); return; }
        await this.OpenSavedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RunSavedAsync(bool all, CancellationToken cancellationToken = default)
    {
        if (!all && this.menuIndex >= this.savedTimers.Length) { this.ShowError("No saved timer selected."); return; }
        SavedTimerSelection selection = all ? new SavedTimerSelection.All()
            : new SavedTimerSelection.ByNameOrId(this.savedTimers[this.menuIndex].Id);
        var started = await client.StartSavedSessionsAsync(selection, cancellationToken).ConfigureAwait(false);
        if (started is ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure failed)
        {
            this.ShowError($"{failed.Error.Message} Refresh sessions before retrying.");
            await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        foreach (TimerSessionSnapshot session in ((ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success)started).Value)
        {
            this.ownedIds.Add(session.SessionId);
        }
        await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
        this.Change(this.state with { Mode = TuiMode.Dashboard, Error = null });
    }

    public void RequestChange(string change)
    {
        this.pendingChange = change;
        this.returnMode = this.state.Mode;
        this.Change(this.state with { Mode = TuiMode.ConfirmChange, Error = null });
    }

    public void CancelChange()
    {
        this.pendingChange = null;
        this.Change(this.state with { Mode = this.returnMode, Error = null });
    }

    public async Task ConfirmChangeAsync(CancellationToken cancellationToken = default)
    {
        string? change = this.pendingChange;
        this.pendingChange = null;
        if (change == null) { this.Back(); return; }
        if (change.StartsWith("saved:", StringComparison.Ordinal))
        {
            SavedTimerChange action = change == "saved:clear" ? new SavedTimerChange.Clear()
                : new SavedTimerChange.Remove(change[6..]);
            var result = await client.ChangeSavedTimersAsync(action, cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure failed) { this.ShowError(failed.Error.Message); return; }
            var durable = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
            if (durable is ApplicationResult<bool>.Failure storage) { this.ShowError(storage.Error.Message); return; }
            await this.OpenSavedAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var result = await client.ClearRecentInputsAsync(cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<ImmutableArray<string>>.Failure failed) { this.ShowError(failed.Error.Message); return; }
            var durable = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
            if (durable is ApplicationResult<bool>.Failure storage) { this.ShowError(storage.Error.Message); return; }
            await this.OpenRecentAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ToggleSettingAsync(CancellationToken cancellationToken = default)
    {
        if (this.state.Mode is not (TuiMode.Settings or TuiMode.Options)) { return; }
        string key = SharedOptionRegistry.Keys[this.menuIndex];
        bool sessionOptions = this.state.Mode == TuiMode.Options;
        TimerSessionSnapshot? selected = this.state.Selected;
        LinuxAppSettings current = sessionOptions && selected != null
            ? new LinuxSettingsSnapshot([], selected.Preferences, selected.Options).ToSettings() : this.settings;
        string previous = SharedOptionRegistry.Get(current, key) ?? string.Empty;
        string next;
        if (key == "audio-alert-sound-id")
        {
            AudioAlertSoundDefinition[] sounds = BuiltInAudioAlertSounds.All.ToArray();
            int index = Array.FindIndex(sounds, sound => sound.Id == previous);
            next = sounds[(index + 1) % sounds.Length].Id;
        }
        else { next = previous == "true" ? "false" : "true"; }
        var applied = SharedOptionRegistry.Apply(current, [new(key, next)]);
        if (applied is ApplicationResult<LinuxAppSettings>.Failure invalid) { this.ShowError(invalid.Error.Message); return; }
        LinuxAppSettings updated = ((ApplicationResult<LinuxAppSettings>.Success)applied).Value;
        if (sessionOptions && selected != null)
        {
            var result = await client.ExecuteAsync(new SessionCommand.Update(selected.SessionId, selected.Revision,
                Options: TimerDefaults.FromSettings(updated), Preferences: ApplicationPreferences.FromSettings(updated)), cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<TimerSessionSnapshot>.Failure failed) { this.ShowError(failed.Error.Message); return; }
            await this.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var result = await client.ChangeSettingsAsync(this.settings, updated, cancellationToken).ConfigureAwait(false);
            if (result is ApplicationResult<LinuxAppSettings>.Failure failed) { this.ShowError(failed.Error.Message); return; }
            this.settings = ((ApplicationResult<LinuxAppSettings>.Success)result).Value;
        }
        var durable = await client.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false);
        this.Change(this.state with { Error = durable is ApplicationResult<bool>.Failure storage ? storage.Error.Message : null });
    }

    public async Task PreviewSoundAsync(CancellationToken cancellationToken = default)
    {
        this.previewCancellation?.Cancel();
        try { await this.previewTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        LinuxAppSettings settings = this.state.Mode == TuiMode.Options && this.state.Selected is TimerSessionSnapshot selected
            ? new LinuxSettingsSnapshot([], selected.Preferences, selected.Options).ToSettings() : this.settings;
        this.previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource preview = this.previewCancellation;
        this.previewTask = PlayAsync();
        await this.previewTask.ConfigureAwait(false);
        async Task PlayAsync()
        {
            try
            {
                var result = await client.PreviewSoundAsync(settings.AudioAlertSoundId, preview.Token).ConfigureAwait(false);
                if (result is ApplicationResult<bool>.Failure failed) { this.ShowError(failed.Error.Message); }
            }
            catch (OperationCanceledException) when (preview.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(this.previewCancellation, preview)) { this.previewCancellation = null; }
                preview.Dispose();
            }
        }
    }

    private void Change(TuiState updated)
    {
        this.state = updated;
        this.Changed?.Invoke(updated);
    }
}
