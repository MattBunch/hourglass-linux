namespace Hourglass.Linux.Services.Tests;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;
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
    public async Task MissingPwPlayFallsBackToPaplay()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo.FileName);
            return startInfo.FileName == "pw-play"
                ? throw new Win32Exception()
                : Task.FromResult(0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play", "paplay"], calls);
    }

    [Fact]
    public async Task NonZeroPwPlayExitFallsBackToPaplay()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<string>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo.FileName);
            return Task.FromResult(startInfo.FileName == "pw-play" ? 1 : 0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play", "paplay"], calls);
    }

    [Fact]
    public async Task PaplayFailureFallsBackToAplay()
    {
        string soundPath = this.CreateSoundFile();
        var calls = new List<ProcessStartInfo>();
        var service = new LinuxAudioAlertService(soundPath, (startInfo, _) =>
        {
            calls.Add(startInfo);
            return Task.FromResult(startInfo.FileName == "aplay" ? 0 : 1);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play", "paplay", "aplay"], calls.Select(call => call.FileName));
        Assert.Equal(["--quiet", soundPath], calls[2].ArgumentList);
    }

    [Fact]
    public async Task AllPlayersFailingReturnsNormally()
    {
        string soundPath = this.CreateSoundFile();
        var service = new LinuxAudioAlertService(soundPath, (_, _) => Task.FromResult(1));

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);
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
    public async Task UnsupportedSoundIdThrows()
    {
        string soundPath = this.CreateSoundFile();
        var service = new LinuxAudioAlertService(soundPath, (_, _) => Task.FromResult(0));

        await Assert.ThrowsAsync<ArgumentException>(() => service.PlayAlertAsync("resource:Loud beep"));
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
            if (calls.Count < 3)
            {
                throw (Exception)Activator.CreateInstance(exceptionType)!;
            }

            return Task.FromResult(0);
        });

        await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);

        Assert.Equal(["pw-play", "paplay", "aplay"], calls);
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
