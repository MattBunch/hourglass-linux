namespace Hourglass.Application;

using Hourglass.Settings;
using Hourglass.Timing;

/// <summary>An owned immutable value; publication never exposes an engine or a persistence DTO.</summary>
public sealed record TimerSessionSnapshot(
    string SessionId,
    long Revision,
    string TimerInput,
    string TimerTitle,
    CountdownState Countdown,
    TimerDefaults Options,
    SessionActions AllowedActions);

[Flags]
public enum SessionActions
{
    None = 0,
    Start = 1,
    Pause = 2,
    Resume = 4,
    Stop = 8,
    Restart = 16,
    Update = 32,
    Dismiss = 64,
    Unlock = 128
}
