namespace Hourglass.Application;

using Hourglass.Settings;
using System.Text.Json.Serialization;

/// <summary>Commands commit through the runtime queue. Revisions detect conflicting edits, not repaint ticks.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(SessionCommand.Start), "start")]
[JsonDerivedType(typeof(SessionCommand.Pause), "pause")]
[JsonDerivedType(typeof(SessionCommand.Resume), "resume")]
[JsonDerivedType(typeof(SessionCommand.Stop), "stop")]
[JsonDerivedType(typeof(SessionCommand.Restart), "restart")]
[JsonDerivedType(typeof(SessionCommand.Dismiss), "dismiss")]
[JsonDerivedType(typeof(SessionCommand.Update), "update")]
[JsonDerivedType(typeof(SessionCommand.Prepare), "prepare")]
[JsonDerivedType(typeof(SessionCommand.Unlock), "unlock")]
public abstract record SessionCommand(string SessionId)
{
    public sealed record Start(string SessionId, string? TimerInput = null, string? TimerTitle = null, TimerDefaults? Options = null)
        : SessionCommand(SessionId);

    public sealed record Pause(string SessionId) : SessionCommand(SessionId);

    public sealed record Resume(string SessionId) : SessionCommand(SessionId);

    public sealed record Stop(string SessionId) : SessionCommand(SessionId);

    public sealed record Restart(string SessionId) : SessionCommand(SessionId);

    public sealed record Dismiss(string SessionId) : SessionCommand(SessionId);

    public sealed record Update(string SessionId, long ExpectedRevision, string? TimerInput = null, string? TimerTitle = null, TimerDefaults? Options = null, ApplicationPreferences? Preferences = null)
        : SessionCommand(SessionId);

    public sealed record Prepare(string SessionId, string TimerInput, string TimerTitle, TimerDefaults Options, ApplicationPreferences Preferences)
        : SessionCommand(SessionId);

    public sealed record Unlock(string SessionId) : SessionCommand(SessionId);
}

public sealed record CreateSessionRequest(string SessionId, string TimerInput, string TimerTitle, TimerDefaults Options, ApplicationPreferences Preferences, SessionLifetime Lifetime = SessionLifetime.Gui)
{
    internal string? OwnerLeaseId { get; init; }
}
