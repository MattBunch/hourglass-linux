namespace Hourglass.Tui;

using System.Collections.Immutable;
using Hourglass.Application;
using Hourglass.Settings;
using Hourglass.Timing;

public enum TuiMode { Dashboard, New, Edit, Conflict, Help, ConfirmQuit }

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

    public TuiState State => this.state;
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

    public void OpenNew() => this.Change(this.state with { Mode = TuiMode.New, Draft = new(string.Empty, string.Empty), Error = null });

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
    public void Back() => this.Change(this.state with { Mode = TuiMode.Dashboard, Draft = null, Error = null });
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

    private void Change(TuiState updated)
    {
        this.state = updated;
        this.Changed?.Invoke(updated);
    }
}
