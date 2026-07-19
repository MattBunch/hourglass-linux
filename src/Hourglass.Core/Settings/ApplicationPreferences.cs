#nullable enable

namespace Hourglass.Settings;

public sealed record ApplicationPreferences(
    bool NotificationsEnabled = true,
    bool AlwaysOnTop = false,
    bool PopUpWhenExpired = true,
    bool PromptOnExit = true,
    bool ShowProgressInTaskbar = true,
    bool ShowInNotificationArea = false,
    bool RestoreActiveSessionOnStartup = true,
    bool OpenSavedTimersOnStartup = false,
    LinuxThemePreference ThemePreference = LinuxThemePreference.System,
    string? CustomThemeId = null)
{
    public string? CustomThemeId { get; init; } = string.IsNullOrWhiteSpace(CustomThemeId) ? null : CustomThemeId.Trim();

    public static ApplicationPreferences FromSettings(LinuxAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ApplicationPreferences(
            settings.NotificationsEnabled,
            settings.AlwaysOnTop,
            settings.PopUpWhenExpired,
            settings.PromptOnExit,
            settings.ShowProgressInTaskbar,
            settings.ShowInNotificationArea,
            settings.RestoreActiveSessionOnStartup,
            settings.OpenSavedTimersOnStartup,
            settings.ThemePreference,
            settings.CustomThemeId);
    }
}
