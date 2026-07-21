namespace Hourglass.Core.Tests.Settings;

using Hourglass.Settings;
using Hourglass.Timing;
using System.Text.Json;
using Xunit;

public sealed class LinuxAppSettingsTests
{
    [Fact]
    public void DefaultSettingsUseDefaultTimerInputAndAlertOptions()
    {
        LinuxAppSettings settings = LinuxAppSettings.Default;

        Assert.Equal("5 minutes", settings.GetInitialTimerInput("5 minutes"));
        Assert.True(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.PopUpWhenExpired);
        Assert.True(settings.PromptOnExit);
        Assert.False(settings.ReverseProgressBar);
        Assert.False(settings.ShowTimeElapsed);
        Assert.False(settings.LoopTimer);
        Assert.False(settings.LoopSound);
        Assert.False(settings.CloseWhenExpired);
        Assert.False(settings.LockInterface);
        Assert.False(settings.DoNotKeepComputerAwake);
        Assert.False(settings.ShutDownWhenExpired);
        Assert.True(settings.ShowProgressInTaskbar);
        Assert.False(settings.ShowInNotificationArea);
        Assert.True(settings.RestoreActiveSessionOnStartup);
        Assert.False(settings.OpenSavedTimersOnStartup);
        Assert.False(settings.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.System, settings.ThemePreference);
        Assert.Null(settings.CustomThemeId);
        Assert.Equal(WindowTitleMode.TimerTitle, settings.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.NormalBeep, settings.AudioAlertSoundId);
        Assert.Empty(settings.RecentTimerInputs);
    }

    [Fact]
    public void AddRecentTimerInputReturnsNewSettingsWithTrimmedMostRecentInput()
    {
        LinuxAppSettings original = LinuxAppSettings.Default;

        LinuxAppSettings updated = original
            .AddRecentTimerInput(" 10 seconds ")
            .AddRecentTimerInput("5 minutes")
            .AddRecentTimerInput("10 seconds");

        Assert.Empty(original.RecentTimerInputs);
        Assert.Equal(["10 seconds", "5 minutes"], updated.RecentTimerInputs);
        Assert.Equal("10 seconds", updated.GetInitialTimerInput("default"));
        Assert.True(updated.NotificationsEnabled);
        Assert.True(updated.AudioAlertsEnabled);
        Assert.False(updated.AlwaysOnTop);
        Assert.True(updated.PopUpWhenExpired);
        Assert.True(updated.PromptOnExit);
        Assert.False(updated.ReverseProgressBar);
        Assert.False(updated.ShowTimeElapsed);
        Assert.False(updated.LoopTimer);
        Assert.False(updated.LoopSound);
        Assert.False(updated.CloseWhenExpired);
        Assert.False(updated.LockInterface);
        Assert.False(updated.DoNotKeepComputerAwake);
        Assert.False(updated.ShutDownWhenExpired);
        Assert.True(updated.ShowProgressInTaskbar);
        Assert.False(updated.ShowInNotificationArea);
        Assert.True(updated.RestoreActiveSessionOnStartup);
        Assert.False(updated.OpenSavedTimersOnStartup);
        Assert.False(updated.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.System, updated.ThemePreference);
        Assert.Equal(WindowTitleMode.TimerTitle, updated.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.NormalBeep, updated.AudioAlertSoundId);
    }

    [Fact]
    public void ConstructorNormalizesRecentTimerInputs()
    {
        var settings = new LinuxAppSettings(
            ["", "  15 minutes ", "15 minutes", "  "],
            notificationsEnabled: false,
            audioAlertsEnabled: false,
            alwaysOnTop: true,
            popUpWhenExpired: false,
            promptOnExit: false,
            reverseProgressBar: true,
            showTimeElapsed: true,
            loopTimer: true,
            loopSound: true,
            closeWhenExpired: true,
            lockInterface: true,
            doNotKeepComputerAwake: true,
            shutDownWhenExpired: true,
            showProgressInTaskbar: false,
            showInNotificationArea: true,
            restoreActiveSessionOnStartup: false,
            openSavedTimersOnStartup: true,
            themePreference: LinuxThemePreference.Dark,
            windowTitleMode: WindowTitleMode.TimeLeftPlusTimerTitle,
            audioAlertSoundId: BuiltInAudioAlertSounds.QuietBeep,
            customThemeId: "theme-1",
            wakeFromSuspendEnabled: true);

        Assert.Equal(["15 minutes"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.False(settings.AudioAlertsEnabled);
        Assert.True(settings.AlwaysOnTop);
        Assert.False(settings.PopUpWhenExpired);
        Assert.False(settings.PromptOnExit);
        Assert.True(settings.ReverseProgressBar);
        Assert.True(settings.ShowTimeElapsed);
        Assert.True(settings.LoopTimer);
        Assert.True(settings.LoopSound);
        Assert.True(settings.CloseWhenExpired);
        Assert.True(settings.LockInterface);
        Assert.True(settings.DoNotKeepComputerAwake);
        Assert.True(settings.ShutDownWhenExpired);
        Assert.False(settings.ShowProgressInTaskbar);
        Assert.True(settings.ShowInNotificationArea);
        Assert.False(settings.RestoreActiveSessionOnStartup);
        Assert.True(settings.OpenSavedTimersOnStartup);
        Assert.True(settings.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.Dark, settings.ThemePreference);
        Assert.Null(settings.CustomThemeId);
        Assert.Equal(WindowTitleMode.TimeLeftPlusTimerTitle, settings.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.None, settings.AudioAlertSoundId);
    }

    [Fact]
    public void AddRecentTimerInputPreservesOptions()
    {
        var settings = new LinuxAppSettings(
            ["1 second"],
            notificationsEnabled: false,
            audioAlertsEnabled: false,
            alwaysOnTop: true,
            popUpWhenExpired: false,
            promptOnExit: false,
            reverseProgressBar: true,
            showTimeElapsed: true,
            loopTimer: true,
            loopSound: true,
            closeWhenExpired: true,
            lockInterface: true,
            doNotKeepComputerAwake: true,
            shutDownWhenExpired: true,
            showProgressInTaskbar: false,
            showInNotificationArea: true,
            restoreActiveSessionOnStartup: false,
            openSavedTimersOnStartup: true,
            themePreference: LinuxThemePreference.Light,
            windowTitleMode: WindowTitleMode.TimeElapsed,
            audioAlertSoundId: BuiltInAudioAlertSounds.LoudBeep,
            customThemeId: "theme-1",
            wakeFromSuspendEnabled: true);

        LinuxAppSettings updated = settings.AddRecentTimerInput("2 seconds");

        Assert.Equal(["2 seconds", "1 second"], updated.RecentTimerInputs);
        Assert.False(updated.NotificationsEnabled);
        Assert.False(updated.AudioAlertsEnabled);
        Assert.True(updated.AlwaysOnTop);
        Assert.False(updated.PopUpWhenExpired);
        Assert.False(updated.PromptOnExit);
        Assert.True(updated.ReverseProgressBar);
        Assert.True(updated.ShowTimeElapsed);
        Assert.True(updated.LoopTimer);
        Assert.True(updated.LoopSound);
        Assert.True(updated.CloseWhenExpired);
        Assert.True(updated.LockInterface);
        Assert.True(updated.DoNotKeepComputerAwake);
        Assert.True(updated.ShutDownWhenExpired);
        Assert.False(updated.ShowProgressInTaskbar);
        Assert.True(updated.ShowInNotificationArea);
        Assert.False(updated.RestoreActiveSessionOnStartup);
        Assert.True(updated.OpenSavedTimersOnStartup);
        Assert.True(updated.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.Light, updated.ThemePreference);
        Assert.Null(updated.CustomThemeId);
        Assert.Equal(WindowTitleMode.TimeElapsed, updated.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.None, updated.AudioAlertSoundId);

        LinuxAppSettings cleared = updated.ClearRecentTimerInputs();

        Assert.Empty(cleared.RecentTimerInputs);
        Assert.False(cleared.RestoreActiveSessionOnStartup);
        Assert.True(cleared.OpenSavedTimersOnStartup);
        Assert.True(cleared.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.Light, cleared.ThemePreference);
        Assert.Equal(WindowTitleMode.TimeElapsed, cleared.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.None, cleared.AudioAlertSoundId);
    }

    [Fact]
    public void OlderJsonWithoutNewOptionsUsesDefaults()
    {
        const string json = """{"RecentTimerInputs":["10 seconds"],"NotificationsEnabled":false}""";

        LinuxAppSettings? settings = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(["10 seconds"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.PopUpWhenExpired);
        Assert.True(settings.PromptOnExit);
        Assert.False(settings.ReverseProgressBar);
        Assert.False(settings.ShowTimeElapsed);
        Assert.False(settings.LoopTimer);
        Assert.False(settings.LoopSound);
        Assert.False(settings.CloseWhenExpired);
        Assert.False(settings.LockInterface);
        Assert.False(settings.DoNotKeepComputerAwake);
        Assert.False(settings.ShutDownWhenExpired);
        Assert.True(settings.ShowProgressInTaskbar);
        Assert.False(settings.ShowInNotificationArea);
        Assert.True(settings.RestoreActiveSessionOnStartup);
        Assert.False(settings.OpenSavedTimersOnStartup);
        Assert.False(settings.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.System, settings.ThemePreference);
        Assert.Equal(WindowTitleMode.TimerTitle, settings.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.NormalBeep, settings.AudioAlertSoundId);
    }

    [Fact]
    public void OlderJsonWithDisabledAudioAlertsUsesNoSound()
    {
        const string json = """{"AudioAlertsEnabled":false}""";

        LinuxAppSettings? settings = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(settings);
        Assert.False(settings.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.None, settings.AudioAlertSoundId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("resource:Missing beep")]
    public void UnknownOrBlankAudioAlertSoundUsesDefaultBuiltInSound(string soundId)
    {
        var settings = new LinuxAppSettings(audioAlertSoundId: soundId);

        Assert.True(settings.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.NormalBeep, settings.AudioAlertSoundId);
    }

    [Fact]
    public void SerializationRoundTripsAllPreferences()
    {
        var settings = new LinuxAppSettings(
            ["10 seconds"],
            notificationsEnabled: true,
            audioAlertsEnabled: false,
            alwaysOnTop: true,
            popUpWhenExpired: false,
            promptOnExit: false,
            reverseProgressBar: true,
            showTimeElapsed: true,
            loopTimer: true,
            loopSound: true,
            closeWhenExpired: true,
            lockInterface: true,
            doNotKeepComputerAwake: true,
            shutDownWhenExpired: true,
            showProgressInTaskbar: false,
            showInNotificationArea: true,
            restoreActiveSessionOnStartup: false,
            openSavedTimersOnStartup: true,
            themePreference: LinuxThemePreference.Custom,
            windowTitleMode: WindowTitleMode.TimerTitlePlusTimeLeft,
            audioAlertSoundId: BuiltInAudioAlertSounds.QuietBeep,
            customThemeId: "theme-1",
            wakeFromSuspendEnabled: true);

        string json = JsonSerializer.Serialize(settings);
        LinuxAppSettings? roundTripped = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(["10 seconds"], roundTripped.RecentTimerInputs);
        Assert.True(roundTripped.NotificationsEnabled);
        Assert.False(roundTripped.AudioAlertsEnabled);
        Assert.True(roundTripped.AlwaysOnTop);
        Assert.False(roundTripped.PopUpWhenExpired);
        Assert.False(roundTripped.PromptOnExit);
        Assert.True(roundTripped.ReverseProgressBar);
        Assert.True(roundTripped.ShowTimeElapsed);
        Assert.True(roundTripped.LoopTimer);
        Assert.True(roundTripped.LoopSound);
        Assert.True(roundTripped.CloseWhenExpired);
        Assert.True(roundTripped.LockInterface);
        Assert.True(roundTripped.DoNotKeepComputerAwake);
        Assert.True(roundTripped.ShutDownWhenExpired);
        Assert.False(roundTripped.ShowProgressInTaskbar);
        Assert.True(roundTripped.ShowInNotificationArea);
        Assert.False(roundTripped.RestoreActiveSessionOnStartup);
        Assert.True(roundTripped.OpenSavedTimersOnStartup);
        Assert.True(roundTripped.WakeFromSuspendEnabled);
        Assert.Equal(LinuxThemePreference.Custom, roundTripped.ThemePreference);
        Assert.Equal("theme-1", roundTripped.CustomThemeId);
        Assert.Equal(WindowTitleMode.TimerTitlePlusTimeLeft, roundTripped.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.None, roundTripped.AudioAlertSoundId);
    }

    [Fact]
    public void FocusedSettingsSnapshotRoundTripsCurrentSettingsDocument()
    {
        var settings = new LinuxAppSettings(
            ["10 seconds", "5 minutes"],
            notificationsEnabled: false,
            audioAlertsEnabled: true,
            alwaysOnTop: true,
            popUpWhenExpired: false,
            promptOnExit: false,
            reverseProgressBar: true,
            showTimeElapsed: true,
            loopTimer: true,
            loopSound: false,
            closeWhenExpired: false,
            lockInterface: true,
            doNotKeepComputerAwake: true,
            shutDownWhenExpired: false,
            showProgressInTaskbar: false,
            showInNotificationArea: true,
            restoreActiveSessionOnStartup: false,
            openSavedTimersOnStartup: true,
            themePreference: LinuxThemePreference.Custom,
            windowTitleMode: WindowTitleMode.TimeElapsedPlusTimerTitle,
            audioAlertSoundId: BuiltInAudioAlertSounds.LoudBeep,
            customThemeId: " theme-1 ",
            wakeFromSuspendEnabled: true);

        LinuxSettingsSnapshot snapshot = LinuxSettingsSnapshot.FromSettings(settings);
        LinuxAppSettings roundTripped = snapshot.ToSettings();

        Assert.Equal(["10 seconds", "5 minutes"], snapshot.RecentTimerInputs);
        Assert.False(snapshot.ApplicationPreferences.NotificationsEnabled);
        Assert.True(snapshot.ApplicationPreferences.AlwaysOnTop);
        Assert.Equal(LinuxThemePreference.Custom, snapshot.ApplicationPreferences.ThemePreference);
        Assert.Equal("theme-1", snapshot.ApplicationPreferences.CustomThemeId);
        Assert.True(snapshot.TimerDefaults.AudioAlertsEnabled);
        Assert.True(snapshot.TimerDefaults.ReverseProgressBar);
        Assert.Equal(WindowTitleMode.TimeElapsedPlusTimerTitle, snapshot.TimerDefaults.WindowTitleMode);
        Assert.Equal(BuiltInAudioAlertSounds.LoudBeep, snapshot.TimerDefaults.AudioAlertSoundId);
        Assert.True(snapshot.TimerDefaults.WakeFromSuspendEnabled);
        Assert.Equal(settings.RecentTimerInputs, roundTripped.RecentTimerInputs);
        Assert.Equal(settings.NotificationsEnabled, roundTripped.NotificationsEnabled);
        Assert.Equal(settings.AlwaysOnTop, roundTripped.AlwaysOnTop);
        Assert.Equal(settings.ThemePreference, roundTripped.ThemePreference);
        Assert.Equal(settings.CustomThemeId, roundTripped.CustomThemeId);
        Assert.Equal(settings.AudioAlertsEnabled, roundTripped.AudioAlertsEnabled);
        Assert.Equal(settings.ReverseProgressBar, roundTripped.ReverseProgressBar);
        Assert.Equal(settings.WindowTitleMode, roundTripped.WindowTitleMode);
        Assert.Equal(settings.AudioAlertSoundId, roundTripped.AudioAlertSoundId);
        Assert.Equal(settings.WakeFromSuspendEnabled, roundTripped.WakeFromSuspendEnabled);
    }

    [Fact]
    public void SettingsMergerPreservesLatestIndependentChanges()
    {
        LinuxAppSettings previous = LinuxAppSettings.Default;
        LinuxAppSettings requested = previous with
        {
            NotificationsEnabled = false,
            ReverseProgressBar = true
        };
        LinuxAppSettings latest = previous with
        {
            AlwaysOnTop = true,
            ShowTimeElapsed = true
        };

        LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);

        Assert.False(merged.NotificationsEnabled);
        Assert.True(merged.ReverseProgressBar);
        Assert.True(merged.AlwaysOnTop);
        Assert.True(merged.ShowTimeElapsed);
    }

    [Fact]
    public void SettingsMergerPreservesIndependentAudioAndLoopChanges()
    {
        LinuxAppSettings previous = LinuxAppSettings.Default with
        {
            LoopTimer = false,
            LoopSound = false,
            CloseWhenExpired = false,
            AudioAlertsEnabled = true,
            AudioAlertSoundId = BuiltInAudioAlertSounds.NormalBeep
        };
        LinuxAppSettings requested = previous with
        {
            LoopTimer = true,
            LoopSound = false,
            CloseWhenExpired = false,
            AudioAlertsEnabled = false,
            AudioAlertSoundId = BuiltInAudioAlertSounds.None
        };
        LinuxAppSettings latest = previous with
        {
            LoopTimer = false,
            LoopSound = true,
            CloseWhenExpired = false,
            AudioAlertsEnabled = true,
            AudioAlertSoundId = BuiltInAudioAlertSounds.QuietBeep
        };

        LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);

        Assert.True(merged.LoopTimer);
        Assert.True(merged.LoopSound);
        Assert.False(merged.CloseWhenExpired);
        Assert.False(merged.AudioAlertsEnabled);
        Assert.Equal(BuiltInAudioAlertSounds.None, merged.AudioAlertSoundId);
    }

    [Fact]
    public void SettingsMergerResolvesCloseWhenExpiredAgainstLoopChanges()
    {
        LinuxAppSettings previous = LinuxAppSettings.Default;
        LinuxAppSettings requested = previous with { LoopSound = true };
        LinuxAppSettings latest = previous with { CloseWhenExpired = true };

        LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);

        Assert.True(merged.LoopSound);
        Assert.False(merged.CloseWhenExpired);
    }

    [Fact]
    public void SettingsMergerCloseWhenExpiredRequestClearsExistingLoopChanges()
    {
        LinuxAppSettings previous = LinuxAppSettings.Default;
        LinuxAppSettings requested = previous with { CloseWhenExpired = true };
        LinuxAppSettings latest = previous with { LoopTimer = true, LoopSound = true };

        LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);

        Assert.True(merged.CloseWhenExpired);
        Assert.False(merged.LoopTimer);
        Assert.False(merged.LoopSound);
    }
}
