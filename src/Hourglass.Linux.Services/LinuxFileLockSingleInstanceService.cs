namespace Hourglass.Linux.Services;

using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hourglass.Platform;

public sealed class LinuxFileLockSingleInstanceService : ISingleInstanceService
{
    private const string ApplicationId = "hourglass-linux";
    private const int IpcTimeoutMilliseconds = 2000;
    private const long LockLength = 1;
    private const long LockOffset = 0;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILockFileSystem fileSystem;
    private readonly Func<DateTimeOffset> getTimestamp;
    private readonly Func<int> getProcessId;
    private readonly string lockPath;
    private readonly string socketPath;
    private readonly object syncRoot = new();

    private CancellationTokenSource? listenerCancellation;
    private ILockFileHandle? lockHandle;
    private Task? listenerTask;
    private bool disposed;

    public LinuxFileLockSingleInstanceService()
        : this(
            LinuxSingleInstanceLockPath.Resolve(
                Environment.GetEnvironmentVariable,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            LinuxSingleInstanceLockPath.ResolveSocketPath(
                Environment.GetEnvironmentVariable,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            new LockFileSystem(),
            () => Environment.ProcessId,
            () => DateTimeOffset.UtcNow)
    {
    }

    internal LinuxFileLockSingleInstanceService(
        string lockPath,
        string socketPath,
        ILockFileSystem fileSystem,
        Func<int> getProcessId,
        Func<DateTimeOffset> getTimestamp)
    {
        this.lockPath = string.IsNullOrWhiteSpace(lockPath)
            ? throw new ArgumentException("Lock path must not be empty.", nameof(lockPath))
            : lockPath;
        this.socketPath = string.IsNullOrWhiteSpace(socketPath)
            ? throw new ArgumentException("Socket path must not be empty.", nameof(socketPath))
            : socketPath;
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.getProcessId = getProcessId ?? throw new ArgumentNullException(nameof(getProcessId));
        this.getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
    }

    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.syncRoot)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.lockHandle != null)
            {
                return Task.FromResult(true);
            }

            string directory = Path.GetDirectoryName(this.lockPath)
                ?? throw new InvalidOperationException("Lock path must include a directory.");
            this.fileSystem.CreateDirectory(directory);

            ILockFileHandle candidate = this.fileSystem.OpenLockFile(this.lockPath);
            bool candidateDisposed = false;

            try
            {
                try
                {
                    candidate.Lock(LockOffset, LockLength);
                }
                catch (IOException)
                {
                    candidate.Dispose();
                    candidateDisposed = true;
                    return Task.FromResult(false);
                }

                try
                {
                    candidate.WriteDiagnostics(CreateDiagnostics(this.getProcessId(), this.getTimestamp()));
                }
                catch
                {
                    candidate.Unlock(LockOffset, LockLength);
                    candidate.Dispose();
                    candidateDisposed = true;
                    throw;
                }

                this.lockHandle = candidate;
                return Task.FromResult(true);
            }
            catch
            {
                if (!candidateDisposed && !ReferenceEquals(this.lockHandle, candidate))
                {
                    candidate.Dispose();
                }

                throw;
            }
        }
    }

    public async Task SendLaunchRequestAsync(
        SingleInstanceLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(IpcTimeoutMilliseconds));

        using Socket socket = await this.ConnectWithRetryAsync(timeoutSource.Token).ConfigureAwait(false);
        await using NetworkStream stream = new(socket, ownsSocket: false);
        await using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true);
        string payload = JsonSerializer.Serialize(ToDto(request));
        await writer.WriteLineAsync(payload.AsMemory(), timeoutSource.Token).ConfigureAwait(false);
        await writer.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
        socket.Shutdown(SocketShutdown.Send);
    }

    public Task StartRequestListenerAsync(
        Func<SingleInstanceLaunchRequest, CancellationToken, Task> handleRequestAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleRequestAsync);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.syncRoot)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.lockHandle == null)
            {
                throw new InvalidOperationException("The single-instance service must own the lock before listening.");
            }

            if (this.listenerTask != null)
            {
                return Task.CompletedTask;
            }

            string directory = Path.GetDirectoryName(this.socketPath)
                ?? throw new InvalidOperationException("Socket path must include a directory.");
            this.fileSystem.CreateDirectory(directory);
            this.fileSystem.DeleteFileIfExists(this.socketPath);

            this.listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken listenerToken = this.listenerCancellation.Token;
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(this.socketPath));
                listener.Listen(backlog: 8);
            }
            catch
            {
                listener.Dispose();
                this.listenerCancellation.Dispose();
                this.listenerCancellation = null;
                throw;
            }

            this.listenerTask = Task.Run(() => this.ListenAsync(listener, handleRequestAsync, listenerToken), CancellationToken.None);
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        Task? taskToWait = null;

        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.listenerCancellation?.Cancel();
            taskToWait = this.listenerTask;

            if (this.lockHandle != null)
            {
                try
                {
                    this.lockHandle.Unlock(LockOffset, LockLength);
                }
                finally
                {
                    this.lockHandle.Dispose();
                    this.lockHandle = null;
                }
            }
        }

        try
        {
            taskToWait?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            this.listenerCancellation?.Dispose();
            this.listenerCancellation = null;
        }
    }

    internal static string CreateDiagnostics(int processId, DateTimeOffset timestamp)
    {
        return $"app={ApplicationId}{Environment.NewLine}pid={processId}{Environment.NewLine}acquiredUtc={timestamp:O}{Environment.NewLine}";
    }

    private async Task<Socket> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var endpoint = new UnixDomainSocketEndPoint(this.socketPath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return socket;
            }
            catch (SocketException exception) when (IsTransientConnectFailure(exception))
            {
                socket.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static bool IsTransientConnectFailure(SocketException exception)
    {
        return exception.SocketErrorCode == SocketError.ConnectionRefused
            || exception.SocketErrorCode == SocketError.AddressNotAvailable
            || exception.NativeErrorCode == 2;
    }

    private async Task ListenAsync(
        Socket listener,
        Func<SingleInstanceLaunchRequest, CancellationToken, Task> handleRequestAsync,
        CancellationToken cancellationToken)
    {
        using (listener)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket? accepted = null;
                try
                {
                    accepted = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    await this.HandleAcceptedSocketAsync(accepted, handleRequestAsync, cancellationToken)
                        .ConfigureAwait(false);
                    accepted = null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    accepted?.Dispose();
                }
            }
        }
    }

    private async Task HandleAcceptedSocketAsync(
        Socket socket,
        Func<SingleInstanceLaunchRequest, CancellationToken, Task> handleRequestAsync,
        CancellationToken cancellationToken)
    {
        using (socket)
        await using (var stream = new NetworkStream(socket, ownsSocket: false))
        {
            try
            {
                using var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);
                string? payload = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return;
                }

                SingleInstanceLaunchRequestDto? dto = JsonSerializer.Deserialize<SingleInstanceLaunchRequestDto>(payload);
                SingleInstanceLaunchRequest? request = FromDto(dto);
                if (request == null)
                {
                    return;
                }

                await handleRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static SingleInstanceLaunchRequestDto ToDto(SingleInstanceLaunchRequest request)
    {
        return new SingleInstanceLaunchRequestDto
        {
            Kind = request.Kind,
            Arguments = request.Arguments.ToArray(),
            TimerInput = request.TimerInput,
            TimerTitle = request.TimerTitle
        };
    }

    private static SingleInstanceLaunchRequest? FromDto(SingleInstanceLaunchRequestDto? dto)
    {
        if (dto?.Kind is not (SingleInstanceLaunchRequestKind.Activate or SingleInstanceLaunchRequestKind.StartTimer))
        {
            return null;
        }

        if (dto.Kind == SingleInstanceLaunchRequestKind.StartTimer && string.IsNullOrWhiteSpace(dto.TimerInput))
        {
            return null;
        }

        return new SingleInstanceLaunchRequest(
            dto.Kind,
            dto.Arguments ?? [],
            dto.TimerInput,
            dto.TimerTitle);
    }

    internal interface ILockFileSystem
    {
        void CreateDirectory(string path);

        void DeleteFileIfExists(string path);

        ILockFileHandle OpenLockFile(string path);
    }

    internal interface ILockFileHandle : IDisposable
    {
        void Lock(long offset, long length);

        void Unlock(long offset, long length);

        void WriteDiagnostics(string contents);
    }

    private sealed class LockFileSystem : ILockFileSystem
    {
        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public void DeleteFileIfExists(string path)
        {
            File.Delete(path);
        }

        public ILockFileHandle OpenLockFile(string path)
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new FileStreamLockFileHandle(stream);
        }
    }

    private sealed class FileStreamLockFileHandle(FileStream stream) : ILockFileHandle
    {
        private readonly FileStream stream = stream;

        public void Lock(long offset, long length)
        {
#pragma warning disable CA1416
            this.stream.Lock(offset, length);
#pragma warning restore CA1416
        }

        public void Unlock(long offset, long length)
        {
#pragma warning disable CA1416
            this.stream.Unlock(offset, length);
#pragma warning restore CA1416
        }

        public void WriteDiagnostics(string contents)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            this.stream.SetLength(0);
            this.stream.Position = 0;
            this.stream.Write(bytes, 0, bytes.Length);
            this.stream.Flush(flushToDisk: true);
        }

        public void Dispose()
        {
            this.stream.Dispose();
        }
    }

    private sealed class SingleInstanceLaunchRequestDto
    {
        public SingleInstanceLaunchRequestKind Kind { get; set; }

        public string[]? Arguments { get; set; }

        public string? TimerInput { get; set; }

        public string? TimerTitle { get; set; }
    }
}
