#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;
using Hourglass.Timing;

public sealed record SavedTimerDefinition
{
    [JsonConstructor]
    public SavedTimerDefinition(
        string id,
        string timerInput,
        string timerTitle = "",
        string? displayName = null,
        SavedTimerOptions? options = null)
    {
        this.Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        this.TimerInput = timerInput?.Trim() ?? string.Empty;
        this.TimerTitle = timerTitle ?? string.Empty;
        this.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        this.Options = options ?? new SavedTimerOptions();
    }

    public string Id { get; }

    public string TimerInput { get; }

    public string TimerTitle { get; }

    public string? DisplayName { get; }

    public SavedTimerOptions Options { get; }

    public string Header => this.DisplayName ?? FormatHeader(this.TimerTitle, this.TimerInput);

    public bool IsValid => IsValidTimerInput(this.TimerInput);

    public static SavedTimerDefinition Create(
        string timerInput,
        string timerTitle,
        LinuxAppSettings settings,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SavedTimerDefinition(
            Guid.NewGuid().ToString("N"),
            timerInput,
            timerTitle,
            displayName,
            SavedTimerOptions.FromSettings(settings));
    }

    private static bool IsValidTimerInput(string timerInput)
    {
        TimerStart? timerStart = TimerStart.FromString(timerInput);
        return timerStart is { IsValid: true };
    }

    private static string FormatHeader(string timerTitle, string timerInput)
    {
        if (string.IsNullOrWhiteSpace(timerTitle))
        {
            return timerInput;
        }

        return $"{timerTitle.Trim()} — {timerInput}";
    }
}
