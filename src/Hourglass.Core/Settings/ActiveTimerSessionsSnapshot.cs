#nullable enable

namespace Hourglass.Settings;

public sealed record ActiveTimerSessionSnapshotDefinition
{
    public ActiveTimerSessionSnapshotDefinition(string sessionId, ActiveTimerSessionSnapshot? session)
    {
        this.SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();
        this.Session = session;
    }

    public string SessionId { get; }

    public ActiveTimerSessionSnapshot? Session { get; }

    public bool IsValid => this.Session != null;
}

public sealed record ActiveTimerSessionsSnapshot
{
    private readonly ActiveTimerSessionSnapshotDefinition[] sessions;

    public ActiveTimerSessionsSnapshot(ActiveTimerSessionSnapshotDefinition[]? sessions = null)
    {
        this.sessions = (sessions ?? [])
            .Where(session => session.IsValid)
            .GroupBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public ActiveTimerSessionSnapshotDefinition[] Sessions => this.sessions.ToArray();

    public static ActiveTimerSessionsSnapshot Empty { get; } = new();

    public static ActiveTimerSessionsSnapshot FromDocument(
        ActiveTimerSessionsDocument document,
        DateTime wallClockNow,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ActiveTimerSessionsSnapshot(
            document.Sessions
                .Select(session => new ActiveTimerSessionSnapshotDefinition(
                    session.SessionId,
                    session.Session == null
                        ? null
                        : ActiveTimerSessionSnapshot.FromDocument(session.Session, wallClockNow, monotonicNow)))
                .ToArray());
    }

    public ActiveTimerSessionsDocument ToDocument()
    {
        ActiveTimerSessionDefinition[] definitions = this.sessions
            .Where(session => session.Session != null)
            .Select(session => new ActiveTimerSessionDefinition(session.SessionId, session.Session!.ToDocument()))
            .ToArray();
        return new ActiveTimerSessionsDocument(sessions: definitions);
    }

    public ActiveTimerSessionsSnapshot AddOrReplace(string sessionId, ActiveTimerSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);

        ActiveTimerSessionSnapshotDefinition replacement = new(sessionId, session);
        ActiveTimerSessionSnapshotDefinition[] updated = this.sessions
            .Where(existing => !StringComparer.Ordinal.Equals(existing.SessionId, replacement.SessionId))
            .Append(replacement)
            .ToArray();
        return new ActiveTimerSessionsSnapshot(updated);
    }

    public ActiveTimerSessionsSnapshot Remove(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return this;
        }

        return new ActiveTimerSessionsSnapshot(
            this.sessions
                .Where(session => !StringComparer.Ordinal.Equals(session.SessionId, sessionId.Trim()))
                .ToArray());
    }
}
