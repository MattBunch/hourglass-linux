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
            new("cross-process-control", true, "Versioned local control protocol 1 is supported."),
            new("notify-send", ExistsOnPath("notify-send"), "Desktop notifications require notify-send and a notification service."),
            new("audio-player", ExistsOnPath("pw-play") || ExistsOnPath("paplay") || ExistsOnPath("aplay"), "Audio playback requires a supported player."),
            new("sound-assets", BuiltInAudioAlertSounds.All.Where(sound => !sound.IsNone).All(sound =>
                sound.AssetFileName != null && File.Exists(Path.Combine(sounds, sound.AssetFileName))), sounds),
            new("systemd-inhibit", ExistsOnPath("systemd-inhibit"), "Keep-awake requires systemd-inhibit and a working user session.")
        ];
    }

    public static async Task<IReadOnlyList<TerminalDiagnostic>> InspectAsync(CancellationToken cancellationToken = default)
    {
        List<TerminalDiagnostic> checks = [.. Inspect()];
        if (!File.Exists(RuntimeControlTransport.DefaultPath))
        {
            checks.Add(new("runtime-connection", false, "No active control endpoint."));
            return checks;
        }
        Hourglass.Application.ApplicationResult<RemoteHourglassClient> result = await RemoteHourglassClient.ConnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result is Hourglass.Application.ApplicationResult<RemoteHourglassClient>.Success connected)
        {
            await using RemoteHourglassClient client = connected.Value;
            checks.Add(new("runtime-connection", true, $"Protocol 1; authority {client.AuthorityId}."));
        }
        else { checks.Add(new("runtime-connection", false, ((Hourglass.Application.ApplicationResult<RemoteHourglassClient>.Failure)result).Error.Message)); }
        return checks;
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
