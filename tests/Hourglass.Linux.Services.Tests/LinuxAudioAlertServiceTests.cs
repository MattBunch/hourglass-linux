namespace Hourglass.Linux.Services.Tests;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;
using Hourglass.Settings;
using Xunit;

public sealed class LinuxAudioAlertServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-audio-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("pw-play", "/tmp/beep.wav")]
    [InlineData("paplay", "/tmp/beep.wav")]
    public void CreateStartInfoUsesExecutableAndSoundPathArgument(string executableName, string soundPath)
    {
        ProcessStartInfo startInfo = LinuxAudioAlertService.CreateStartInfo(executableName, soundPath);

        Assert.Equal(executableName, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal([soundPath], startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfoUsesAplayQuietArgument()
    {
        ProcessStartInfo startInfo = LinuxAudioAlertService.CreateStartInfo("aplay", "/tmp/beep.wav", "--quiet");

        Assert.Equal("aplay", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["--quiet", "/tmp/beep.wav"], startInfo.ArgumentList);
    }

    [Fact]
    public async Task PathWithSpacesRemainsSingleArgument()
    {
        string soundPath = this.CreateSoundFile("path with spaces/Beep Normal.wav");
        var calls = new List<ProcessStartInfo>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo);
            return Task.FromResult(0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        ProcessStartInfo call = Assert.Single(calls);
        Assert.Equal([soundPath], call.ArgumentList);
    }

    [Fact]
    public async Task PwPlaySuccessPreventsFallbacks()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo.FileName);
            return Task.FromResult(0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play"], calls);
    }

    [Fact]
    public async Task SelectedBackendIsUsedWithoutPerPlaybackFallbackProbe()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(
            soundPath,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                return Task.FromResult(0);
            },
            executable => executable == "paplay");

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.True(service.IsSupported);
        Assert.Equal(["paplay"], calls);
    }

    [Fact]
    public async Task NonExecutablePathCandidateDoesNotMaskLaterExecutableBackend()
    {
        string soundPath = this.CreateSoundFile();
        string firstPathDirectory = Path.Combine(this.tempDirectory, "path-1");
        string secondPathDirectory = Path.Combine(this.tempDirectory, "path-2");
        Directory.CreateDirectory(firstPathDirectory);
        Directory.CreateDirectory(secondPathDirectory);
        File.WriteAllText(Path.Combine(firstPathDirectory, "pw-play"), string.Empty);
        string paplayPath = Path.Combine(secondPathDirectory, "paplay");
        File.WriteAllText(paplayPath, string.Empty);
#pragma warning disable CA1416
        File.SetUnixFileMode(paplayPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
#pragma warning restore CA1416
        string? previousPath = Environment.GetEnvironmentVariable("PATH");
        var calls = new List<string>();

        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, firstPathDirectory, secondPathDirectory));
            var service = new LinuxAudioAlertService(
                soundPath,
                (startInfo, _) =>
                {
                    calls.Add(startInfo.FileName);
                    return Task.FromResult(0);
                },
                LinuxAudioAlertService.IsExecutableAvailable);

            await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

            Assert.True(service.IsSupported);
            Assert.Equal(["paplay"], calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public void DirectoryPathCandidateIsNotExecutable()
    {
        string pathDirectory = Path.Combine(this.tempDirectory, "path");
        Directory.CreateDirectory(Path.Combine(pathDirectory, "pw-play"));
        string? previousPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", pathDirectory);

            Assert.False(LinuxAudioAlertService.IsExecutableAvailable("pw-play"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public async Task SelectedBackendFailureReturnsNormally()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(
            soundPath,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                return Task.FromResult(1);
            },
            executable => executable == "pw-play");

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play"], calls);
    }

    [Fact]
    public async Task AplayBackendUsesQuietArgument()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<ProcessStartInfo>();
        var service = new LinuxAudioAlertService(
            soundPath,
            (startInfo, _) =>
            {
                calls.Add(startInfo);
                return Task.FromResult(0);
            },
            executable => executable == "aplay");

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        ProcessStartInfo call = Assert.Single(calls);
        Assert.Equal("aplay", call.FileName);
        Assert.Equal(["--quiet", soundPath], call.ArgumentList);
    }

    [Fact]
    public async Task AllPlayersFailingReturnsNormally()
    {
        string soundPath = this.CreateSoundFile();
        var diagnostics = new RecordingDiagnosticSink();
        var service = new LinuxAudioAlertService(soundPath, (_, _) => Task.FromResult(1), diagnosticSink: diagnostics);

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        DiagnosticEvent diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticFailureClass.BestEffort, diagnostic.FailureClass);
        Assert.Equal("audio-alerts", diagnostic.Category);
        Assert.Equal("play", diagnostic.Operation);
    }

    [Fact]
    public async Task LoopingPlaybackReturnsDisposableHandleThatCancelsPlayback()
    {
        string soundPath = this.CreateSoundFile();
        var playbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callCount = 0;
        var service = new LinuxAudioAlertService(soundPath, async (_, cancellationToken) =>
        {
            callCount++;
            playbackStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });

        IAsyncDisposable? playback = await service.PlayAlertLoopingAsync(AudioAlertSoundIds.NormalBeep);

        Assert.NotNull(playback);
        await playbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await playback.DisposeAsync();
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task MissingSoundFileLaunchesNoProcess()
    {
        int callCount = 0;
        string soundPath = Path.Combine(this.tempDirectory, "missing.wav");
        var service = new LinuxAudioAlertService(soundPath, (_, _) =>
        {
            callCount++;
            return Task.FromResult(0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task BuiltInSoundDirectoryMapsSelectedSoundToAssetFile()
    {
        string soundPath = this.CreateSoundFile("BeepQuiet.wav");
        var calls = new List<ProcessStartInfo>();
        var service = new LinuxAudioAlertService(
            LinuxAudioAlertService.CreateBuiltInSoundPaths(this.tempDirectory),
            (startInfo, _) =>
            {
                calls.Add(startInfo);
                return Task.FromResult(0);
            });

        await service.PlayAlertAsync(AudioAlertSoundIds.QuietBeep);

        ProcessStartInfo call = Assert.Single(calls);
        Assert.Equal(soundPath, call.ArgumentList.Single());
    }

    [Fact]
    public void BuiltInSoundDirectoryMapsAllPackagedSoundAssets()
    {
        IReadOnlyDictionary<string, string> soundPaths = LinuxAudioAlertService.CreateBuiltInSoundPaths(this.tempDirectory);

        Assert.DoesNotContain(AudioAlertSoundIds.None, soundPaths.Keys);
        foreach (AudioAlertSoundDefinition sound in BuiltInAudioAlertSounds.All.Where(sound => !sound.IsNone))
        {
            Assert.True(soundPaths.TryGetValue(sound.Id, out string? path));
            Assert.Equal(Path.Combine(this.tempDirectory, sound.AssetFileName!), path);
        }
    }

    [Fact]
    public void IsSoundAvailableRequiresAssetAndBackend()
    {
        string soundPath = this.CreateSoundFile("BeepLoud.wav");
        var service = new LinuxAudioAlertService(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AudioAlertSoundIds.LoudBeep] = soundPath,
                [AudioAlertSoundIds.NormalBeep] = Path.Combine(this.tempDirectory, "missing.wav")
            },
            (_, _) => Task.FromResult(0),
            executable => executable == "pw-play");

        Assert.True(service.IsSupported);
        Assert.True(service.IsSoundAvailable(AudioAlertSoundIds.LoudBeep));
        Assert.False(service.IsSoundAvailable(AudioAlertSoundIds.NormalBeep));
        Assert.False(service.IsSoundAvailable(AudioAlertSoundIds.None));
    }

    [Fact]
    public void IsSoundAvailableReportsFalseWhenBackendIsMissing()
    {
        string soundPath = this.CreateSoundFile();
        var service = new LinuxAudioAlertService(soundPath, (_, _) => Task.FromResult(0), _ => false);

        Assert.False(service.IsSupported);
        Assert.False(service.IsSoundAvailable(AudioAlertSoundIds.NormalBeep));
    }

    [Fact]
    public async Task UnsupportedSoundIdThrows()
    {
        string soundPath = this.CreateSoundFile();
        var service = new LinuxAudioAlertService(soundPath, (_, _) => Task.FromResult(0));

        await Assert.ThrowsAsync<ArgumentException>(() => service.PlayAlertAsync("resource:Missing beep"));
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        string soundPath = this.CreateSoundFile();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var service = new LinuxAudioAlertService(
            soundPath,
            (_, cancellationToken) => throw new OperationCanceledException(cancellationToken));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep, cancellationTokenSource.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsInvalidSoundPath(string soundPath)
    {
        Assert.Throws<ArgumentException>(() => new LinuxAudioAlertService(soundPath));
    }

    [Fact]
    public void ConstructorRejectsNullProcessRunner()
    {
        Assert.Throws<ArgumentNullException>(() => new LinuxAudioAlertService("/tmp/beep.wav", null!));
    }

    [Theory]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task ExpectedInfrastructureFailuresFallBackAndReturnNormally(Type exceptionType)
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo.FileName);
            throw (Exception)Activator.CreateInstance(exceptionType)!;
        }, executable => executable == "pw-play");

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play"], calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            Directory.Delete(this.tempDirectory, recursive: true);
        }
    }

    private string CreateSoundFile(string relativePath = "BeepNormal.wav")
    {
        string soundPath = Path.Combine(this.tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(soundPath)!);
        File.WriteAllBytes(soundPath, [0]);
        return soundPath;
    }
}
