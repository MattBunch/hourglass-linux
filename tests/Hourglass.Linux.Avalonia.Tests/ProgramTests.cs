namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Platform;
using Hourglass.Platform;
using Xunit;

public sealed class ProgramTests
{
    [Fact]
    public void HourglassApplicationIconResourceIsAvailable()
    {
        var assetLoader = new StandardAssetLoader();
        assetLoader.SetDefaultAssembly(typeof(App).Assembly);

        using Stream stream = assetLoader.Open(
            new Uri("avares://hourglass-linux/Assets/hourglass.png"));

        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void HourglassApplicationIconResourceIsPngForStatusIcon()
    {
        var assetLoader = new StandardAssetLoader();
        assetLoader.SetDefaultAssembly(typeof(App).Assembly);
        byte[] pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

        using Stream stream = assetLoader.Open(
            new Uri("avares://hourglass-linux/Assets/hourglass.png"));
        byte[] actualSignature = new byte[pngSignature.Length];
        int bytesRead = stream.Read(actualSignature);

        Assert.Equal(pngSignature.Length, bytesRead);
        Assert.Equal(pngSignature, actualSignature);
    }

    [Fact]
    public void RunStartsDesktopLifetimeWhenOwnershipIsAcquired()
    {
        string[] args = ["10 seconds", "--test"];
        var service = new RecordingSingleInstanceService { AcquireResult = true };
        string[]? capturedArgs = null;

        int exitCode = Program.Run(
            args,
            () => service,
            startArgs =>
            {
                Assert.False(service.Disposed);
                capturedArgs = startArgs;
                return 42;
            },
            TextWriter.Null);

        Assert.Equal(42, exitCode);
        Assert.Same(args, capturedArgs);
        Assert.Equal(1, service.AcquireCount);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void RunDoesNotStartDesktopLifetimeWhenOwnershipIsNotAcquired()
    {
        var service = new RecordingSingleInstanceService { AcquireResult = false };
        int startCount = 0;

        int exitCode = Program.Run(
            [],
            () => service,
            _ =>
            {
                startCount++;
                return 1;
            },
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, startCount);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void RunWritesErrorAndReturnsOneWhenAcquisitionFails()
    {
        var service = new RecordingSingleInstanceService
        {
            AcquireException = new InvalidOperationException("no safe path")
        };
        using var errorWriter = new StringWriter();

        int exitCode = Program.Run(
            [],
            () => service,
            _ => throw new Xunit.Sdk.XunitException("Desktop should not start."),
            errorWriter);

        Assert.Equal(1, exitCode);
        Assert.True(service.Disposed);
        Assert.Contains("single-instance lock", errorWriter.ToString());
        Assert.Contains("no safe path", errorWriter.ToString());
    }

    [Fact]
    public void RunDisposesServiceWhenDesktopRunnerThrows()
    {
        var service = new RecordingSingleInstanceService { AcquireResult = true };

        Assert.Throws<InvalidOperationException>(() => Program.Run(
            [],
            () => service,
            _ => throw new InvalidOperationException("desktop failed"),
            TextWriter.Null));

        Assert.True(service.Disposed);
    }

    private sealed class RecordingSingleInstanceService : ISingleInstanceService
    {
        public bool AcquireResult { get; init; }

        public Exception? AcquireException { get; init; }

        public int AcquireCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;

            if (this.AcquireException != null)
            {
                return Task.FromException<bool>(this.AcquireException);
            }

            return Task.FromResult(this.AcquireResult);
        }

        public void Dispose()
        {
            this.Disposed = true;
        }
    }
}
