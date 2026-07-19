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
        string timerStartInput = "",
        string timerTitle = "",
        ActiveTimerPresentationMode presentationMode = ActiveTimerPresentationMode.Input,
        DateTime? savedAt = null,
        TimerState state = TimerState.Stopped,
        DateTime? startTime = null,
        DateTime? endTime = null,
        long? timeElapsedTicks = null,
        long? timeLeftTicks = null,
        long? timeExpiredTicks = null,
        long? totalTimeTicks = null,
        SavedTimerOptions? options = null,
        WindowGeometrySnapshot? windowGeometry = null)
    {
        this.Version = version;
        this.TimerInput = timerInput ?? string.Empty;
        this.TimerStartInput = timerStartInput ?? string.Empty;
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
        this.Options = options ?? new SavedTimerOptions();
        this.WindowGeometry = windowGeometry;
        this.HasOptions = options != null;
    }

    public int Version { get; }

    public string TimerInput { get; }

    public string TimerStartInput { get; }

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

    public SavedTimerOptions Options { get; }

    public WindowGeometrySnapshot? WindowGeometry { get; }

    [JsonIgnore]
    public bool HasOptions { get; }

    public static ActiveTimerSessionDocument FromTimerInfo(
        string timerInput,
        string timerTitle,
        ActiveTimerPresentationMode presentationMode,
        TimerInfo timerInfo,
        DateTime savedAt,
        SavedTimerOptions? options = null,
        WindowGeometrySnapshot? windowGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(timerInfo);

        return new ActiveTimerSessionDocument(
            CurrentVersion,
            timerInput,
            timerInfo.TimerStart?.TimerStartToken?.ToString() ?? timerInput,
            timerTitle,
            presentationMode,
            savedAt,
            timerInfo.State,
            timerInfo.StartTime,
            timerInfo.EndTime,
            timerInfo.TimeElapsed?.Ticks,
            timerInfo.TimeLeft?.Ticks,
            timerInfo.TimeExpired?.Ticks,
            timerInfo.TotalTime?.Ticks,
            options,
            windowGeometry);
    }

    public TimerInfo? ToTimerInfo(DateTime wallClockNow)
    {
        return ActiveTimerSessionSnapshot
            .FromDocument(this, wallClockNow, TimeSpan.Zero)
            ?.ToTimerInfo();
    }
}
