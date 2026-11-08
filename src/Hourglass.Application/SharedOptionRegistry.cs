namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;

/// <summary>The terminal-editable settings shared by all timer frontends.</summary>
public static class SharedOptionRegistry
{
    public static ImmutableArray<string> Keys { get; } =
    [
        "notifications-enabled", "audio-alerts-enabled", "audio-alert-sound-id", "prompt-on-exit",
        "reverse-progress-bar", "show-time-elapsed", "loop-timer", "loop-sound",
        "close-when-expired", "lock-interface", "do-not-keep-computer-awake"
    ];

    public static string? Get(LinuxAppSettings settings, string key) => key switch
    {
        "notifications-enabled" => Bool(settings.NotificationsEnabled),
        "audio-alerts-enabled" => Bool(settings.AudioAlertsEnabled),
        "audio-alert-sound-id" => settings.AudioAlertSoundId,
        "prompt-on-exit" => Bool(settings.PromptOnExit),
        "reverse-progress-bar" => Bool(settings.ReverseProgressBar),
        "show-time-elapsed" => Bool(settings.ShowTimeElapsed),
        "loop-timer" => Bool(settings.LoopTimer),
        "loop-sound" => Bool(settings.LoopSound),
        "close-when-expired" => Bool(settings.CloseWhenExpired),
        "lock-interface" => Bool(settings.LockInterface),
        "do-not-keep-computer-awake" => Bool(settings.DoNotKeepComputerAwake),
        _ => null
    };

    public static ApplicationResult<LinuxAppSettings> Apply(LinuxAppSettings current, IEnumerable<KeyValuePair<string, string>> changes)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changes);
        KeyValuePair<string, string>[] edits = changes.ToArray();
        if (edits.GroupBy(edit => edit.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return Failure("A setting can be specified only once.");
        }
        bool closing = edits.Any(edit => edit.Key == "close-when-expired" && string.Equals(edit.Value, "true", StringComparison.OrdinalIgnoreCase));
        bool looping = edits.Any(edit => edit.Key is "loop-timer" or "loop-sound" && string.Equals(edit.Value, "true", StringComparison.OrdinalIgnoreCase));
        if (closing && looping) { return Failure("Close-on-expiry cannot be enabled with looping in one request."); }
        bool? audioEnabled = edits.Where(edit => edit.Key == "audio-alerts-enabled")
            .Select(edit => bool.TryParse(edit.Value, out bool enabled) ? enabled : (bool?)null).FirstOrDefault();
        string? chosenSound = edits.FirstOrDefault(edit => edit.Key == "audio-alert-sound-id").Value;
        if (chosenSound != null && audioEnabled.HasValue && BuiltInAudioAlertSounds.TryGet(chosenSound, out AudioAlertSoundDefinition? selectedSound)
            && selectedSound != null && audioEnabled.Value == selectedSound.IsNone)
        {
            return Failure("Audio enablement conflicts with the selected sound.");
        }
        LinuxAppSettings result = current;
        foreach (KeyValuePair<string, string> edit in edits)
        {
            if (!Keys.Contains(edit.Key)) { return Failure($"Unknown setting: {edit.Key}"); }
            if (edit.Key == "audio-alert-sound-id")
            {
                if (!BuiltInAudioAlertSounds.TryGet(edit.Value, out AudioAlertSoundDefinition? sound) || sound == null)
                {
                    return Failure($"Unknown sound: {edit.Value}");
                }
                result = result with { AudioAlertSoundId = sound.Id, AudioAlertsEnabled = !sound.IsNone };
                continue;
            }
            if (!bool.TryParse(edit.Value, out bool enabled)) { return Failure($"Invalid boolean value for {edit.Key}: {edit.Value}"); }
            result = edit.Key switch
            {
                "notifications-enabled" => result with { NotificationsEnabled = enabled },
                "audio-alerts-enabled" => result with
                {
                    AudioAlertsEnabled = enabled,
                    AudioAlertSoundId = enabled && BuiltInAudioAlertSounds.IsNone(result.AudioAlertSoundId) ? BuiltInAudioAlertSounds.Default.Id
                        : enabled ? result.AudioAlertSoundId : BuiltInAudioAlertSounds.None
                },
                "prompt-on-exit" => result with { PromptOnExit = enabled },
                "reverse-progress-bar" => result with { ReverseProgressBar = enabled },
                "show-time-elapsed" => result with { ShowTimeElapsed = enabled },
                "loop-timer" => result with { LoopTimer = enabled, CloseWhenExpired = enabled ? false : result.CloseWhenExpired },
                "loop-sound" => result with { LoopSound = enabled, CloseWhenExpired = enabled ? false : result.CloseWhenExpired },
                "close-when-expired" => result with
                {
                    CloseWhenExpired = enabled,
                    LoopTimer = enabled ? false : result.LoopTimer,
                    LoopSound = enabled ? false : result.LoopSound
                },
                "lock-interface" => result with { LockInterface = enabled },
                "do-not-keep-computer-awake" => result with { DoNotKeepComputerAwake = enabled },
                _ => result
            };
        }
        return new ApplicationResult<LinuxAppSettings>.Success(result);
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static ApplicationResult<LinuxAppSettings> Failure(string message) =>
        new ApplicationResult<LinuxAppSettings>.Failure(new(ApplicationErrorCode.Validation, message));
}
