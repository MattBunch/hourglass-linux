#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;
using Hourglass.Serialization;
using Hourglass.Timing;

public enum ActiveTimerPresentationMode
{
    Input,
    Status
}

public sealed record ActiveTimerSessionDocument
{
    public const int CurrentVersion = 1;

    [JsonConstructor]
    public ActiveTimerSessionDocument(
        int version = CurrentVersion,
        string timerInput = "",
        string timerTitle = "",
        ActiveTimerPresentationMode presentationMode = ActiveTimerPresentationMode.Input,
        DateTime? savedAt = null,
        TimerState state = TimerState.Stopped,
        DateTime? startTime = null,
        DateTime? endTime = null,
        long? timeElapsedTicks = null,
        long? timeLeftTicks = null,
        long? timeExpiredTicks = null,
        long? totalTimeTicks = null)
    {
        this.Version = version;
        this.TimerInput = timerInput ?? string.Empty;
        this.TimerTitle = timerTitle ?? string.Empty;
        this.PresentationMode = presentationMode;
        this.SavedAt = savedAt;
        this.State = state;
        this.StartTime = startTime;
        this.EndTime = endTime;
        this.TimeElapsedTicks = timeElapsedTicks;
        this.TimeLeftTicks = timeLeftTicks;
        this.TimeExpiredTicks = timeExpiredTicks;
        this.TotalTimeTicks = totalTimeTicks;
    }

    public int Version { get; }

    public string TimerInput { get; }

    public string TimerTitle { get; }

    public ActiveTimerPresentationMode PresentationMode { get; }

    public DateTime? SavedAt { get; }

    public TimerState State { get; }

    public DateTime? StartTime { get; }

    public DateTime? EndTime { get; }

    public long? TimeElapsedTicks { get; }

    public long? TimeLeftTicks { get; }

    public long? TimeExpiredTicks { get; }

    public long? TotalTimeTicks { get; }

    public static ActiveTimerSessionDocument FromTimerInfo(
        string timerInput,
        string timerTitle,
        ActiveTimerPresentationMode presentationMode,
        TimerInfo timerInfo,
        DateTime savedAt)
    {
        ArgumentNullException.ThrowIfNull(timerInfo);

        return new ActiveTimerSessionDocument(
            CurrentVersion,
            timerInput,
            timerTitle,
            presentationMode,
            savedAt,
            timerInfo.State,
            timerInfo.StartTime,
            timerInfo.EndTime,
            timerInfo.TimeElapsed?.Ticks,
            timerInfo.TimeLeft?.Ticks,
            timerInfo.TimeExpired?.Ticks,
            timerInfo.TotalTime?.Ticks);
    }

    public TimerInfo? ToTimerInfo(DateTime wallClockNow)
    {
        TimerStart? timerStart = TimerStart.FromString(this.TimerInput);
        if (this.State != TimerState.Stopped && timerStart == null)
        {
            return null;
        }

        return this.State switch
        {
            TimerState.Running => this.ToRunningOrExpiredTimerInfo(wallClockNow, timerStart),
            TimerState.Paused => this.ToPausedTimerInfo(timerStart),
            TimerState.Expired => this.ToExpiredTimerInfo(wallClockNow, timerStart),
            _ => new TimerInfo { State = TimerState.Stopped }
        };
    }

    private TimerInfo? ToRunningOrExpiredTimerInfo(DateTime wallClockNow, TimerStart? timerStart)
    {
        if (!this.StartTime.HasValue || !this.EndTime.HasValue || !this.TotalTimeTicks.HasValue)
        {
            return null;
        }

        TimeSpan totalTime = new(this.TotalTimeTicks.Value);
        if (this.EndTime.Value <= wallClockNow)
        {
            TimeSpan timeExpired = wallClockNow - this.EndTime.Value;
            return new TimerInfo
            {
                State = TimerState.Expired,
                StartTime = this.StartTime,
                EndTime = this.EndTime,
                TimeElapsed = totalTime + timeExpired,
                TimeLeft = TimeSpan.Zero,
                TimeExpired = timeExpired,
                TotalTime = totalTime,
                TimerStart = timerStart?.ToTimerStartInfo()
            };
        }

        TimeSpan timeElapsed = wallClockNow - this.StartTime.Value;
        if (timeElapsed < TimeSpan.Zero)
        {
            timeElapsed = TimeSpan.Zero;
        }

        return new TimerInfo
        {
            State = TimerState.Running,
            StartTime = this.StartTime,
            EndTime = this.EndTime,
            TimeElapsed = timeElapsed,
            TimeLeft = this.EndTime.Value - wallClockNow,
            TimeExpired = TimeSpan.Zero,
            TotalTime = totalTime,
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }

    private TimerInfo? ToPausedTimerInfo(TimerStart? timerStart)
    {
        if (!this.TimeElapsedTicks.HasValue || !this.TimeLeftTicks.HasValue || !this.TotalTimeTicks.HasValue)
        {
            return null;
        }

        return new TimerInfo
        {
            State = TimerState.Paused,
            StartTime = this.StartTime,
            EndTime = this.EndTime,
            TimeElapsed = new TimeSpan(this.TimeElapsedTicks.Value),
            TimeLeft = new TimeSpan(this.TimeLeftTicks.Value),
            TimeExpired = TimeSpan.Zero,
            TotalTime = new TimeSpan(this.TotalTimeTicks.Value),
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }

    private TimerInfo? ToExpiredTimerInfo(DateTime wallClockNow, TimerStart? timerStart)
    {
        if (!this.StartTime.HasValue || !this.EndTime.HasValue || !this.TotalTimeTicks.HasValue)
        {
            return null;
        }

        TimeSpan totalTime = new(this.TotalTimeTicks.Value);
        TimeSpan timeExpired = this.EndTime.Value <= wallClockNow
            ? wallClockNow - this.EndTime.Value
            : new TimeSpan(this.TimeExpiredTicks ?? 0);

        return new TimerInfo
        {
            State = TimerState.Expired,
            StartTime = this.StartTime,
            EndTime = this.EndTime,
            TimeElapsed = totalTime + timeExpired,
            TimeLeft = TimeSpan.Zero,
            TimeExpired = timeExpired,
            TotalTime = totalTime,
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }
}
