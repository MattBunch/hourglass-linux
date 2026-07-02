namespace Hourglass.Core.Tests.Settings;

using System.Text.Json;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class SavedTimersDocumentTests
{
    [Fact]
    public void SavedTimerDocumentFiltersInvalidEntriesAndDuplicateIds()
    {
        var valid = new SavedTimerDefinition("timer-1", "10 seconds", "Tea");
        var duplicate = new SavedTimerDefinition("timer-1", "20 seconds", "Coffee");
        var invalid = new SavedTimerDefinition("timer-2", "not a timer", "Invalid");

        var document = new SavedTimersDocument(timers: [valid, duplicate, invalid]);

        SavedTimerDefinition timer = Assert.Single(document.Timers);
        Assert.Equal("timer-1", timer.Id);
        Assert.Equal("10 seconds", timer.TimerInput);
        Assert.Equal("Tea — 10 seconds", timer.Header);
    }

    [Fact]
    public void SavedTimerDocumentRoundTripsOptions()
    {
        var timer = new SavedTimerDefinition(
            "timer-1",
            "10 seconds",
            "Tea",
            options: new SavedTimerOptions(
                ReverseProgressBar: true,
                ShowTimeElapsed: true,
                LoopTimer: true,
                LoopSound: true,
                CloseWhenExpired: true,
                LockInterface: true,
                DoNotKeepComputerAwake: true,
                ShutDownWhenExpired: true));
        var document = new SavedTimersDocument(timers: [timer]);

        string json = JsonSerializer.Serialize(document);
        SavedTimersDocument? roundTripped = JsonSerializer.Deserialize<SavedTimersDocument>(json);

        Assert.NotNull(roundTripped);
        SavedTimerDefinition roundTrippedTimer = Assert.Single(roundTripped.Timers);
        Assert.Equal(timer, roundTrippedTimer);
    }

    [Fact]
    public void ActiveSessionDocumentRestoresExpiredRunningTimerWhenTargetPassed()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerTitle: "Tea",
            presentationMode: ActiveTimerPresentationMode.Status,
            savedAt: start.AddSeconds(5),
            state: TimerState.Running,
            startTime: start,
            endTime: end,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        var timerInfo = document.ToTimerInfo(end.AddSeconds(5));

        Assert.NotNull(timerInfo);
        Assert.Equal(TimerState.Expired, timerInfo.State);
        Assert.Equal(TimeSpan.Zero, timerInfo.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(5), timerInfo.TimeExpired);
        Assert.Equal(TimeSpan.FromSeconds(15), timerInfo.TimeElapsed);
    }

    [Fact]
    public void ActiveSessionDocumentRestoresPausedTimer()
    {
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        var timerInfo = document.ToTimerInfo(new DateTime(2026, 7, 2, 8, 0, 0));

        Assert.NotNull(timerInfo);
        Assert.Equal(TimerState.Paused, timerInfo.State);
        Assert.Equal(TimeSpan.FromSeconds(4), timerInfo.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(6), timerInfo.TimeLeft);
    }
}
