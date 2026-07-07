namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Platform;
using Hourglass.Platform;
using System.Net.Sockets;
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
        string[] args = ["10", "seconds"];
        var service = new RecordingSingleInstanceService { AcquireResult = true };
        SingleInstanceLaunchRequest? capturedRequest = null;

        int exitCode = Program.Run(
            args,
            () => service,
            request =>
            {
                Assert.False(service.Disposed);
                capturedRequest = request;
                return 42;
            },
            TextWriter.Null);

        Assert.Equal(42, exitCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal(SingleInstanceLaunchRequestKind.StartTimer, capturedRequest.Kind);
        Assert.Equal("10 seconds", capturedRequest.TimerInput);
        Assert.Equal(1, service.AcquireCount);
        Assert.Equal(1, service.ListenCount);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void RunSendsLaunchRequestWhenOwnershipIsNotAcquired()
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
        Assert.NotNull(service.SentRequest);
        Assert.Equal(SingleInstanceLaunchRequestKind.Activate, service.SentRequest.Kind);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void RunReturnsTwoForInvalidCommandLineWithoutAcquiringOwnership()
    {
        var service = new RecordingSingleInstanceService { AcquireResult = true };
        using var errorWriter = new StringWriter();

        int exitCode = Program.Run(
            ["--title"],
            () => service,
            _ => throw new Xunit.Sdk.XunitException("Desktop should not start."),
            errorWriter);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, service.AcquireCount);
        Assert.Contains("--title", errorWriter.ToString());
        Assert.False(service.Disposed);
    }

    [Fact]
    public void RunReturnsOneWhenSecondaryHandoffFails()
    {
        var service = new RecordingSingleInstanceService
        {
            AcquireResult = false,
            SendException = new SocketException((int)SocketError.ConnectionRefused)
        };
        using var errorWriter = new StringWriter();

        int exitCode = Program.Run(
            ["10 seconds"],
            () => service,
            _ => throw new Xunit.Sdk.XunitException("Desktop should not start."),
            errorWriter);

        Assert.Equal(1, exitCode);
        Assert.Contains("running instance", errorWriter.ToString());
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

        public Exception? SendException { get; init; }

        public int AcquireCount { get; private set; }

        public int ListenCount { get; private set; }

        public bool Disposed { get; private set; }

        public SingleInstanceLaunchRequest? SentRequest { get; private set; }

        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;

            if (this.AcquireException != null)
            {
                return Task.FromException<bool>(this.AcquireException);
            }

            return Task.FromResult(this.AcquireResult);
        }

        public Task SendLaunchRequestAsync(
            SingleInstanceLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            if (this.SendException != null)
            {
                return Task.FromException(this.SendException);
            }

            this.SentRequest = request;
            return Task.CompletedTask;
        }

        public Task StartRequestListenerAsync(
            Func<SingleInstanceLaunchRequest, CancellationToken, Task> handleRequestAsync,
            CancellationToken cancellationToken = default)
        {
            this.ListenCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            this.Disposed = true;
        }
    }
}
