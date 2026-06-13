namespace Hourglass.Linux.Avalonia;

using Hourglass.Timing;

public sealed record TimerViewState(
    string TimerInput,
    string RemainingTime,
    string StatusText,
    string PauseResumeText,
    bool IsInputEnabled,
    bool IsRunning,
    TimerState State)
{
    public static TimerViewState Initial { get; } = FromTimerState("5 minutes", CountdownState.Stopped);

    public static TimerViewState FromTimerState(
        string timerInput,
        CountdownState timerState,
        string? explicitStatus = null)
    {
        ArgumentNullException.ThrowIfNull(timerInput);
        ArgumentNullException.ThrowIfNull(timerState);

        return new TimerViewState(
            timerInput,
            FormatRemainingTime(timerState.TimeLeft ?? TimeSpan.Zero),
            explicitStatus ?? GetStatusText(timerState.State),
            timerState.State == TimerState.Paused ? "Resume" : "Pause",
            timerState.State == TimerState.Stopped,
            timerState.State == TimerState.Running,
            timerState.State);
    }

    public static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        int hours = (int)Math.Floor(remaining.TotalHours);
        return FormattableString.Invariant($"{hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}");
    }

    public static string GetStatusText(TimerState state)
    {
        return state switch
        {
            TimerState.Running => "Running",
            TimerState.Paused => "Paused",
            TimerState.Expired => "Timer complete",
            _ => "Ready"
        };
    }
}
