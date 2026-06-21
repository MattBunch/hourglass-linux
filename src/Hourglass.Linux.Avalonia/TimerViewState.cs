namespace Hourglass.Linux.Avalonia;

using Hourglass.Timing;

public enum TimerPresentationMode
{
    Input,
    Status
}

public sealed record TimerViewState(
    string TimerInput,
    string TimerTitle,
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
    bool IsStopVisible,
    bool IsCancelVisible,
    TimerPresentationMode PresentationMode,
    string? InputBeforeEdit)
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
        string timerTitle = "",
        string? explicitStatus = null,
        TimerPresentationMode? presentationMode = null,
        string? inputBeforeEdit = null)
    {
        ArgumentNullException.ThrowIfNull(timerInput);
        ArgumentNullException.ThrowIfNull(timerState);
        ArgumentNullException.ThrowIfNull(timerTitle);

        TimerPresentationMode resolvedPresentationMode = presentationMode
            ?? (timerState.State == TimerState.Stopped ? TimerPresentationMode.Input : TimerPresentationMode.Status);
        bool isInputMode = resolvedPresentationMode == TimerPresentationMode.Input;

        return new TimerViewState(
            timerInput,
            timerTitle,
            FormatRemainingTime(timerState.TimeLeft ?? TimeSpan.Zero),
            explicitStatus ?? GetStatusText(timerState.State),
            timerState.State == TimerState.Paused ? ResumeCommandText : PauseCommandText,
            isInputMode,
            timerState.State == TimerState.Running,
            timerState.State,
            GetProgressPercent(timerState),
            isInputMode,
            !isInputMode && timerState.State is TimerState.Running or TimerState.Paused,
            !isInputMode && timerState.State == TimerState.Expired,
            isInputMode,
            !isInputMode && timerState.State == TimerState.Running,
            !isInputMode && timerState.State == TimerState.Paused,
            !isInputMode && timerState.State is TimerState.Running or TimerState.Paused or TimerState.Expired,
            isInputMode && timerState.State is TimerState.Running or TimerState.Paused,
            resolvedPresentationMode,
            inputBeforeEdit);
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
