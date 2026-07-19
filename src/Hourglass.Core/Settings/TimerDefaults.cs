#nullable enable

namespace Hourglass.Settings;

using Hourglass.Timing;

public sealed record TimerDefaults(
    bool AudioAlertsEnabled = true,
    bool ReverseProgressBar = false,
    bool ShowTimeElapsed = false,
    bool LoopTimer = false,
    bool LoopSound = false,
    bool CloseWhenExpired = false,
    bool LockInterface = false,
    bool DoNotKeepComputerAwake = false,
    bool ShutDownWhenExpired = false,
    WindowTitleMode WindowTitleMode = WindowTitleMode.TimerTitle,
    string AudioAlertSoundId = BuiltInAudioAlertSounds.NormalBeep,
    bool WakeFromSuspendEnabled = false)
{
    public string AudioAlertSoundId { get; init; } = BuiltInAudioAlertSounds.NormalizeId(AudioAlertSoundId);

    public static TimerDefaults FromSettings(LinuxAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new TimerDefaults(
            settings.AudioAlertsEnabled,
            settings.ReverseProgressBar,
            settings.ShowTimeElapsed,
            settings.LoopTimer,
            settings.LoopSound,
            settings.CloseWhenExpired,
            settings.LockInterface,
            settings.DoNotKeepComputerAwake,
            settings.ShutDownWhenExpired,
            settings.WindowTitleMode,
            settings.AudioAlertSoundId,
            settings.WakeFromSuspendEnabled);
    }
}
