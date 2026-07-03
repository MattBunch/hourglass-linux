#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;

public sealed record ActiveTimerSessionDefinition
{
    [JsonConstructor]
    public ActiveTimerSessionDefinition(string sessionId, ActiveTimerSessionDocument? session)
    {
        this.SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();
        this.Session = session;
    }

    public string SessionId { get; }

    public ActiveTimerSessionDocument? Session { get; }

    public bool IsValid => this.Session != null;
}

public sealed record ActiveTimerSessionsDocument
{
    public const int CurrentVersion = 1;

    [JsonConstructor]
    public ActiveTimerSessionsDocument(int version = CurrentVersion, ActiveTimerSessionDefinition[]? sessions = null)
    {
        this.Version = version;
        this.Sessions = (sessions ?? [])
            .Where(session => session.IsValid)
            .GroupBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public int Version { get; }

    public ActiveTimerSessionDefinition[] Sessions { get; }

    public static ActiveTimerSessionsDocument Empty { get; } = new();

    public ActiveTimerSessionsDocument AddOrReplace(string sessionId, ActiveTimerSessionDocument session)
    {
        ArgumentNullException.ThrowIfNull(session);

        ActiveTimerSessionDefinition replacement = new(sessionId, session);
        ActiveTimerSessionDefinition[] updated = this.Sessions
            .Where(existing => !StringComparer.Ordinal.Equals(existing.SessionId, replacement.SessionId))
            .Append(replacement)
            .ToArray();
        return new ActiveTimerSessionsDocument(CurrentVersion, updated);
    }

    public ActiveTimerSessionsDocument Remove(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return this;
        }

        return new ActiveTimerSessionsDocument(
            CurrentVersion,
            this.Sessions
                .Where(session => !StringComparer.Ordinal.Equals(session.SessionId, sessionId.Trim()))
                .ToArray());
    }
}
