#nullable enable

namespace Hourglass.Settings;

public static class LinuxSettingsMerger
{
    public static LinuxAppSettings MergeSettingsChange(
        LinuxAppSettings previous,
        LinuxAppSettings requested,
        LinuxAppSettings latest)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(latest);

        return Merge(
            LinuxSettingsSnapshot.FromSettings(previous),
            LinuxSettingsSnapshot.FromSettings(requested),
            LinuxSettingsSnapshot.FromSettings(latest))
            .ToSettings();
    }

    public static LinuxSettingsSnapshot Merge(
        LinuxSettingsSnapshot previous,
        LinuxSettingsSnapshot requested,
        LinuxSettingsSnapshot latest)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(latest);

        TimerDefaults timerDefaults = MergeTimerDefaults(
            previous.TimerDefaults,
            requested.TimerDefaults,
            latest.TimerDefaults);

        ApplicationPreferences applicationPreferences = MergeApplicationPreferences(
            previous.ApplicationPreferences,
            requested.ApplicationPreferences,
            latest.ApplicationPreferences);

        return new LinuxSettingsSnapshot(
            MergeRecentTimerInputs(previous, requested, latest),
            applicationPreferences,
            timerDefaults);
    }

    private static ApplicationPreferences MergeApplicationPreferences(
        ApplicationPreferences previous,
        ApplicationPreferences requested,
        ApplicationPreferences latest)
    {
        (LinuxThemePreference themePreference, string? customThemeId) = SelectThemeSelection(previous, requested, latest);

        return new ApplicationPreferences(
            SelectChanged(previous.NotificationsEnabled, requested.NotificationsEnabled, latest.NotificationsEnabled),
            SelectChanged(previous.AlwaysOnTop, requested.AlwaysOnTop, latest.AlwaysOnTop),
            SelectChanged(previous.PopUpWhenExpired, requested.PopUpWhenExpired, latest.PopUpWhenExpired),
            SelectChanged(previous.PromptOnExit, requested.PromptOnExit, latest.PromptOnExit),
            SelectChanged(previous.ShowProgressInTaskbar, requested.ShowProgressInTaskbar, latest.ShowProgressInTaskbar),
            SelectChanged(previous.ShowInNotificationArea, requested.ShowInNotificationArea, latest.ShowInNotificationArea),
            SelectChanged(
                previous.RestoreActiveSessionOnStartup,
                requested.RestoreActiveSessionOnStartup,
                latest.RestoreActiveSessionOnStartup),
            SelectChanged(previous.OpenSavedTimersOnStartup, requested.OpenSavedTimersOnStartup, latest.OpenSavedTimersOnStartup),
            themePreference,
            customThemeId);
    }

    private static TimerDefaults MergeTimerDefaults(
        TimerDefaults previous,
        TimerDefaults requested,
        TimerDefaults latest)
    {
        bool loopTimer = SelectChanged(previous.LoopTimer, requested.LoopTimer, latest.LoopTimer);
        bool loopSound = SelectChanged(previous.LoopSound, requested.LoopSound, latest.LoopSound);
        bool closeWhenExpired = SelectChanged(previous.CloseWhenExpired, requested.CloseWhenExpired, latest.CloseWhenExpired);
        bool audioAlertsEnabled = SelectChanged(previous.AudioAlertsEnabled, requested.AudioAlertsEnabled, latest.AudioAlertsEnabled);
        string audioAlertSoundId = SelectChanged(previous.AudioAlertSoundId, requested.AudioAlertSoundId, latest.AudioAlertSoundId);

        if (previous.LoopTimer != requested.LoopTimer
            || previous.LoopSound != requested.LoopSound
            || previous.CloseWhenExpired != requested.CloseWhenExpired)
        {
            loopTimer = requested.LoopTimer;
            loopSound = requested.LoopSound;
            closeWhenExpired = requested.CloseWhenExpired;
        }

        if (previous.AudioAlertsEnabled != requested.AudioAlertsEnabled
            || !StringComparer.Ordinal.Equals(previous.AudioAlertSoundId, requested.AudioAlertSoundId))
        {
            audioAlertsEnabled = requested.AudioAlertsEnabled;
            audioAlertSoundId = requested.AudioAlertSoundId;
        }

        return new TimerDefaults(
            audioAlertsEnabled,
            SelectChanged(previous.ReverseProgressBar, requested.ReverseProgressBar, latest.ReverseProgressBar),
            SelectChanged(previous.ShowTimeElapsed, requested.ShowTimeElapsed, latest.ShowTimeElapsed),
            loopTimer,
            loopSound,
            closeWhenExpired,
            SelectChanged(previous.LockInterface, requested.LockInterface, latest.LockInterface),
            SelectChanged(previous.DoNotKeepComputerAwake, requested.DoNotKeepComputerAwake, latest.DoNotKeepComputerAwake),
            SelectChanged(previous.ShutDownWhenExpired, requested.ShutDownWhenExpired, latest.ShutDownWhenExpired),
            SelectChanged(previous.WindowTitleMode, requested.WindowTitleMode, latest.WindowTitleMode),
            audioAlertSoundId,
            SelectChanged(previous.WakeFromSuspendEnabled, requested.WakeFromSuspendEnabled, latest.WakeFromSuspendEnabled));
    }

    private static (LinuxThemePreference ThemePreference, string? CustomThemeId) SelectThemeSelection(
        ApplicationPreferences previous,
        ApplicationPreferences requested,
        ApplicationPreferences latest)
    {
        return previous.ThemePreference == requested.ThemePreference
            && StringComparer.Ordinal.Equals(previous.CustomThemeId, requested.CustomThemeId)
            ? (latest.ThemePreference, latest.CustomThemeId)
            : (requested.ThemePreference, requested.CustomThemeId);
    }

    private static string[] MergeRecentTimerInputs(
        LinuxSettingsSnapshot previous,
        LinuxSettingsSnapshot requested,
        LinuxSettingsSnapshot latest)
    {
        string[] previousInputs = previous.RecentTimerInputs;
        string[] requestedInputs = requested.RecentTimerInputs;
        if (previousInputs.SequenceEqual(requestedInputs, StringComparer.Ordinal))
        {
            return latest.RecentTimerInputs;
        }

        if (requestedInputs.Length == 0)
        {
            return requestedInputs;
        }

        return requestedInputs
            .Concat(latest.RecentTimerInputs)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool SelectChanged(bool previous, bool requested, bool latest)
    {
        return previous == requested ? latest : requested;
    }

    private static string SelectChanged(string previous, string requested, string latest)
    {
        return StringComparer.Ordinal.Equals(previous, requested) ? latest : requested;
    }

    private static T SelectChanged<T>(T previous, T requested, T latest)
        where T : struct, Enum
    {
        return EqualityComparer<T>.Default.Equals(previous, requested) ? latest : requested;
    }
}
