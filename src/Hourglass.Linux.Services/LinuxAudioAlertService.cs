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

    public async Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
    {
        string? soundPath = this.GetSoundPath(soundId, cancellationToken);
        if (soundPath == null)
        {
            return null;
        }

        await this.TryPlayOnceAsync(soundPath, cancellationToken).ConfigureAwait(false);
        return null;
    }

    public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default)
    {
        string? soundPath = this.GetSoundPath(soundId, cancellationToken);
        if (soundPath == null)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        var playback = new LoopingAudioPlayback(this, soundPath);
        playback.Start();
        return Task.FromResult<IAsyncDisposable?>(playback);
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

    private string? GetSoundPath(string soundId, CancellationToken cancellationToken)
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
            return null;
        }

        return soundPath;
    }

    private async Task<bool> TryPlayOnceAsync(string soundPath, CancellationToken cancellationToken)
    {
        foreach (AudioPlayerCommand command in PlayerCommands)
        {
            ProcessStartInfo startInfo = command.CreateStartInfo(soundPath);

            try
            {
                int exitCode = await this.runProcessAsync(startInfo, cancellationToken).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    return true;
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

        return false;
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

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw;
        }

        return process.ExitCode;
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
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

    private sealed class LoopingAudioPlayback : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly LinuxAudioAlertService owner;
        private readonly string soundPath;
        private Task? loopTask;

        public LoopingAudioPlayback(LinuxAudioAlertService owner, string soundPath)
        {
            this.owner = owner;
            this.soundPath = soundPath;
        }

        public void Start()
        {
            this.loopTask = Task.Run(this.PlayLoopAsync);
        }

        public async ValueTask DisposeAsync()
        {
            await this.cancellationTokenSource.CancelAsync().ConfigureAwait(false);

            if (this.loopTask != null)
            {
                try
                {
                    await this.loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            this.cancellationTokenSource.Dispose();
        }

        private async Task PlayLoopAsync()
        {
            while (!this.cancellationTokenSource.IsCancellationRequested)
            {
                bool played = await this.owner.TryPlayOnceAsync(this.soundPath, this.cancellationTokenSource.Token).ConfigureAwait(false);
                if (!played)
                {
                    return;
                }
            }
        }
    }
}
