namespace Hourglass.Linux.Services;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hourglass.Platform;

public sealed class LinuxFileLockSingleInstanceService : ISingleInstanceService
{
    private const string ApplicationId = "hourglass-linux";
    private const int IpcTimeoutMilliseconds = 2000;
    private const int MaxPayloadBytes = 8 * 1024;
    private const int MaxConcurrentHandlers = 4;
    private const long LockLength = 1;
    private const long LockOffset = 0;
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode SharedDirectoryMode =
        UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherWrite
        | UnixFileMode.OtherExecute;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILockFileSystem fileSystem;
    private readonly Func<DateTimeOffset> getTimestamp;
    private readonly Func<int> getProcessId;
    private readonly string lockPath;
    private readonly string socketPath;
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<Task, byte> activeHandlers = [];
    private readonly SemaphoreSlim handlerGate = new(MaxConcurrentHandlers, MaxConcurrentHandlers);

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
        string payload = JsonSerializer.Serialize(ToDto(request));
        await WriteFramedPayloadAsync(stream, payload, timeoutSource.Token).ConfigureAwait(false);
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
            this.fileSystem.EnsurePrivateDirectory(directory);
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
        Task[] tasksToWait = [];

        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.listenerCancellation?.Cancel();
            tasksToWait = this.GetOwnedTasksSnapshot();

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
            Task.WaitAll(tasksToWait);
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
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
                    this.TrackHandler(this.HandleAcceptedSocketAsync(accepted, handleRequestAsync, cancellationToken));
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
            bool gateAcquired = false;
            try
            {
                await this.handlerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateAcquired = true;

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(IpcTimeoutMilliseconds));

                string? payload = await ReadFramedPayloadAsync(stream, timeoutSource.Token).ConfigureAwait(false);
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
            finally
            {
                if (gateAcquired)
                {
                    this.handlerGate.Release();
                }
            }
        }
    }

    private void TrackHandler(Task task)
    {
        this.activeHandlers.TryAdd(task, 0);
        task.ContinueWith(
            completedTask => this.activeHandlers.TryRemove(completedTask, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private Task[] GetOwnedTasksSnapshot()
    {
        List<Task> tasks = [];
        if (this.listenerTask != null)
        {
            tasks.Add(this.listenerTask);
        }

        tasks.AddRange(this.activeHandlers.Keys);
        return [.. tasks];
    }

    private static async Task WriteFramedPayloadAsync(
        Stream stream,
        string payload,
        CancellationToken cancellationToken)
    {
        byte[] payloadBytes = Utf8NoBom.GetBytes(payload);
        if (payloadBytes.Length == 0 || payloadBytes.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException("Single-instance request payload size is invalid.");
        }

        byte[] lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)payloadBytes.Length);
        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadFramedPayloadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = new byte[sizeof(int)];
        if (!await ReadExactOrEndAsync(stream, lengthPrefix, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lengthPrefix);
        if (payloadLength == 0 || payloadLength > MaxPayloadBytes)
        {
            return null;
        }

        byte[] payloadBytes = new byte[payloadLength];
        if (!await ReadExactOrEndAsync(stream, payloadBytes, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Utf8NoBom.GetString(payloadBytes);
    }

    private static async Task<bool> ReadExactOrEndAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return false;
            }

            offset += bytesRead;
        }

        return true;
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

        void EnsurePrivateDirectory(string path);

        void DeleteFileIfExists(string path);

        ILockFileHandle OpenLockFile(string path);
    }

    internal interface ILockFileHandle : IDisposable
    {
        void Lock(long offset, long length);

        void Unlock(long offset, long length);

        void WriteDiagnostics(string contents);
    }

    internal sealed class LockFileSystem : ILockFileSystem
    {
        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public void EnsurePrivateDirectory(string path)
        {
            Directory.CreateDirectory(path);

#pragma warning disable CA1416
            File.SetUnixFileMode(path, PrivateDirectoryMode);
            UnixFileMode mode = File.GetUnixFileMode(path);
#pragma warning restore CA1416
            if ((mode & SharedDirectoryMode) != 0)
            {
                throw new UnauthorizedAccessException($"Directory '{path}' is not private to the current user.");
            }
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
