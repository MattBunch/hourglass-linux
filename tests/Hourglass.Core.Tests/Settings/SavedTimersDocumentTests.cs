namespace Hourglass.Core.Tests.Settings;

using System.Text.Json;
using Hourglass.Serialization;
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
                ShutDownWhenExpired: true,
                WindowTitleMode: WindowTitleMode.TimerTitlePlusTimeLeft,
                AudioAlertSoundId: BuiltInAudioAlertSounds.LoudBeep,
                CustomThemeId: "theme-1"));
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
    public void ActiveSessionSnapshotRestoresRunningTimerBeforeTarget()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "10 seconds",
            timerTitle: "Tea",
            state: TimerState.Running,
            startTime: start,
            endTime: end,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            start.AddSeconds(4),
            TimeSpan.Zero);

        Assert.NotNull(snapshot);
        Assert.Equal(TimerState.Running, snapshot.CountdownState.State);
        Assert.Equal(TimerState.Running, snapshot.SavedState);
        Assert.False(snapshot.ExpiredWhileClosed);
        Assert.Equal(TimeSpan.FromSeconds(4), snapshot.CountdownState.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(6), snapshot.CountdownState.TimeLeft);
    }

    [Fact]
    public void ActiveSessionSnapshotMarksRunningTimerExpiredAfterTarget()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "10 seconds",
            timerTitle: "Tea",
            state: TimerState.Running,
            startTime: start,
            endTime: end,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            end.AddSeconds(5),
            TimeSpan.Zero);

        Assert.NotNull(snapshot);
        Assert.Equal(TimerState.Expired, snapshot.CountdownState.State);
        Assert.True(snapshot.ExpiredWhileClosed);
        Assert.Equal(TimeSpan.Zero, snapshot.CountdownState.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.CountdownState.TimeExpired);
    }

    [Fact]
    public void ActiveSessionSnapshotRestoresPausedTimerWithoutWallClockDrift()
    {
        DateTime savedAt = new(2026, 7, 2, 8, 0, 0);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "10 seconds",
            state: TimerState.Paused,
            savedAt: savedAt,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            savedAt.AddHours(2),
            TimeSpan.FromHours(2));

        Assert.NotNull(snapshot);
        Assert.Equal(TimerState.Paused, snapshot.CountdownState.State);
        Assert.Equal(TimeSpan.FromSeconds(4), snapshot.CountdownState.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(6), snapshot.CountdownState.TimeLeft);
    }

    [Fact]
    public void ActiveSessionSnapshotRestoresAlreadyExpiredTimerWithoutExpiredWhileClosedFlag()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "10 seconds",
            state: TimerState.Expired,
            startTime: start,
            endTime: end,
            timeElapsedTicks: TimeSpan.FromSeconds(15).Ticks,
            timeLeftTicks: TimeSpan.Zero.Ticks,
            timeExpiredTicks: TimeSpan.FromSeconds(5).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            end.AddSeconds(15),
            TimeSpan.Zero);

        Assert.NotNull(snapshot);
        Assert.Equal(TimerState.Expired, snapshot.CountdownState.State);
        Assert.False(snapshot.ExpiredWhileClosed);
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.CountdownState.TimeExpired);
    }

    [Fact]
    public void ActiveSessionSnapshotRejectsActiveSessionWithoutValidStartExpression()
    {
        var document = new ActiveTimerSessionDocument(
            timerInput: "not valid",
            timerStartInput: "also not valid",
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            new DateTime(2026, 7, 2, 8, 0, 0),
            TimeSpan.Zero);

        Assert.Null(snapshot);
    }

    [Fact]
    public void ActiveSessionSnapshotRoundTripsDocumentStateOptionsAndGeometry()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var geometry = new WindowGeometrySnapshot(10, 20, 420, 240, WindowGeometryState.Maximized);
        var options = new SavedTimerOptions(
            ReverseProgressBar: true,
            ShowTimeElapsed: true,
            WindowTitleMode: WindowTitleMode.TimeElapsedPlusTimerTitle,
            AudioAlertSoundId: BuiltInAudioAlertSounds.QuietBeep,
            CustomThemeId: "theme-1");
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "10 seconds",
            timerTitle: "Tea",
            presentationMode: ActiveTimerPresentationMode.Status,
            savedAt: start.AddSeconds(4),
            state: TimerState.Running,
            startTime: start,
            endTime: end,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks,
            options: options,
            windowGeometry: geometry);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            start.AddSeconds(4),
            TimeSpan.Zero);
        ActiveTimerSessionDocument? roundTripped = snapshot?.ToDocument();

        Assert.NotNull(roundTripped);
        Assert.Equal("10 seconds", roundTripped.TimerInput);
        Assert.Equal("10 seconds", roundTripped.TimerStartInput);
        Assert.Equal("Tea", roundTripped.TimerTitle);
        Assert.Equal(ActiveTimerPresentationMode.Status, roundTripped.PresentationMode);
        Assert.True(roundTripped.HasOptions);
        Assert.Equal(options, roundTripped.Options);
        Assert.Equal(geometry, roundTripped.WindowGeometry);
        Assert.Equal(TimerState.Running, roundTripped.State);
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, roundTripped.TimeElapsedTicks);
        Assert.Equal(TimeSpan.FromSeconds(6).Ticks, roundTripped.TimeLeftTicks);
    }

    [Fact]
    public void ActiveSessionSnapshotPreservesOlderDocumentWithoutOptionsOrGeometry()
    {
        const string json = """
            {
              "Version": 1,
              "TimerInput": "10 seconds",
              "State": 2,
              "TimeElapsedTicks": 40000000,
              "TimeLeftTicks": 60000000,
              "TotalTimeTicks": 100000000
            }
            """;

        ActiveTimerSessionDocument? document = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(json);
        Assert.NotNull(document);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            new DateTime(2026, 7, 2, 8, 0, 0),
            TimeSpan.Zero);
        ActiveTimerSessionDocument? roundTripped = snapshot?.ToDocument();

        Assert.NotNull(snapshot);
        Assert.False(snapshot.HasOptions);
        Assert.Null(snapshot.WindowGeometry);
        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.HasOptions);
        Assert.Equal(new SavedTimerOptions(), roundTripped.Options);
        Assert.Null(roundTripped.WindowGeometry);
    }

    [Fact]
    public void ActiveSessionSnapshotFromStateCreatesPersistenceDocument()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        CountdownState timerState = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            start,
            TimeSpan.Zero).State;
        var options = new SavedTimerOptions(LoopTimer: true);
        var geometry = new WindowGeometrySnapshot(10, 20, 420, 240);

        ActiveTimerSessionDocument document = ActiveTimerSessionSnapshot.FromState(
            "10 seconds",
            "Tea",
            ActiveTimerPresentationMode.Input,
            timerState,
            start,
            options,
            geometry).ToDocument();

        Assert.Equal("10 seconds", document.TimerInput);
        Assert.Equal("Tea", document.TimerTitle);
        Assert.Equal(TimerState.Running, document.State);
        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, document.TotalTimeTicks);
        Assert.True(document.HasOptions);
        Assert.Equal(options, document.Options);
        Assert.Equal(geometry, document.WindowGeometry);
    }

    [Fact]
    public void ActiveSessionDocumentRoundTripsOptions()
    {
        var options = new SavedTimerOptions(
            ReverseProgressBar: true,
            ShowTimeElapsed: true,
            LoopTimer: true,
            LoopSound: true,
            CloseWhenExpired: true,
            LockInterface: true,
                DoNotKeepComputerAwake: true,
                ShutDownWhenExpired: true,
                WindowTitleMode: WindowTitleMode.TimeElapsedPlusTimerTitle,
                AudioAlertSoundId: BuiltInAudioAlertSounds.QuietBeep,
                CustomThemeId: "theme-1");
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            options: options);

        string json = JsonSerializer.Serialize(document);
        ActiveTimerSessionDocument? roundTripped = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(json);

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.HasOptions);
        Assert.Equal(options, roundTripped.Options);
    }

    [Fact]
    public void SavedTimerOptionsApplyCustomThemeSelection()
    {
        var options = new SavedTimerOptions(CustomThemeId: "theme-1");

        LinuxAppSettings settings = options.ApplyTo(LinuxAppSettings.Default);

        Assert.Equal(LinuxThemePreference.Custom, settings.ThemePreference);
        Assert.Equal("theme-1", settings.CustomThemeId);
    }

    [Fact]
    public void ActiveSessionDocumentRoundTripsWindowGeometry()
    {
        var geometry = new WindowGeometrySnapshot(10, 20, 420, 240, WindowGeometryState.Maximized);
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            windowGeometry: geometry);

        string json = JsonSerializer.Serialize(document);
        ActiveTimerSessionDocument? roundTripped = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(geometry, roundTripped.WindowGeometry);
    }

    [Fact]
    public void OlderActiveSessionJsonWithoutWindowGeometryStillLoads()
    {
        const string json = """{"TimerInput":"10 seconds"}""";

        ActiveTimerSessionDocument? document = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(json);

        Assert.NotNull(document);
        Assert.Null(document.WindowGeometry);
        Assert.Equal("10 seconds", document.TimerInput);
    }

    [Fact]
    public void WindowGeometrySnapshotNormalizesInvalidValues()
    {
        var geometry = new WindowGeometrySnapshot(
            double.NaN,
            double.PositiveInfinity,
            -1,
            double.NegativeInfinity);

        Assert.Equal(0, geometry.X);
        Assert.Equal(0, geometry.Y);
        Assert.Equal(WindowGeometrySnapshot.MinimumWidth, geometry.Width);
        Assert.Equal(WindowGeometrySnapshot.MinimumHeight, geometry.Height);
    }

    [Fact]
    public void LegacySavedTimerOptionsPreserveCurrentSoundSelection()
    {
        LinuxAppSettings disabledSettings = LinuxAppSettings.Default with
        {
            AudioAlertsEnabled = false,
            AudioAlertSoundId = BuiltInAudioAlertSounds.None
        };
        LinuxAppSettings quietSettings = LinuxAppSettings.Default with
        {
            AudioAlertSoundId = BuiltInAudioAlertSounds.QuietBeep
        };
        var legacyOptions = new SavedTimerOptions(ReverseProgressBar: true);

        LinuxAppSettings appliedToDisabled = legacyOptions.ApplyTo(disabledSettings);
        LinuxAppSettings appliedToQuiet = legacyOptions.ApplyTo(quietSettings);

        Assert.True(appliedToDisabled.ReverseProgressBar);
        Assert.False(appliedToDisabled.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.None, appliedToDisabled.AudioAlertSoundId);
        Assert.True(appliedToQuiet.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.QuietBeep, appliedToQuiet.AudioAlertSoundId);
    }

    [Fact]
    public void SavedTimerOptionsApplySoundSelectionCoherently()
    {
        LinuxAppSettings disabledSettings = LinuxAppSettings.Default with
        {
            AudioAlertsEnabled = false,
            AudioAlertSoundId = BuiltInAudioAlertSounds.None
        };
        var quietOptions = new SavedTimerOptions(AudioAlertSoundId: BuiltInAudioAlertSounds.QuietBeep);
        var noSoundOptions = new SavedTimerOptions(AudioAlertSoundId: BuiltInAudioAlertSounds.None);

        LinuxAppSettings quietSettings = quietOptions.ApplyTo(disabledSettings);
        LinuxAppSettings noSoundSettings = noSoundOptions.ApplyTo(LinuxAppSettings.Default);

        Assert.True(quietSettings.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.QuietBeep, quietSettings.AudioAlertSoundId);
        Assert.False(noSoundSettings.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.None, noSoundSettings.AudioAlertSoundId);
    }

    [Fact]
    public void OlderOptionsJsonUsesDefaultWindowTitleMode()
    {
        const string savedTimersJson = """
            {
              "Version": 1,
              "Timers": [
                {
                  "Id": "timer-1",
                  "TimerInput": "10 seconds",
                  "TimerTitle": "Tea",
                  "Options": {
                    "ReverseProgressBar": true
                  }
                }
              ]
            }
            """;
        const string activeSessionJson = """
            {
              "Version": 1,
              "TimerInput": "10 seconds",
              "Options": {
                "ShowTimeElapsed": true
              }
            }
            """;

        SavedTimersDocument? savedTimers = JsonSerializer.Deserialize<SavedTimersDocument>(savedTimersJson);
        ActiveTimerSessionDocument? activeSession = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(activeSessionJson);

        Assert.NotNull(savedTimers);
        SavedTimerDefinition timer = Assert.Single(savedTimers.Timers);
        Assert.True(timer.Options.ReverseProgressBar);
        Assert.Equal(WindowTitleMode.TimerTitle, timer.Options.WindowTitleMode);
        Assert.Null(timer.Options.AudioAlertSoundId);
        Assert.NotNull(activeSession);
        Assert.True(activeSession.HasOptions);
        Assert.True(activeSession.Options.ShowTimeElapsed);
        Assert.Equal(WindowTitleMode.TimerTitle, activeSession.Options.WindowTitleMode);
        Assert.Null(activeSession.Options.AudioAlertSoundId);
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

    [Fact]
    public void ActiveSessionDocumentUsesStoredStartInputWhenEditorTextIsInvalid()
    {
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 secon",
            timerStartInput: "10 seconds",
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        var timerInfo = document.ToTimerInfo(new DateTime(2026, 7, 2, 8, 0, 0));

        Assert.NotNull(timerInfo);
        Assert.Equal(TimerState.Paused, timerInfo.State);
        Assert.NotNull(timerInfo.TimerStart);
    }

    [Fact]
    public void ActiveSessionDocumentFallsBackToTimerInputWhenStoredStartInputIsInvalid()
    {
        var document = new ActiveTimerSessionDocument(
            timerInput: "10 seconds",
            timerStartInput: "not valid",
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        var timerInfo = document.ToTimerInfo(new DateTime(2026, 7, 2, 8, 0, 0));

        Assert.NotNull(timerInfo);
        Assert.Equal(TimerState.Paused, timerInfo.State);
        Assert.NotNull(timerInfo.TimerStart);
    }

    [Fact]
    public void ActiveSessionDocumentFallsBackToTimerInputForOlderSchema()
    {
        const string json = """
            {
              "Version": 1,
              "TimerInput": "10 seconds",
              "State": 2,
              "TimeElapsedTicks": 40000000,
              "TimeLeftTicks": 60000000,
              "TotalTimeTicks": 100000000
            }
            """;

        ActiveTimerSessionDocument? document = JsonSerializer.Deserialize<ActiveTimerSessionDocument>(json);

        Assert.NotNull(document);
        Assert.Equal(new SavedTimerOptions(), document.Options);
        Assert.False(document.HasOptions);
        var timerInfo = document.ToTimerInfo(new DateTime(2026, 7, 2, 8, 0, 0));
        Assert.NotNull(timerInfo);
        Assert.Equal(TimerState.Paused, timerInfo.State);
    }

    [Fact]
    public void ActiveSessionDocumentRejectsActiveSessionWithoutValidStartExpression()
    {
        var document = new ActiveTimerSessionDocument(
            timerInput: "not valid",
            timerStartInput: "also not valid",
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        TimerInfo? timerInfo = document.ToTimerInfo(new DateTime(2026, 7, 2, 8, 0, 0));

        Assert.Null(timerInfo);
    }

    [Fact]
    public void ActiveTimerSessionsDocumentFiltersInvalidEntriesAndDuplicateIds()
    {
        var first = new ActiveTimerSessionDefinition(
            "session-1",
            new ActiveTimerSessionDocument(timerInput: "10 seconds"));
        var duplicate = new ActiveTimerSessionDefinition(
            "session-1",
            new ActiveTimerSessionDocument(timerInput: "20 seconds"));
        var invalid = new ActiveTimerSessionDefinition("session-2", null);

        var document = new ActiveTimerSessionsDocument(sessions: [first, duplicate, invalid]);

        ActiveTimerSessionDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsDocumentCopiesConstructorInput()
    {
        var first = new ActiveTimerSessionDefinition(
            "session-1",
            new ActiveTimerSessionDocument(timerInput: "10 seconds"));
        var replacement = new ActiveTimerSessionDefinition(
            "session-2",
            new ActiveTimerSessionDocument(timerInput: "20 seconds"));
        ActiveTimerSessionDefinition[] sessions = [first];
        var document = new ActiveTimerSessionsDocument(sessions: sessions);

        sessions[0] = replacement;

        ActiveTimerSessionDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsDocumentReturnsSessionSnapshots()
    {
        var first = new ActiveTimerSessionDefinition(
            "session-1",
            new ActiveTimerSessionDocument(timerInput: "10 seconds"));
        var replacement = new ActiveTimerSessionDefinition(
            "session-2",
            new ActiveTimerSessionDocument(timerInput: "20 seconds"));
        var document = new ActiveTimerSessionsDocument(sessions: [first]);
        ActiveTimerSessionDefinition[] sessions = document.Sessions;

        sessions[0] = replacement;

        ActiveTimerSessionDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsDocumentAddReplaceAndRemovePreservesOtherSessions()
    {
        var first = new ActiveTimerSessionDocument(timerInput: "10 seconds");
        var second = new ActiveTimerSessionDocument(timerInput: "20 seconds");
        var replacement = new ActiveTimerSessionDocument(timerInput: "30 seconds");

        ActiveTimerSessionsDocument document = ActiveTimerSessionsDocument.Empty
            .AddOrReplace("first", first)
            .AddOrReplace("second", second)
            .AddOrReplace("first", replacement)
            .Remove("second");

        ActiveTimerSessionDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("first", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("30 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsSnapshotFiltersInvalidEntriesAndDuplicateIds()
    {
        ActiveTimerSessionSnapshot first = CreateActiveSessionSnapshot("10 seconds");
        ActiveTimerSessionSnapshot duplicate = CreateActiveSessionSnapshot("20 seconds");
        var document = new ActiveTimerSessionsSnapshot(sessions:
        [
            new ActiveTimerSessionSnapshotDefinition("session-1", first),
            new ActiveTimerSessionSnapshotDefinition("session-1", duplicate),
            new ActiveTimerSessionSnapshotDefinition("session-2", null)
        ]);

        ActiveTimerSessionSnapshotDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsSnapshotReturnsSessionSnapshots()
    {
        ActiveTimerSessionSnapshot first = CreateActiveSessionSnapshot("10 seconds");
        ActiveTimerSessionSnapshot replacement = CreateActiveSessionSnapshot("20 seconds");
        var document = new ActiveTimerSessionsSnapshot(sessions:
        [
            new ActiveTimerSessionSnapshotDefinition("session-1", first)
        ]);
        ActiveTimerSessionSnapshotDefinition[] sessions = document.Sessions;

        sessions[0] = new ActiveTimerSessionSnapshotDefinition("session-2", replacement);

        ActiveTimerSessionSnapshotDefinition session = Assert.Single(document.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsSnapshotAddReplaceAndRemovePreservesOtherSessions()
    {
        ActiveTimerSessionSnapshot first = CreateActiveSessionSnapshot("10 seconds");
        ActiveTimerSessionSnapshot second = CreateActiveSessionSnapshot("20 seconds");
        ActiveTimerSessionSnapshot replacement = CreateActiveSessionSnapshot("30 seconds");

        ActiveTimerSessionsSnapshot snapshot = ActiveTimerSessionsSnapshot.Empty
            .AddOrReplace("first", first)
            .AddOrReplace("second", second)
            .AddOrReplace("first", replacement)
            .Remove("second");

        ActiveTimerSessionSnapshotDefinition session = Assert.Single(snapshot.Sessions);
        Assert.Equal("first", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("30 seconds", session.Session.TimerInput);
    }

    [Fact]
    public void ActiveTimerSessionsSnapshotRoundTripsDocument()
    {
        var document = new ActiveTimerSessionsDocument(sessions:
        [
            new ActiveTimerSessionDefinition(
                "session-1",
                new ActiveTimerSessionDocument(
                    timerInput: "10 seconds",
                    timerStartInput: "10 seconds",
                    state: TimerState.Paused,
                    timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
                    timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
                    totalTimeTicks: TimeSpan.FromSeconds(10).Ticks))
        ]);

        ActiveTimerSessionsSnapshot snapshot = ActiveTimerSessionsSnapshot.FromDocument(
            document,
            new DateTime(2026, 7, 2, 8, 0, 0),
            TimeSpan.Zero);
        ActiveTimerSessionsDocument roundTripped = snapshot.ToDocument();

        ActiveTimerSessionDefinition session = Assert.Single(roundTripped.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.NotNull(session.Session);
        Assert.Equal("10 seconds", session.Session.TimerInput);
        Assert.Equal(TimerState.Paused, session.Session.State);
    }

    private static ActiveTimerSessionSnapshot CreateActiveSessionSnapshot(string timerInput)
    {
        ActiveTimerSessionDocument document = new(
            timerInput: timerInput,
            timerStartInput: timerInput,
            state: TimerState.Paused,
            timeElapsedTicks: TimeSpan.FromSeconds(4).Ticks,
            timeLeftTicks: TimeSpan.FromSeconds(6).Ticks,
            totalTimeTicks: TimeSpan.FromSeconds(10).Ticks);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            document,
            new DateTime(2026, 7, 2, 8, 0, 0),
            TimeSpan.Zero);

        Assert.NotNull(snapshot);
        return snapshot;
    }
}
