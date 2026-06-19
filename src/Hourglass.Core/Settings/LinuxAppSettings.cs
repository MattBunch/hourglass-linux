#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;

public sealed record LinuxAppSettings
{
    private const int DefaultMaxRecentTimerInputs = 10;

    private readonly string[] recentTimerInputs;

    public LinuxAppSettings()
        : this(null, true, true, false)
    {
    }

    [JsonConstructor]
    public LinuxAppSettings(
        string[]? recentTimerInputs = null,
        bool notificationsEnabled = true,
        bool audioAlertsEnabled = true,
        bool alwaysOnTop = false)
    {
        this.recentTimerInputs = NormalizeRecentTimerInputs(recentTimerInputs, DefaultMaxRecentTimerInputs);
        this.NotificationsEnabled = notificationsEnabled;
        this.AudioAlertsEnabled = audioAlertsEnabled;
        this.AlwaysOnTop = alwaysOnTop;
    }

    public static LinuxAppSettings Default { get; } = new();

    public string[] RecentTimerInputs => this.recentTimerInputs.ToArray();

    public bool NotificationsEnabled { get; init; }

    public bool AudioAlertsEnabled { get; init; }

    public bool AlwaysOnTop { get; init; }

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
            this.AlwaysOnTop);
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
