namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;

public sealed class LinuxAudioAlertService : IAudioAlertService
{
    private const string AplayExecutable = "aplay";
    private const string PaplayExecutable = "paplay";
    private const string PwPlayExecutable = "pw-play";
    private const string QuietArgument = "--quiet";

    private static readonly AudioPlayerCommand[] PlayerCommands =
    [
        new(PwPlayExecutable),
        new(PaplayExecutable),
        new(AplayExecutable, QuietArgument)
    ];

    private readonly string normalBeepPath;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync;

    public LinuxAudioAlertService(string normalBeepPath)
        : this(normalBeepPath, RunProcessAsync)
    {
    }

    internal LinuxAudioAlertService(
        string normalBeepPath,
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync)
    {
        this.normalBeepPath = string.IsNullOrWhiteSpace(normalBeepPath)
            ? throw new ArgumentException("Sound path must not be empty.", nameof(normalBeepPath))
            : normalBeepPath;
        this.runProcessAsync = runProcessAsync ?? throw new ArgumentNullException(nameof(runProcessAsync));
    }

    public async Task PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(soundId);
        cancellationToken.ThrowIfCancellationRequested();

        string soundPath = soundId switch
        {
            AudioAlertSoundIds.NormalBeep => this.normalBeepPath,
            _ => throw new ArgumentException($"Unsupported audio alert sound ID: {soundId}", nameof(soundId))
        };

        if (!File.Exists(soundPath))
        {
            return;
        }

        foreach (AudioPlayerCommand command in PlayerCommands)
        {
            ProcessStartInfo startInfo = command.CreateStartInfo(soundPath);

            try
            {
                int exitCode = await this.runProcessAsync(startInfo, cancellationToken).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executableName, string soundPath, params string[] stableArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executableName,
            UseShellExecute = false
        };

        foreach (string argument in stableArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(soundPath);
        return startInfo;
    }

    private static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return -1;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private readonly record struct AudioPlayerCommand(string ExecutableName, string? StableArgument = null)
    {
        public ProcessStartInfo CreateStartInfo(string soundPath)
        {
            return this.StableArgument == null
                ? LinuxAudioAlertService.CreateStartInfo(this.ExecutableName, soundPath)
                : LinuxAudioAlertService.CreateStartInfo(this.ExecutableName, soundPath, this.StableArgument);
        }
    }
}
