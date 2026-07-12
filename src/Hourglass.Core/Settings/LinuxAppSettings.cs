#nullable enable

namespace Hourglass.Settings;

using Hourglass.Timing;
using System.Text.Json.Serialization;

public sealed record LinuxAppSettings
{
    private const int DefaultMaxRecentTimerInputs = 10;

    private readonly string[] recentTimerInputs;

    public LinuxAppSettings()
        : this(null, true, true, false, true, true, false, false, false, false, false, false, false, false, true, false, true, false, LinuxThemePreference.System, WindowTitleMode.TimerTitle)
    {
    }

    [JsonConstructor]
    public LinuxAppSettings(
        string[]? recentTimerInputs = null,
        bool notificationsEnabled = true,
        bool audioAlertsEnabled = true,
        bool alwaysOnTop = false,
        bool popUpWhenExpired = true,
        bool promptOnExit = true,
        bool reverseProgressBar = false,
        bool showTimeElapsed = false,
        bool loopTimer = false,
        bool loopSound = false,
        bool closeWhenExpired = false,
        bool lockInterface = false,
        bool doNotKeepComputerAwake = false,
        bool shutDownWhenExpired = false,
        bool showProgressInTaskbar = true,
        bool showInNotificationArea = false,
        bool restoreActiveSessionOnStartup = true,
        bool openSavedTimersOnStartup = false,
        LinuxThemePreference themePreference = LinuxThemePreference.System,
        WindowTitleMode windowTitleMode = WindowTitleMode.TimerTitle)
    {
        this.recentTimerInputs = NormalizeRecentTimerInputs(recentTimerInputs, DefaultMaxRecentTimerInputs);
        this.NotificationsEnabled = notificationsEnabled;
        this.AudioAlertsEnabled = audioAlertsEnabled;
        this.AlwaysOnTop = alwaysOnTop;
        this.PopUpWhenExpired = popUpWhenExpired;
        this.PromptOnExit = promptOnExit;
        this.ReverseProgressBar = reverseProgressBar;
        this.ShowTimeElapsed = showTimeElapsed;
        this.LoopTimer = loopTimer;
        this.LoopSound = loopSound;
        this.CloseWhenExpired = closeWhenExpired;
        this.LockInterface = lockInterface;
        this.DoNotKeepComputerAwake = doNotKeepComputerAwake;
        this.ShutDownWhenExpired = shutDownWhenExpired;
        this.ShowProgressInTaskbar = showProgressInTaskbar;
        this.ShowInNotificationArea = showInNotificationArea;
        this.RestoreActiveSessionOnStartup = restoreActiveSessionOnStartup;
        this.OpenSavedTimersOnStartup = openSavedTimersOnStartup;
        this.ThemePreference = themePreference;
        this.WindowTitleMode = windowTitleMode;
    }

    public static LinuxAppSettings Default { get; } = new();

    public string[] RecentTimerInputs => this.recentTimerInputs.ToArray();

    public bool NotificationsEnabled { get; init; }

    public bool AudioAlertsEnabled { get; init; }

    public bool AlwaysOnTop { get; init; }

    public bool PopUpWhenExpired { get; init; }

    public bool PromptOnExit { get; init; }

    public bool ReverseProgressBar { get; init; }

    public bool ShowTimeElapsed { get; init; }

    public bool LoopTimer { get; init; }

    public bool LoopSound { get; init; }

    public bool CloseWhenExpired { get; init; }

    public bool LockInterface { get; init; }

    public bool DoNotKeepComputerAwake { get; init; }

    public bool ShutDownWhenExpired { get; init; }

    public bool ShowProgressInTaskbar { get; init; }

    public bool ShowInNotificationArea { get; init; }

    public bool RestoreActiveSessionOnStartup { get; init; }

    public bool OpenSavedTimersOnStartup { get; init; }

    public LinuxThemePreference ThemePreference { get; init; }

    public WindowTitleMode WindowTitleMode { get; init; }

    public LinuxAppSettings AddRecentTimerInput(string timerInput, int maxRecentTimerInputs = DefaultMaxRecentTimerInputs)
    {
        if (maxRecentTimerInputs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecentTimerInputs), maxRecentTimerInputs, "The maximum must be positive.");
        }

        if (string.IsNullOrWhiteSpace(timerInput))
        {
            return this;
        }

        string trimmed = timerInput.Trim();
        string[] updatedRecentTimerInputs = new[] { trimmed }
            .Concat(this.recentTimerInputs.Where(input => !StringComparer.Ordinal.Equals(input, trimmed)))
            .Take(maxRecentTimerInputs)
            .ToArray();

        return new LinuxAppSettings(
            updatedRecentTimerInputs,
            this.NotificationsEnabled,
            this.AudioAlertsEnabled,
            this.AlwaysOnTop,
            this.PopUpWhenExpired,
            this.PromptOnExit,
            this.ReverseProgressBar,
            this.ShowTimeElapsed,
            this.LoopTimer,
            this.LoopSound,
            this.CloseWhenExpired,
            this.LockInterface,
            this.DoNotKeepComputerAwake,
            this.ShutDownWhenExpired,
            this.ShowProgressInTaskbar,
            this.ShowInNotificationArea,
            this.RestoreActiveSessionOnStartup,
            this.OpenSavedTimersOnStartup,
            this.ThemePreference,
            this.WindowTitleMode);
    }

    public LinuxAppSettings ClearRecentTimerInputs()
    {
        return new LinuxAppSettings(
            [],
            this.NotificationsEnabled,
            this.AudioAlertsEnabled,
            this.AlwaysOnTop,
            this.PopUpWhenExpired,
            this.PromptOnExit,
            this.ReverseProgressBar,
            this.ShowTimeElapsed,
            this.LoopTimer,
            this.LoopSound,
            this.CloseWhenExpired,
            this.LockInterface,
            this.DoNotKeepComputerAwake,
            this.ShutDownWhenExpired,
            this.ShowProgressInTaskbar,
            this.ShowInNotificationArea,
            this.RestoreActiveSessionOnStartup,
            this.OpenSavedTimersOnStartup,
            this.ThemePreference,
            this.WindowTitleMode);
    }

    public string GetInitialTimerInput(string defaultTimerInput)
    {
        ArgumentNullException.ThrowIfNull(defaultTimerInput);

        return this.recentTimerInputs.Length > 0 ? this.recentTimerInputs[0] : defaultTimerInput;
    }

    private static string[] NormalizeRecentTimerInputs(string[]? inputs, int maxRecentTimerInputs)
    {
        if (inputs == null)
        {
            return [];
        }

        return inputs
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .Select(input => input.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(maxRecentTimerInputs)
            .ToArray();
    }
}
