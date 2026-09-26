namespace Hourglass.Application;

using Hourglass.Settings;

/// <summary>Commands commit through the runtime queue. Revisions detect conflicting edits, not repaint ticks.</summary>
public abstract record SessionCommand(string SessionId)
{
    public sealed record Start(string SessionId, string TimerInput, string TimerTitle, TimerDefaults Options)
        : SessionCommand(SessionId);

    public sealed record Pause(string SessionId) : SessionCommand(SessionId);

    public sealed record Resume(string SessionId) : SessionCommand(SessionId);

    public sealed record Stop(string SessionId) : SessionCommand(SessionId);

    public sealed record Restart(string SessionId) : SessionCommand(SessionId);

    public sealed record Dismiss(string SessionId) : SessionCommand(SessionId);

    public sealed record Update(string SessionId, long ExpectedRevision, string TimerInput, string TimerTitle, TimerDefaults Options)
        : SessionCommand(SessionId);

    public sealed record Unlock(string SessionId) : SessionCommand(SessionId);
}
