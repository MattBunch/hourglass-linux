namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;
using Hourglass.Settings;

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

    private readonly IReadOnlyDictionary<string, string> soundPaths;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync;
    private readonly AudioPlayerCommand? selectedCommand;

    public LinuxAudioAlertService(string soundsDirectory)
        : this(CreateBuiltInSoundPaths(soundsDirectory), RunProcessAsync, IsExecutableAvailable)
    {
    }

    internal LinuxAudioAlertService(
        string normalBeepPath,
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync,
        Func<string, bool>? isExecutableAvailable = null)
        : this(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AudioAlertSoundIds.NormalBeep] = normalBeepPath
            },
            runProcessAsync,
            isExecutableAvailable)
    {
    }

    internal LinuxAudioAlertService(
        IReadOnlyDictionary<string, string> soundPaths,
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync,
        Func<string, bool>? isExecutableAvailable = null)
    {
        this.soundPaths = ValidateSoundPaths(soundPaths);
        this.runProcessAsync = runProcessAsync ?? throw new ArgumentNullException(nameof(runProcessAsync));
        Func<string, bool> executableAvailable = isExecutableAvailable ?? (_ => true);
        this.selectedCommand = PlayerCommands
            .Where(command => executableAvailable(command.ExecutableName))
            .Cast<AudioPlayerCommand?>()
            .FirstOrDefault();
    }

    public bool IsSupported => this.selectedCommand != null;

    public bool IsSoundAvailable(string soundId)
    {
        ArgumentNullException.ThrowIfNull(soundId);

        return this.IsSupported
            && this.TryGetSoundPath(soundId, out string? soundPath)
            && File.Exists(soundPath);
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

    internal static IReadOnlyDictionary<string, string> CreateBuiltInSoundPaths(string soundsDirectory)
    {
        if (string.IsNullOrWhiteSpace(soundsDirectory))
        {
            throw new ArgumentException("Sound directory must not be empty.", nameof(soundsDirectory));
        }

        return BuiltInAudioAlertSounds.All
            .Where(sound => !sound.IsNone && sound.AssetFileName != null)
            .ToDictionary(
                sound => sound.Id,
                sound => Path.Combine(soundsDirectory, sound.AssetFileName!),
                StringComparer.Ordinal);
    }

    private string? GetSoundPath(string soundId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(soundId);
        cancellationToken.ThrowIfCancellationRequested();

        if (StringComparer.Ordinal.Equals(soundId, AudioAlertSoundIds.None))
        {
            return null;
        }

        if (!this.TryGetSoundPath(soundId, out string? soundPath))
        {
            throw new ArgumentException($"Unsupported audio alert sound ID: {soundId}", nameof(soundId));
        }

        if (!File.Exists(soundPath))
        {
            return null;
        }

        return soundPath;
    }

    private bool TryGetSoundPath(string soundId, out string soundPath)
    {
        if (StringComparer.Ordinal.Equals(soundId, AudioAlertSoundIds.None))
        {
            soundPath = string.Empty;
            return false;
        }

        return this.soundPaths.TryGetValue(soundId, out soundPath!);
    }

    private static IReadOnlyDictionary<string, string> ValidateSoundPaths(IReadOnlyDictionary<string, string> soundPaths)
    {
        ArgumentNullException.ThrowIfNull(soundPaths);

        var validated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> soundPath in soundPaths)
        {
            if (string.IsNullOrWhiteSpace(soundPath.Key))
            {
                throw new ArgumentException("Sound ID must not be empty.", nameof(soundPaths));
            }

            if (string.IsNullOrWhiteSpace(soundPath.Value))
            {
                throw new ArgumentException("Sound path must not be empty.", nameof(soundPaths));
            }

            validated[soundPath.Key] = soundPath.Value;
        }

        return validated;
    }

    private async Task<bool> TryPlayOnceAsync(string soundPath, CancellationToken cancellationToken)
    {
        if (this.selectedCommand is not AudioPlayerCommand command)
        {
            return false;
        }

        ProcessStartInfo startInfo = command.CreateStartInfo(soundPath);

        try
        {
            int exitCode = await this.runProcessAsync(startInfo, cancellationToken).ConfigureAwait(false);
            return exitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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

    private static bool IsExecutableAvailable(string executableName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
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
