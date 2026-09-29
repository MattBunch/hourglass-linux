namespace Hourglass.Linux.Services;

using Hourglass.Settings;

public sealed record TerminalDiagnostic(string Name, bool Available, string Detail);

/// <summary>Read-only prerequisite checks; no desktop service is invoked.</summary>
public static class TerminalDiagnostics
{
    public static IReadOnlyList<TerminalDiagnostic> Inspect()
    {
        string directory = new XdgSettingsPathService().GetSettingsDirectory();
        string sounds = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        string authority = LinuxSingleInstanceLockPath.Resolve(Environment.GetEnvironmentVariable,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return
        [
            new("settings-directory", true, directory),
            new("runtime-authority-path", true, authority),
            new("cross-process-control", false, "Versioned control arrives in Stage G; this check does not determine lock ownership."),
            new("notify-send", ExistsOnPath("notify-send"), "Desktop notifications require notify-send and a notification service."),
            new("audio-player", ExistsOnPath("pw-play") || ExistsOnPath("paplay") || ExistsOnPath("aplay"), "Audio playback requires a supported player."),
            new("sound-assets", BuiltInAudioAlertSounds.All.Where(sound => !sound.IsNone).All(sound =>
                sound.AssetFileName != null && File.Exists(Path.Combine(sounds, sound.AssetFileName))), sounds),
            new("systemd-inhibit", ExistsOnPath("systemd-inhibit"), "Keep-awake requires systemd-inhibit and a working user session.")
        ];
    }

    private static bool ExistsOnPath(string executable)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, executable))) { return true; }
        }
        return false;
    }
}
