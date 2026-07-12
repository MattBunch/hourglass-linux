#nullable enable

namespace Hourglass.Settings;

using Hourglass.Timing;

public sealed record SavedTimerOptions(
    bool ReverseProgressBar = false,
    bool ShowTimeElapsed = false,
    bool LoopTimer = false,
    bool LoopSound = false,
    bool CloseWhenExpired = false,
    bool LockInterface = false,
    bool DoNotKeepComputerAwake = false,
    bool ShutDownWhenExpired = false,
    WindowTitleMode WindowTitleMode = WindowTitleMode.TimerTitle,
    string? AudioAlertSoundId = null)
{
    public string? AudioAlertSoundId { get; init; } = NormalizeOptionalSoundId(AudioAlertSoundId);

    public static SavedTimerOptions FromSettings(LinuxAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SavedTimerOptions(
            settings.ReverseProgressBar,
            settings.ShowTimeElapsed,
            settings.LoopTimer,
            settings.LoopSound,
            settings.CloseWhenExpired,
            settings.LockInterface,
            settings.DoNotKeepComputerAwake,
            settings.ShutDownWhenExpired,
            settings.WindowTitleMode,
            settings.AudioAlertsEnabled ? settings.AudioAlertSoundId : BuiltInAudioAlertSounds.None);
    }

    public LinuxAppSettings ApplyTo(LinuxAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings with
        {
            ReverseProgressBar = this.ReverseProgressBar,
            ShowTimeElapsed = this.ShowTimeElapsed,
            LoopTimer = this.LoopTimer,
            LoopSound = this.LoopSound,
            CloseWhenExpired = this.CloseWhenExpired,
            LockInterface = this.LockInterface,
            DoNotKeepComputerAwake = this.DoNotKeepComputerAwake,
            ShutDownWhenExpired = this.ShutDownWhenExpired,
            WindowTitleMode = this.WindowTitleMode,
            AudioAlertsEnabled = this.AudioAlertSoundId == null
                ? settings.AudioAlertsEnabled
                : !BuiltInAudioAlertSounds.IsNone(this.AudioAlertSoundId),
            AudioAlertSoundId = this.AudioAlertSoundId ?? settings.AudioAlertSoundId
        };
    }

    private static string? NormalizeOptionalSoundId(string? soundId)
    {
        return soundId == null
            ? null
            : BuiltInAudioAlertSounds.NormalizeId(soundId);
    }
}
