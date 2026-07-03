#nullable enable

namespace Hourglass.Settings;

public sealed record SavedTimerOptions(
    bool ReverseProgressBar = false,
    bool ShowTimeElapsed = false,
    bool LoopTimer = false,
    bool LoopSound = false,
    bool CloseWhenExpired = false,
    bool LockInterface = false,
    bool DoNotKeepComputerAwake = false,
    bool ShutDownWhenExpired = false)
{
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
            settings.ShutDownWhenExpired);
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
            ShutDownWhenExpired = this.ShutDownWhenExpired
        };
    }
}
