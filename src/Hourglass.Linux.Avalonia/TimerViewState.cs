namespace Hourglass.Linux.Avalonia;

using Hourglass.Timing;
using Hourglass.Application;

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
    bool IsRestartVisible,
    bool IsCancelVisible,
    bool IsLocked,
    TimerPresentationMode PresentationMode,
    string? InputBeforeEdit,
    bool HasValidationError)
{
    internal static string DefaultTimerInput => TimerStart.Default.ToString();
    internal static string ReadyStatusText => ApplicationStrings.StatusReady;
    internal static string RunningStatusText => ApplicationStrings.StatusRunning;
    internal static string PausedStatusText => ApplicationStrings.StatusPaused;
    internal static string TimerCompleteStatusText => ApplicationStrings.StatusTimerComplete;
    internal static string PauseCommandText => ApplicationStrings.CommandPause;
    internal static string ResumeCommandText => ApplicationStrings.CommandResume;

    public static TimerViewState Initial { get; } = FromTimerState(DefaultTimerInput, CountdownState.Stopped);

    public bool HasCompletionEmphasis =>
        this.State == TimerState.Expired && this.PresentationMode == TimerPresentationMode.Status;

    public static TimerViewState FromTimerState(
        string timerInput,
        CountdownState timerState,
        string timerTitle = "",
        string? explicitStatus = null,
        TimerPresentationMode? presentationMode = null,
        string? inputBeforeEdit = null,
        bool hasValidationError = false,
        bool showTimeElapsed = false,
        bool reverseProgressBar = false,
        bool isLocked = false)
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
            FormatTimerTime(GetDisplayedTime(timerState, showTimeElapsed)),
            explicitStatus ?? GetStatusText(timerState.State),
            timerState.State == TimerState.Paused ? ResumeCommandText : PauseCommandText,
            isInputMode,
            timerState.State == TimerState.Running,
            timerState.State,
            GetProgressPercent(timerState, reverseProgressBar),
            isInputMode,
            !isInputMode && timerState.State is TimerState.Running or TimerState.Paused,
            !isInputMode && timerState.State == TimerState.Expired,
            isInputMode,
            !isLocked && !isInputMode && timerState.State == TimerState.Running,
            !isLocked && !isInputMode && timerState.State == TimerState.Paused,
            !isLocked && !isInputMode && timerState.State is TimerState.Running or TimerState.Paused or TimerState.Expired,
            !isLocked && !isInputMode && timerState.SupportsRestart,
            !isLocked && isInputMode && timerState.State is TimerState.Running or TimerState.Paused,
            isLocked,
            resolvedPresentationMode,
            inputBeforeEdit,
            hasValidationError);
    }

    public static string FormatRemainingTime(TimeSpan remaining)
    {
        return FormatTimerTime(remaining);
    }

    public static string FormatTimerTime(TimeSpan time)
    {
        return TimerDisplay.FormatTimerTime(time);
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

    public static double GetProgressPercent(CountdownState timerState, bool reverseProgressBar = false)
    {
        return TimerDisplay.GetProgressPercent(timerState, reverseProgressBar);
    }

    private static TimeSpan GetDisplayedTime(CountdownState timerState, bool showTimeElapsed)
    {
        if (showTimeElapsed)
        {
            return timerState.TimeElapsed ?? TimeSpan.Zero;
        }

        return timerState.TimeLeft ?? TimeSpan.Zero;
    }
}
