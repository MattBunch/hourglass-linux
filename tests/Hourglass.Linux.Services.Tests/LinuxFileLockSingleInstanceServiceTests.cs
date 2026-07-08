namespace Hourglass.Linux.Services.Tests;

using Hourglass.Platform;
using Hourglass.Linux.Services;
using System.Net.Sockets;
using Xunit;

public sealed class LinuxFileLockSingleInstanceServiceTests
{
    [Fact]
    public async Task SuccessfulAcquisitionReturnsTrueAndRetainsHandleUntilDispose()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        bool acquired = await service.TryAcquireAsync();

        Assert.True(acquired);
        Assert.Equal("/runtime/hourglass-linux", fileSystem.CreatedDirectory);
        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.True(handle.IsLocked);
        Assert.False(handle.Disposed);

        service.Dispose();

        Assert.False(handle.IsLocked);
        Assert.True(handle.Disposed);
    }

    [Fact]
    public async Task RepeatedAcquireOnAcquiredServiceDoesNotOpenOrLockAgain()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        Assert.True(await service.TryAcquireAsync());
        Assert.True(await service.TryAcquireAsync());

        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.Equal(1, handle.LockCount);
        Assert.Equal(1, fileSystem.OpenCount);
    }

    [Fact]
    public async Task SimulatedContentionReturnsFalseAndDisposesCandidate()
    {
        var fileSystem = new RecordingLockFileSystem
        {
            NextHandle = new RecordingLockFileHandle { ThrowOnLock = new IOException("locked") }
        };
        using var service = CreateService(fileSystem);

        bool acquired = await service.TryAcquireAsync();

        Assert.False(acquired);
        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.True(handle.Disposed);
    }

    [Fact]
    public async Task CancellationBeforeAcquisitionPropagates()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        using var service = CreateService(new RecordingLockFileSystem());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.TryAcquireAsync(cancellationTokenSource.Token));
    }

    [Fact]
    public async Task FileOpenFailurePropagates()
    {
        var fileSystem = new RecordingLockFileSystem { OpenException = new IOException("open failed") };
        using var service = CreateService(fileSystem);

        await Assert.ThrowsAsync<IOException>(() => service.TryAcquireAsync());
    }

    [Fact]
    public async Task UnexpectedLockFailureAfterOpenDisposesCandidate()
    {
        var fileSystem = new RecordingLockFileSystem
        {
            NextHandle = new RecordingLockFileHandle { ThrowOnLock = new UnauthorizedAccessException("lock denied") }
        };
        using var service = CreateService(fileSystem);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TryAcquireAsync());

        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task DirectoryCreationFailurePropagates()
    {
        var fileSystem = new RecordingLockFileSystem { CreateDirectoryException = new UnauthorizedAccessException("denied") };
        using var service = CreateService(fileSystem);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TryAcquireAsync());
    }

    [Fact]
    public async Task DiagnosticWriteFailureReleasesAndDisposesHandle()
    {
        var fileSystem = new RecordingLockFileSystem
        {
            NextHandle = new RecordingLockFileHandle { ThrowOnWriteDiagnostics = new IOException("write failed") }
        };
        using var service = CreateService(fileSystem);

        await Assert.ThrowsAsync<IOException>(() => service.TryAcquireAsync());

        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.False(handle.IsLocked);
        Assert.Equal(1, handle.UnlockCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task DisposeBeforeAcquisitionIsValidAndIdempotent()
    {
        using var service = CreateService(new RecordingLockFileSystem());

        service.Dispose();
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.TryAcquireAsync());
    }

    [Fact]
    public async Task DisposeReleasesAndDisposesOnlyOnce()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        Assert.True(await service.TryAcquireAsync());
        service.Dispose();
        service.Dispose();

        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.Equal(1, handle.UnlockCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task LockRangeAndOpenPathAreCorrect()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        Assert.True(await service.TryAcquireAsync());

        Assert.Equal("/runtime/hourglass-linux/hourglass-linux.lock", fileSystem.OpenedPath);
        RecordingLockFileHandle handle = Assert.Single(fileSystem.Handles);
        Assert.Equal(0, handle.LockOffset);
        Assert.Equal(1, handle.LockLength);
        Assert.Contains("app=hourglass-linux", handle.Diagnostics);
        Assert.Contains("pid=123", handle.Diagnostics);
    }

    [Fact]
    public async Task DisposeDoesNotDeleteLockFile()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        Assert.True(await service.TryAcquireAsync());
        service.Dispose();

        Assert.Equal(0, fileSystem.DeleteCount);
    }

    [Fact]
    public async Task StartRequestListenerRequiresOwnership()
    {
        var fileSystem = new RecordingLockFileSystem();
        using var service = CreateService(fileSystem);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartRequestListenerAsync((_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task StartRequestListenerDeletesStaleSocketOnlyAfterOwnership()
    {
        string tempDirectory = CreateTempDirectory();
        var fileSystem = new RecordingLockFileSystem { CreateRealDirectories = true };
        string socketPath = Path.Combine(tempDirectory, "hourglass-linux.sock");
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            socketPath,
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));

        Assert.True(await service.TryAcquireAsync());
        await service.StartRequestListenerAsync((_, _) => Task.CompletedTask);

        Assert.Equal(1, fileSystem.EnsurePrivateDirectoryCount);
        Assert.Equal(tempDirectory, fileSystem.PrivateDirectoryPath);
        Assert.Equal(1, fileSystem.DeleteCount);
        Assert.Equal(socketPath, fileSystem.DeletedPath);
        Assert.True(fileSystem.PrivateDirectoryEnsuredBeforeDelete);
    }

    [Fact]
    public async Task StartRequestListenerCreatesPrivateSocketDirectory()
    {
        string tempDirectory = CreateTempDirectory();
        string socketDirectory = Path.Combine(tempDirectory, "ipc");
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            Path.Combine(socketDirectory, "hourglass-linux.sock"),
            new LinuxFileLockSingleInstanceService.LockFileSystem(),
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));

        Assert.True(await service.TryAcquireAsync());
        await service.StartRequestListenerAsync((_, _) => Task.CompletedTask);

#pragma warning disable CA1416
        UnixFileMode mode = File.GetUnixFileMode(socketDirectory);
#pragma warning restore CA1416
        const UnixFileMode expectedUserMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        const UnixFileMode sharedMode =
            UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        Assert.Equal(expectedUserMode, mode & expectedUserMode);
        Assert.Equal((UnixFileMode)0, mode & sharedMode);
    }

    [Fact]
    public async Task StartRequestListenerDoesNotDeleteStaleSocketWhenPrivateDirectoryFails()
    {
        string tempDirectory = CreateTempDirectory();
        var fileSystem = new RecordingLockFileSystem
        {
            EnsurePrivateDirectoryException = new UnauthorizedAccessException("directory is shared")
        };
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            Path.Combine(tempDirectory, "hourglass-linux.sock"),
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));

        Assert.True(await service.TryAcquireAsync());

        UnauthorizedAccessException exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.StartRequestListenerAsync((_, _) => Task.CompletedTask));

        Assert.Contains("directory is shared", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fileSystem.DeleteCount);
    }

    [Fact]
    public async Task LaunchRequestRoundTripsOverUnixSocket()
    {
        string tempDirectory = CreateTempDirectory();
        var fileSystem = new RecordingLockFileSystem { CreateRealDirectories = true };
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            Path.Combine(tempDirectory, "hourglass-linux.sock"),
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));
        var receivedCompletion = new TaskCompletionSource<SingleInstanceLaunchRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await service.TryAcquireAsync());
        await service.StartRequestListenerAsync((request, _) =>
        {
            receivedCompletion.TrySetResult(request);
            return Task.CompletedTask;
        });

        var sent = new SingleInstanceLaunchRequest(
            SingleInstanceLaunchRequestKind.StartTimer,
            ["--title", "Tea", "5 minutes"],
            "5 minutes",
            "Tea");
        await service.SendLaunchRequestAsync(sent);

        Task completed = await Task.WhenAny(receivedCompletion.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(receivedCompletion.Task, completed);
        SingleInstanceLaunchRequest received = await receivedCompletion.Task;
        Assert.Equal(sent.Kind, received.Kind);
        Assert.Equal(sent.Arguments, received.Arguments);
        Assert.Equal(sent.TimerInput, received.TimerInput);
        Assert.Equal(sent.TimerTitle, received.TimerTitle);
    }

    [Fact]
    public async Task SendLaunchRequestRetriesUntilListenerStarts()
    {
        string tempDirectory = CreateTempDirectory();
        string socketPath = Path.Combine(tempDirectory, "hourglass-linux.sock");
        var fileSystem = new RecordingLockFileSystem { CreateRealDirectories = true };
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            socketPath,
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));
        var receivedCompletion = new TaskCompletionSource<SingleInstanceLaunchRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await service.TryAcquireAsync());

        var sent = new SingleInstanceLaunchRequest(
            SingleInstanceLaunchRequestKind.Activate,
            []);
        Task sendTask = service.SendLaunchRequestAsync(sent);
        await Task.Yield();

        await service.StartRequestListenerAsync((request, _) =>
        {
            receivedCompletion.TrySetResult(request);
            return Task.CompletedTask;
        });

        Task completedSend = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(sendTask, completedSend);
        await sendTask;

        Task completedReceive = await Task.WhenAny(receivedCompletion.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(receivedCompletion.Task, completedReceive);
        SingleInstanceLaunchRequest received = await receivedCompletion.Task;
        Assert.Equal(sent.Kind, received.Kind);
        Assert.Equal(sent.Arguments, received.Arguments);
    }

    [Fact]
    public async Task StalledClientDoesNotBlockLaterLaunchRequests()
    {
        string tempDirectory = CreateTempDirectory();
        string socketPath = Path.Combine(tempDirectory, "hourglass-linux.sock");
        var fileSystem = new RecordingLockFileSystem { CreateRealDirectories = true };
        using var service = new LinuxFileLockSingleInstanceService(
            Path.Combine(tempDirectory, "hourglass-linux.lock"),
            socketPath,
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));
        var receivedCompletion = new TaskCompletionSource<SingleInstanceLaunchRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await service.TryAcquireAsync());
        await service.StartRequestListenerAsync((request, _) =>
        {
            receivedCompletion.TrySetResult(request);
            return Task.CompletedTask;
        });

        using var stalledClient = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await stalledClient.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));

        var sent = new SingleInstanceLaunchRequest(
            SingleInstanceLaunchRequestKind.Activate,
            []);
        await service.SendLaunchRequestAsync(sent);

        Task completed = await Task.WhenAny(receivedCompletion.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(receivedCompletion.Task, completed);
        SingleInstanceLaunchRequest received = await receivedCompletion.Task;
        Assert.Equal(sent.Kind, received.Kind);
        Assert.Equal(sent.Arguments, received.Arguments);
    }

    private static LinuxFileLockSingleInstanceService CreateService(RecordingLockFileSystem fileSystem)
    {
        return new LinuxFileLockSingleInstanceService(
            "/runtime/hourglass-linux/hourglass-linux.lock",
            "/runtime/hourglass-linux/hourglass-linux.sock",
            fileSystem,
            () => 123,
            () => new DateTimeOffset(2026, 6, 14, 8, 0, 0, TimeSpan.Zero));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "hourglass-single-instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingLockFileSystem : LinuxFileLockSingleInstanceService.ILockFileSystem
    {
        public string? CreatedDirectory { get; private set; }

        public string? OpenedPath { get; private set; }

        public int OpenCount { get; private set; }

        public int EnsurePrivateDirectoryCount { get; private set; }

        public int DeleteCount { get; private set; }

        public string? PrivateDirectoryPath { get; private set; }

        public string? DeletedPath { get; private set; }

        public Exception? CreateDirectoryException { get; init; }

        public Exception? EnsurePrivateDirectoryException { get; init; }

        public Exception? OpenException { get; init; }

        public bool CreateRealDirectories { get; init; }

        public bool PrivateDirectoryEnsuredBeforeDelete { get; private set; }

        public RecordingLockFileHandle? NextHandle { get; init; }

        public List<RecordingLockFileHandle> Handles { get; } = [];

        public void CreateDirectory(string path)
        {
            if (this.CreateDirectoryException != null)
            {
                throw this.CreateDirectoryException;
            }

            this.CreatedDirectory = path;
            if (this.CreateRealDirectories)
            {
                Directory.CreateDirectory(path);
            }
        }

        public void EnsurePrivateDirectory(string path)
        {
            if (this.EnsurePrivateDirectoryException != null)
            {
                throw this.EnsurePrivateDirectoryException;
            }

            this.EnsurePrivateDirectoryCount++;
            this.PrivateDirectoryPath = path;
            if (this.CreateRealDirectories)
            {
                Directory.CreateDirectory(path);
#pragma warning disable CA1416
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416
            }
        }

        public void DeleteFileIfExists(string path)
        {
            this.DeleteCount++;
            this.DeletedPath = path;
            this.PrivateDirectoryEnsuredBeforeDelete = this.EnsurePrivateDirectoryCount > 0;
            if (this.CreateRealDirectories)
            {
                File.Delete(path);
            }
        }

        public LinuxFileLockSingleInstanceService.ILockFileHandle OpenLockFile(string path)
        {
            if (this.OpenException != null)
            {
                throw this.OpenException;
            }

            this.OpenCount++;
            this.OpenedPath = path;
            RecordingLockFileHandle handle = this.NextHandle ?? new RecordingLockFileHandle();
            this.Handles.Add(handle);
            return handle;
        }
    }

    private sealed class RecordingLockFileHandle : LinuxFileLockSingleInstanceService.ILockFileHandle
    {
        public bool IsLocked { get; private set; }

        public bool Disposed { get; private set; }

        public long LockOffset { get; private set; }

        public long LockLength { get; private set; }

        public int LockCount { get; private set; }

        public int UnlockCount { get; private set; }

        public int DisposeCount { get; private set; }

        public string Diagnostics { get; private set; } = string.Empty;

        public Exception? ThrowOnLock { get; init; }

        public Exception? ThrowOnWriteDiagnostics { get; init; }

        public void Lock(long offset, long length)
        {
            if (this.ThrowOnLock != null)
            {
                throw this.ThrowOnLock;
            }

            this.LockOffset = offset;
            this.LockLength = length;
            this.LockCount++;
            this.IsLocked = true;
        }

        public void Unlock(long offset, long length)
        {
            Assert.Equal(0, offset);
            Assert.Equal(1, length);
            this.UnlockCount++;
            this.IsLocked = false;
        }

        public void WriteDiagnostics(string contents)
        {
            if (this.ThrowOnWriteDiagnostics != null)
            {
                throw this.ThrowOnWriteDiagnostics;
            }

            this.Diagnostics = contents;
        }

        public void Dispose()
        {
            this.DisposeCount++;
            this.Disposed = true;
        }
    }
}
