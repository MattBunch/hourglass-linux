#nullable enable

namespace Hourglass.Settings;

using Hourglass.Serialization;
using Hourglass.Timing;

public sealed record ActiveTimerSessionSnapshot(
    string TimerInput,
    string TimerStartInput,
    string TimerTitle,
    ActiveTimerPresentationMode PresentationMode,
    DateTime? SavedAt,
    TimerState SavedState,
    CountdownState CountdownState,
    SavedTimerOptions Options,
    WindowGeometrySnapshot? WindowGeometry,
    bool HasOptions)
{
    public string TimerInput { get; init; } = TimerInput ?? string.Empty;

    public string TimerStartInput { get; init; } = TimerStartInput ?? string.Empty;

    public string TimerTitle { get; init; } = TimerTitle ?? string.Empty;

    public SavedTimerOptions Options { get; init; } = Options ?? new SavedTimerOptions();

    public static ActiveTimerSessionSnapshot? FromDocument(
        ActiveTimerSessionDocument document,
        DateTime wallClockNow,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(document);

        TimerInfo? timerInfo = CreateTimerInfo(document, wallClockNow);
        if (timerInfo == null)
        {
            return null;
        }

        return new ActiveTimerSessionSnapshot(
            document.TimerInput,
            string.IsNullOrWhiteSpace(document.TimerStartInput) ? document.TimerInput : document.TimerStartInput,
            document.TimerTitle,
            document.PresentationMode,
            document.SavedAt,
            document.State,
            CountdownState.FromTimerInfo(timerInfo, monotonicNow),
            document.Options,
            document.WindowGeometry,
            document.HasOptions);
    }

    public static ActiveTimerSessionSnapshot FromState(
        string timerInput,
        string timerTitle,
        ActiveTimerPresentationMode presentationMode,
        CountdownState timerState,
        DateTime savedAt,
        SavedTimerOptions? options = null,
        WindowGeometrySnapshot? windowGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(timerState);

        return new ActiveTimerSessionSnapshot(
            timerInput,
            timerState.TimerStart?.ToString() ?? timerInput,
            timerTitle,
            presentationMode,
            savedAt,
            timerState.State,
            timerState,
            options ?? new SavedTimerOptions(),
            windowGeometry,
            options != null);
    }

    public ActiveTimerSessionDocument ToDocument()
    {
        TimerInfo timerInfo = this.ToTimerInfo();
        return new ActiveTimerSessionDocument(
            ActiveTimerSessionDocument.CurrentVersion,
            this.TimerInput,
            string.IsNullOrWhiteSpace(this.TimerStartInput) ? this.TimerInput : this.TimerStartInput,
            this.TimerTitle,
            this.PresentationMode,
            this.SavedAt,
            timerInfo.State,
            timerInfo.StartTime,
            timerInfo.EndTime,
            timerInfo.TimeElapsed?.Ticks,
            timerInfo.TimeLeft?.Ticks,
            timerInfo.TimeExpired?.Ticks,
            timerInfo.TotalTime?.Ticks,
            this.HasOptions ? this.Options : null,
            this.WindowGeometry);
    }

    public TimerInfo ToTimerInfo()
    {
        TimerInfo timerInfo = this.CountdownState.ToTimerInfo();
        if (timerInfo.State == TimerState.Expired && timerInfo.TotalTime.HasValue)
        {
            timerInfo.TimeElapsed = timerInfo.TotalTime.Value + (timerInfo.TimeExpired ?? TimeSpan.Zero);
        }

        return timerInfo;
    }

    public bool ExpiredWhileClosed => this.SavedState == TimerState.Running && this.CountdownState.State == TimerState.Expired;

    private static TimerInfo? CreateTimerInfo(ActiveTimerSessionDocument document, DateTime wallClockNow)
    {
        TimerStart? timerStart = GetTimerStart(document);
        if (document.State != TimerState.Stopped && timerStart == null)
        {
            return null;
        }

        return document.State switch
        {
            TimerState.Running => CreateRunningOrExpiredTimerInfo(document, wallClockNow, timerStart),
            TimerState.Paused => CreatePausedTimerInfo(document, timerStart),
            TimerState.Expired => CreateExpiredTimerInfo(document, wallClockNow, timerStart),
            _ => new TimerInfo { State = TimerState.Stopped }
        };
    }

    private static TimerInfo? CreateRunningOrExpiredTimerInfo(
        ActiveTimerSessionDocument document,
        DateTime wallClockNow,
        TimerStart? timerStart)
    {
        if (!document.StartTime.HasValue || !document.EndTime.HasValue || !document.TotalTimeTicks.HasValue)
        {
            return null;
        }

        TimeSpan totalTime = new(document.TotalTimeTicks.Value);
        if (document.EndTime.Value <= wallClockNow)
        {
            TimeSpan timeExpired = wallClockNow - document.EndTime.Value;
            return new TimerInfo
            {
                State = TimerState.Expired,
                StartTime = document.StartTime,
                EndTime = document.EndTime,
                TimeElapsed = totalTime + timeExpired,
                TimeLeft = TimeSpan.Zero,
                TimeExpired = timeExpired,
                TotalTime = totalTime,
                TimerStart = timerStart?.ToTimerStartInfo()
            };
        }

        TimeSpan timeElapsed = wallClockNow - document.StartTime.Value;
        if (timeElapsed < TimeSpan.Zero)
        {
            timeElapsed = TimeSpan.Zero;
        }

        return new TimerInfo
        {
            State = TimerState.Running,
            StartTime = document.StartTime,
            EndTime = document.EndTime,
            TimeElapsed = timeElapsed,
            TimeLeft = document.EndTime.Value - wallClockNow,
            TimeExpired = TimeSpan.Zero,
            TotalTime = totalTime,
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }

    private static TimerInfo? CreatePausedTimerInfo(ActiveTimerSessionDocument document, TimerStart? timerStart)
    {
        if (!document.TimeElapsedTicks.HasValue || !document.TimeLeftTicks.HasValue || !document.TotalTimeTicks.HasValue)
        {
            return null;
        }

        return new TimerInfo
        {
            State = TimerState.Paused,
            StartTime = document.StartTime,
            EndTime = document.EndTime,
            TimeElapsed = new TimeSpan(document.TimeElapsedTicks.Value),
            TimeLeft = new TimeSpan(document.TimeLeftTicks.Value),
            TimeExpired = TimeSpan.Zero,
            TotalTime = new TimeSpan(document.TotalTimeTicks.Value),
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }

    private static TimerInfo? CreateExpiredTimerInfo(
        ActiveTimerSessionDocument document,
        DateTime wallClockNow,
        TimerStart? timerStart)
    {
        if (!document.StartTime.HasValue || !document.EndTime.HasValue || !document.TotalTimeTicks.HasValue)
        {
            return null;
        }

        TimeSpan totalTime = new(document.TotalTimeTicks.Value);
        TimeSpan timeExpired = document.EndTime.Value <= wallClockNow
            ? wallClockNow - document.EndTime.Value
            : new TimeSpan(document.TimeExpiredTicks ?? 0);

        return new TimerInfo
        {
            State = TimerState.Expired,
            StartTime = document.StartTime,
            EndTime = document.EndTime,
            TimeElapsed = totalTime + timeExpired,
            TimeLeft = TimeSpan.Zero,
            TimeExpired = timeExpired,
            TotalTime = totalTime,
            TimerStart = timerStart?.ToTimerStartInfo()
        };
    }

    private static TimerStart? GetTimerStart(ActiveTimerSessionDocument document)
    {
        string timerStartInput = string.IsNullOrWhiteSpace(document.TimerStartInput)
            ? document.TimerInput
            : document.TimerStartInput;
        return TimerStart.FromString(timerStartInput);
    }
}
