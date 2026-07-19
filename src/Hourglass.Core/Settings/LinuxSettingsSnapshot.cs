#nullable enable

namespace Hourglass.Settings;

public sealed record LinuxSettingsSnapshot
{
    private readonly string[] recentTimerInputs;

    public LinuxSettingsSnapshot(
        string[]? recentTimerInputs = null,
        ApplicationPreferences? applicationPreferences = null,
        TimerDefaults? timerDefaults = null)
    {
        this.recentTimerInputs = NormalizeRecentTimerInputs(recentTimerInputs);
        this.ApplicationPreferences = applicationPreferences ?? new ApplicationPreferences();
        this.TimerDefaults = timerDefaults ?? new TimerDefaults();
    }

    public string[] RecentTimerInputs => this.recentTimerInputs.ToArray();

    public ApplicationPreferences ApplicationPreferences { get; init; }

    public TimerDefaults TimerDefaults { get; init; }

    public static LinuxSettingsSnapshot FromSettings(LinuxAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new LinuxSettingsSnapshot(
            settings.RecentTimerInputs,
            ApplicationPreferences.FromSettings(settings),
            TimerDefaults.FromSettings(settings));
    }

    public LinuxAppSettings ToSettings()
    {
        return new LinuxAppSettings(
            this.recentTimerInputs,
            this.ApplicationPreferences.NotificationsEnabled,
            this.TimerDefaults.AudioAlertsEnabled,
            this.ApplicationPreferences.AlwaysOnTop,
            this.ApplicationPreferences.PopUpWhenExpired,
            this.ApplicationPreferences.PromptOnExit,
            this.TimerDefaults.ReverseProgressBar,
            this.TimerDefaults.ShowTimeElapsed,
            this.TimerDefaults.LoopTimer,
            this.TimerDefaults.LoopSound,
            this.TimerDefaults.CloseWhenExpired,
            this.TimerDefaults.LockInterface,
            this.TimerDefaults.DoNotKeepComputerAwake,
            this.TimerDefaults.ShutDownWhenExpired,
            this.ApplicationPreferences.ShowProgressInTaskbar,
            this.ApplicationPreferences.ShowInNotificationArea,
            this.ApplicationPreferences.RestoreActiveSessionOnStartup,
            this.ApplicationPreferences.OpenSavedTimersOnStartup,
            this.ApplicationPreferences.ThemePreference,
            this.TimerDefaults.WindowTitleMode,
            this.TimerDefaults.AudioAlertSoundId,
            this.ApplicationPreferences.CustomThemeId,
            this.TimerDefaults.WakeFromSuspendEnabled);
    }

    private static string[] NormalizeRecentTimerInputs(string[]? inputs)
    {
        return inputs?.ToArray() ?? [];
    }
}
