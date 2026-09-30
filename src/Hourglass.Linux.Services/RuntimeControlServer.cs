namespace Hourglass.Linux.Services;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text.Json;
using Hourglass.Application;
using Hourglass.Platform;

/// <summary>Serves application commands while its caller holds the runtime authority lock.</summary>
public sealed class RuntimeControlServer : IAsyncDisposable
{
    private const int MaxConnections = 32;
    private readonly IHourglassClient client;
    private readonly string path;
    private readonly string authorityId = Guid.NewGuid().ToString("N");
    private readonly IDiagnosticSink diagnostics;
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource forceDisconnect = new();
    private readonly object disposalGate = new();
    private Task? disposal;
    private readonly SemaphoreSlim connections = new(MaxConnections, MaxConnections);
    private readonly SemaphoreSlim handlers = new(4, 4);
    private readonly ConcurrentDictionary<Task, byte> pending = [];
    private Socket? listener;
    private Task accept = Task.CompletedTask;

    public RuntimeControlServer(IHourglassClient client, string? path = null, IDiagnosticSink? diagnostics = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.path = path ?? RuntimeControlTransport.DefaultPath;
        this.diagnostics = diagnostics ?? NoOpDiagnosticSink.Instance;
    }

    public void Start()
    {
        if (!OperatingSystem.IsLinux()) { throw new PlatformNotSupportedException("Runtime control requires Linux."); }
        if (this.stopping.IsCancellationRequested) { throw new ObjectDisposedException(nameof(RuntimeControlServer)); }
        if (this.listener != null) { throw new InvalidOperationException("Control server already started."); }
        string directory = Path.GetDirectoryName(this.path) ?? throw new InvalidOperationException("Missing control directory.");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RuntimeControlTransport.VerifyPath(directory, directory: true);
        if (File.Exists(this.path)) { RuntimeControlTransport.VerifyPath(this.path, directory: false); File.Delete(this.path); }
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(this.path));
            File.SetUnixFileMode(this.path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            socket.Listen(MaxConnections);
            this.listener = socket;
            this.accept = this.AcceptAsync(socket);
        }
        catch { socket.Dispose(); throw; }
    }

    private async Task AcceptAsync(Socket socket)
    {
        try
        {
            while (!this.stopping.IsCancellationRequested)
            {
                await this.connections.WaitAsync(this.stopping.Token).ConfigureAwait(false);
                Socket accepted;
                try { accepted = await socket.AcceptAsync(this.stopping.Token).ConfigureAwait(false); }
                catch { this.connections.Release(); throw; }
                Task task = this.ServeAsync(accepted);
                this.pending.TryAdd(task, 0);
                _ = task.ContinueWith(completed => this.pending.TryRemove(completed, out _), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException && this.stopping.IsCancellationRequested) { }
    }

    private async Task ServeAsync(Socket socket)
    {
        using (socket)
        using (CancellationTokenSource disconnected = CancellationTokenSource.CreateLinkedTokenSource(this.forceDisconnect.Token))
        await using (NetworkStream stream = new(socket, ownsSocket: false))
        {
            ConnectionState state = new(this.client, this.authorityId, disconnected.Token);
            try
            {
                RuntimeControlTransport.VerifyPeer(socket);
                while (!disconnected.IsCancellationRequested)
                {
                    // Idle clients are allowed; once a frame begins its entire body has a deadline.
                    byte[] first = new byte[1];
                    if (await stream.ReadAsync(first, disconnected.Token).ConfigureAwait(false) == 0) { break; }
                    using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(disconnected.Token);
                    deadline.CancelAfter(RuntimeControlTransport.Deadline);
                    await using PrefixStream framed = new(first[0], stream);
                    ControlRequest? request = await RuntimeControlTransport.ReadAsync<ControlRequest>(framed, RuntimeControlTransport.RequestLimit, deadline.Token).ConfigureAwait(false);
                    if (request == null) { break; }
                    await this.handlers.WaitAsync(deadline.Token).ConfigureAwait(false);
                    ControlResponse response;
                    try
                    {
                        response = this.stopping.IsCancellationRequested && request.RequestKind is not ("flush" or "diagnostics" or "operation" or "cancel-operation" or "unsubscribe" or "notifications")
                            ? RuntimeControlDispatch.Error(request.RequestId, new(ApplicationErrorCode.RuntimeUnavailable, "Runtime authority is shutting down."))
                            : await state.ExecuteAsync(request, deadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
                    { response = RuntimeControlDispatch.Error(request.RequestId, new(ApplicationErrorCode.Validation, exception.Message)); }
                    finally { this.handlers.Release(); }
                    await RuntimeControlTransport.WriteAsync(stream, response, RuntimeControlTransport.ResponseLimit, deadline.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or JsonException or UnauthorizedAccessException)
            {
                if (!this.stopping.IsCancellationRequested)
                { this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort, "control", "connection", "socket", "Control connection ended.", exception)); }
            }
            finally
            {
                disconnected.Cancel();
                await state.DisposeAsync().ConfigureAwait(false);
                this.connections.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (this.disposalGate) { return new(this.disposal ??= this.ShutdownAsync()); }
    }

    private async Task ShutdownAsync()
    {
        await this.stopping.CancelAsync().ConfigureAwait(false);
        this.listener?.Dispose();
        await this.accept.ConfigureAwait(false);
        Task drain = Task.WhenAll(this.pending.Keys);
        try
        {
            // Existing commands may finish their response, durability check, and diagnostics before disconnecting.
            await drain.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await this.forceDisconnect.CancelAsync().ConfigureAwait(false);
            try { await drain.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception exception)
            { this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort, "control", "shutdown", "socket", "Control shutdown did not drain cleanly.", exception)); }
        }
        await this.forceDisconnect.CancelAsync().ConfigureAwait(false);
        if (this.listener != null) { File.Delete(this.path); this.listener = null; }
    }

    private sealed class ConnectionState(IHourglassClient client, string authorityId, CancellationToken disconnected) : IAsyncDisposable
    {
        private readonly Dictionary<string, RemoteSubscription> subscriptions = [];
        private readonly Dictionary<string, (CancellationTokenSource Cancellation, Task<ControlResponse> Task)> operations = [];
        private readonly Dictionary<string, ImmutableArray<JsonElement>> pages = [];
        private bool negotiated;

        internal async Task<ControlResponse> ExecuteAsync(ControlRequest request, CancellationToken token)
        {
            string id = request.RequestId;
            if (string.IsNullOrWhiteSpace(id)) { return Error(id, ApplicationErrorCode.Validation, "Missing request ID."); }
            if (request.ProtocolVersion != RuntimeControlTransport.Version) { return Error(id, ApplicationErrorCode.Unsupported, "Incompatible control protocol."); }
            if (request.RequestKind == "hello")
            {
                this.negotiated = true;
                return Success(id, new ControlHandshake(authorityId, RuntimeControlTransport.Version));
            }
            if (!this.negotiated) { return Error(id, ApplicationErrorCode.Unsupported, "Control handshake required."); }
            switch (request.RequestKind)
            {
                case "subscribe":
                    if (this.subscriptions.Count >= 32) { return Error(id, ApplicationErrorCode.TransportFailure, "Subscription limit reached."); }
                    RemoteSubscription queue = new();
                    ApplicationResult<SessionSubscription> subscription = await client.SubscribeAsync(request.SessionId ?? throw new JsonException("Missing session ID."), queue.PublishAsync, token).ConfigureAwait(false);
                    if (subscription is ApplicationResult<SessionSubscription>.Failure failed) { return RuntimeControlDispatch.Error(id, failed.Error); }
                    queue.Subscription = ((ApplicationResult<SessionSubscription>.Success)subscription).Value;
                    string subscriptionId = Guid.NewGuid().ToString("N");
                    this.subscriptions.Add(subscriptionId, queue);
                    return Success(id, new ControlSubscription(subscriptionId, queue.Subscription.InitialSnapshot, queue.Subscription.InitialSequence));
                case "notifications":
                    string selected = RuntimeControlJson.Read<ControlPoll>(request.Payload).Id;
                    if (!this.subscriptions.TryGetValue(selected, out RemoteSubscription? selectedQueue)) { return Error(id, ApplicationErrorCode.NotFound, "Unknown subscription."); }
                    return selectedQueue.Poll(id);
                case "unsubscribe":
                    if (this.subscriptions.Remove(RuntimeControlJson.Read<ControlPoll>(request.Payload).Id, out RemoteSubscription? removed)) { removed.Subscription?.Dispose(); }
                    return Success(id, true);
                case "foreground":
                case "saved-foreground":
                    if (this.operations.Count >= 8) { return Error(id, ApplicationErrorCode.TransportFailure, "Foreground operation limit reached."); }
                    CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(disconnected);
                    Task<ControlResponse> operation;
                    if (request.RequestKind == "foreground")
                    {
                        CreateSessionRequest start = RuntimeControlJson.Read<CreateSessionRequest>(request.Payload);
                        operation = CompleteAsync(client.RunForegroundAsync(start, cancellation.Token));
                    }
                    else
                    {
                        SavedTimerSelection selection = RuntimeControlJson.Read<SavedTimerSelection>(request.Payload);
                        operation = CompleteAsync(client.RunSavedForegroundAsync(selection, cancellation.Token));
                    }
                    string operationId = Guid.NewGuid().ToString("N");
                    this.operations.Add(operationId, (cancellation, operation));
                    return Success(id, new ControlOperation(operationId, false));
                case "operation":
                case "cancel-operation":
                    string target = RuntimeControlJson.Read<ControlPoll>(request.Payload).Id;
                    if (!this.operations.TryGetValue(target, out var active)) { return Error(id, ApplicationErrorCode.NotFound, "Unknown foreground operation."); }
                    if (request.RequestKind == "cancel-operation") { active.Cancellation.Cancel(); }
                    if (!active.Task.IsCompleted) { return Success(id, new ControlOperation(target, false)); }
                    ControlResponse outcome = await active.Task.ConfigureAwait(false);
                    this.operations.Remove(target);
                    active.Cancellation.Dispose();
                    return Success(id, new ControlOperation(target, true, outcome));
                case "page":
                    string cursor = RuntimeControlJson.Read<ControlPoll>(request.Payload).Id;
                    if (!this.pages.Remove(cursor, out ImmutableArray<JsonElement> values)) { return Error(id, ApplicationErrorCode.NotFound, "Unknown page token."); }
                    return this.Page(id, values);
                default:
                    ControlResponse result = await RuntimeControlDispatch.ExecuteAsync(client, request, token).ConfigureAwait(false);
                    return result.Success && result.Result is { ValueKind: JsonValueKind.Array } array
                        ? this.Page(id, array.EnumerateArray().Select(value => value.Clone()).ToImmutableArray()) : result;
            }
        }

        private ControlResponse Page(string id, ImmutableArray<JsonElement> values)
        {
            int count = 0;
            int bytes = 512;
            while (count < values.Length && count < 64)
            {
                int size = System.Text.Encoding.UTF8.GetByteCount(values[count].GetRawText()) + 1;
                if (bytes + size > RuntimeControlTransport.ResponseLimit / 2) { break; }
                bytes += size; count++;
            }
            if (count == 0 && values.Length > 0) { return Error(id, ApplicationErrorCode.TransportFailure, "Collection item exceeds its response limit."); }
            string? cursor = null;
            if (count < values.Length)
            {
                if (this.pages.Count >= 32) { return Error(id, ApplicationErrorCode.TransportFailure, "Page limit reached."); }
                cursor = Guid.NewGuid().ToString("N"); this.pages.Add(cursor, values[count..]);
            }
            return Success(id, new ControlPage(values[..count], cursor));
        }

        private static async Task<ControlResponse> CompleteAsync<T>(Task<ApplicationResult<T>> operation)
        {
            try { return RuntimeControlDispatch.Result(string.Empty, await operation.ConfigureAwait(false)); }
            catch (OperationCanceledException) { return Error(string.Empty, ApplicationErrorCode.InvalidTransition, "Foreground operation cancelled."); }
            catch (Exception) { return Error(string.Empty, ApplicationErrorCode.InternalFailure, "Foreground operation failed."); }
        }
        private static ControlResponse Success<T>(string id, T value) => new(RuntimeControlTransport.Version, id, true, RuntimeControlJson.Value(value), null);
        private static ControlResponse Error(string id, ApplicationErrorCode code, string message) => RuntimeControlDispatch.Error(id, new(code, message));
        public async ValueTask DisposeAsync()
        {
            foreach (RemoteSubscription subscription in this.subscriptions.Values) { subscription.Subscription?.Dispose(); }
            foreach (var operation in this.operations.Values) { operation.Cancellation.Cancel(); }
            try { await Task.WhenAll(this.operations.Values.Select(value => value.Task)).WaitAsync(RuntimeControlTransport.Deadline).ConfigureAwait(false); }
            catch (TimeoutException) { }
            foreach (var operation in this.operations.Values) { operation.Cancellation.Dispose(); }
        }
    }

    internal sealed class RemoteSubscription
    {
        private readonly object gate = new();
        private readonly Queue<SessionNotification> notifications = [];
        private bool overflow;
        internal SessionSubscription? Subscription { get; set; }
        internal Task PublishAsync(SessionNotification notification)
        {
            lock (this.gate)
            {
                if (this.notifications.Count >= 256) { this.overflow = true; }
                else { this.notifications.Enqueue(notification); }
            }
            return Task.CompletedTask;
        }
        internal ControlResponse Poll(string id)
        {
            lock (this.gate)
            {
                if (this.overflow) { return RuntimeControlDispatch.Error(id, new(ApplicationErrorCode.TransportFailure, "Subscription overflow; reconnect explicitly.")); }
                ImmutableArray<SessionNotification> items = this.notifications.Take(32).ToImmutableArray();
                for (int index = 0; index < items.Length; index++) { this.notifications.Dequeue(); }
                return new(RuntimeControlTransport.Version, id, true, RuntimeControlJson.Value(new ControlNotifications(items,
                    this.notifications.Count == 0 && this.Subscription?.Completion.IsCompleted == true)), null);
            }
        }
    }

    private sealed class PrefixStream(byte first, Stream inner) : Stream
    {
        private bool consumed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!this.consumed && buffer.Length > 0) { buffer.Span[0] = first; this.consumed = true; return 1; }
            return await inner.ReadAsync(buffer, token).ConfigureAwait(false);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
