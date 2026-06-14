namespace Hourglass.Linux.Avalonia;

using Hourglass.Timing;

public sealed record TimerViewState(
    string TimerInput,
    string RemainingTime,
    string StatusText,
    string PauseResumeText,
    bool IsInputEnabled,
    bool IsRunning,
    TimerState State,
    double ProgressPercent,
    bool IsTimerInputVisible,
    bool IsRemainingTimeVisible,
    bool IsCompletionTextVisible,
    bool IsStartVisible,
    bool IsPauseVisible,
    bool IsResumeVisible,
    bool IsStopVisible)
{
    internal const string DefaultTimerInput = "5 minutes";
    internal const string ReadyStatusText = "Ready";
    internal const string RunningStatusText = "Running";
    internal const string PausedStatusText = "Paused";
    internal const string TimerCompleteStatusText = "Timer complete";
    internal const string PauseCommandText = "Pause";
    internal const string ResumeCommandText = "Resume";

    public static TimerViewState Initial { get; } = FromTimerState(DefaultTimerInput, CountdownState.Stopped);

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
            timerState.State == TimerState.Paused ? ResumeCommandText : PauseCommandText,
            timerState.State == TimerState.Stopped,
            timerState.State == TimerState.Running,
            timerState.State,
            GetProgressPercent(timerState),
            timerState.State == TimerState.Stopped,
            timerState.State is TimerState.Running or TimerState.Paused,
            timerState.State == TimerState.Expired,
            timerState.State == TimerState.Stopped,
            timerState.State == TimerState.Running,
            timerState.State == TimerState.Paused,
            timerState.State is TimerState.Running or TimerState.Paused or TimerState.Expired);
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
            TimerState.Running => RunningStatusText,
            TimerState.Paused => PausedStatusText,
            TimerState.Expired => TimerCompleteStatusText,
            _ => ReadyStatusText
        };
    }

    public static double GetProgressPercent(CountdownState timerState)
    {
        ArgumentNullException.ThrowIfNull(timerState);

        if (timerState.State == TimerState.Expired)
        {
            return 100;
        }

        TimeSpan total = timerState.TotalTime ?? TimeSpan.Zero;
        if (timerState.State == TimerState.Stopped || total <= TimeSpan.Zero)
        {
            return 0;
        }

        TimeSpan elapsed = timerState.TimeElapsed ?? TimeSpan.Zero;
        double progress = elapsed.TotalMilliseconds / total.TotalMilliseconds * 100;

        if (double.IsNaN(progress) || double.IsInfinity(progress))
        {
            return 0;
        }

        return Math.Clamp(progress, 0, 100);
    }
}
