#nullable enable

namespace Hourglass.Timing;

using Hourglass.Serialization;

public sealed record CountdownState
{
    internal CountdownState(
        TimerState state,
        DateTime? startTime,
        DateTime? endTime,
        TimeSpan? timeElapsed,
        TimeSpan? timeLeft,
        TimeSpan? timeExpired,
        TimeSpan? totalTime,
        TimerStart? timerStart,
        bool canRestart,
        TimeSpan? restartDuration,
        TimeSpan runStartedAt,
        TimeSpan elapsedBeforeRun)
    {
        this.State = state;
        this.StartTime = startTime;
        this.EndTime = endTime;
        this.TimeElapsed = timeElapsed;
        this.TimeLeft = timeLeft;
        this.TimeExpired = timeExpired;
        this.TotalTime = totalTime;
        this.TimerStart = timerStart;
        this.CanRestart = canRestart;
        this.RestartDuration = restartDuration;
        this.RunStartedAt = runStartedAt;
        this.ElapsedBeforeRun = elapsedBeforeRun;
    }

    public static CountdownState Stopped { get; } = new(
        TimerState.Stopped,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        TimeSpan.Zero,
        TimeSpan.Zero);

    public TimerState State { get; }

    public DateTime? StartTime { get; }

    public DateTime? EndTime { get; }

    public TimeSpan? TimeElapsed { get; }

    public TimeSpan? TimeLeft { get; }

    public TimeSpan? TimeExpired { get; }

    public TimeSpan? TotalTime { get; }

    public TimerStart? TimerStart { get; }

    public bool CanRestart { get; }

    public TimeSpan? RestartDuration { get; }

    public TimeSpan RunStartedAt { get; }

    public TimeSpan ElapsedBeforeRun { get; }

    public bool SupportsRestart => this.CanRestart && (this.TimerStart != null || this.RestartDuration.HasValue);

    public static CountdownState FromTimerInfo(TimerInfo timerInfo, TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(timerInfo);

        TimerStart? timerStart = TimerStart.FromTimerStartInfo(timerInfo.TimerStart);
        var state = new CountdownState(
            timerInfo.State,
            timerInfo.StartTime,
            timerInfo.EndTime,
            timerInfo.TimeElapsed,
            timerInfo.TimeLeft,
            timerInfo.TimeExpired,
            timerInfo.TotalTime,
            timerStart,
            timerStart?.Type == TimerStartType.TimeSpan,
            null,
            timerInfo.State is TimerState.Running or TimerState.Expired ? monotonicNow : TimeSpan.Zero,
            timerInfo.State is TimerState.Running or TimerState.Expired ? timerInfo.TimeElapsed ?? TimeSpan.Zero : TimeSpan.Zero);

        if (state.State is TimerState.Running or TimerState.Expired)
        {
            return CountdownTransitions.Tick(state, monotonicNow).State;
        }

        return state;
    }

    public TimerInfo ToTimerInfo()
    {
        return new TimerInfo
        {
            State = this.State,
            StartTime = this.StartTime,
            EndTime = this.EndTime,
            TimeElapsed = this.TimeElapsed,
            TimeLeft = this.TimeLeft,
            TimeExpired = this.TimeExpired,
            TotalTime = this.TotalTime,
            TimerStart = this.TimerStart?.ToTimerStartInfo()
        };
    }
}
