namespace Hourglass.Linux.Services;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text.Json;
using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Settings;

/// <summary>A connection to one authoritative application runtime. Mutations are never replayed.</summary>
public sealed class RemoteHourglassClient : IHourglassClient, IAsyncDisposable
{
    private readonly Socket socket;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim requests = new(1, 1);
    private readonly CancellationTokenSource disconnected = new();
    private readonly ConcurrentDictionary<Task, byte> polling = [];
    private int disposed;
    public string AuthorityId { get; private set; } = string.Empty;
    public bool IsConnected => !this.disconnected.IsCancellationRequested;
    private RemoteHourglassClient(Socket socket) { this.socket = socket; this.stream = new(socket, ownsSocket: false); }

    public static async Task<ApplicationResult<RemoteHourglassClient>> ConnectAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) { return new ApplicationResult<RemoteHourglassClient>.Failure(new(ApplicationErrorCode.Unsupported, "Runtime control requires Linux.")); }
        path ??= RuntimeControlTransport.DefaultPath;
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        RemoteHourglassClient? client = null;
        try
        {
            RuntimeControlTransport.VerifyPath(Path.GetDirectoryName(path) ?? throw new IOException("Missing runtime directory."), true);
            RuntimeControlTransport.VerifyPath(path, false);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(RuntimeControlTransport.Deadline);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), deadline.Token).ConfigureAwait(false);
            RuntimeControlTransport.VerifyPeer(socket);
            client = new(socket);
            ApplicationResult<ControlHandshake> handshake = await client.CallAsync<ControlHandshake, bool>("hello", true, null, cancellationToken).ConfigureAwait(false);
            if (handshake is ApplicationResult<ControlHandshake>.Success success && success.Value.ProtocolVersion == RuntimeControlTransport.Version)
            { client.AuthorityId = success.Value.AuthorityId; return new ApplicationResult<RemoteHourglassClient>.Success(client); }
            await client.DisposeAsync().ConfigureAwait(false);
            return new ApplicationResult<RemoteHourglassClient>.Failure(handshake is ApplicationResult<ControlHandshake>.Failure failure
                ? failure.Error : new(ApplicationErrorCode.Unsupported, "Incompatible control handshake."));
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (client != null) { await client.DisposeAsync().ConfigureAwait(false); } else { socket.Dispose(); }
            cancellationToken.ThrowIfCancellationRequested();
            return new ApplicationResult<RemoteHourglassClient>.Failure(new(ApplicationErrorCode.TransportFailure, exception.Message));
        }
    }

    public static async Task<ApplicationResult<RemoteHourglassClient>> ConnectWhenReadyAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        path ??= RuntimeControlTransport.DefaultPath;
        using CancellationTokenSource readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readiness.CancelAfter(RuntimeControlTransport.ReadinessDeadline);
        ApplicationError error = new(ApplicationErrorCode.RuntimeUnavailable, "Authority did not publish its control endpoint.");
        try
        {
            using PeriodicTimer retry = new(RuntimeControlTransport.PollInterval);
            do
            {
                if (File.Exists(path))
                {
                    ApplicationResult<RemoteHourglassClient> result = await ConnectAsync(path, readiness.Token).ConfigureAwait(false);
                    if (result is ApplicationResult<RemoteHourglassClient>.Success) { return result; }
                    error = ((ApplicationResult<RemoteHourglassClient>.Failure)result).Error;
                    if (error.Code == ApplicationErrorCode.Unsupported) { return result; }
                }
            } while (await retry.WaitForNextTickAsync(readiness.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); }
        return new ApplicationResult<RemoteHourglassClient>.Failure(error);
    }

    private async Task<ApplicationResult<T>> CallAsync<T, TPayload>(string kind, TPayload payload, string? sessionId, CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token, this.disconnected.Token);
        deadline.CancelAfter(RuntimeControlTransport.Deadline);
        bool entered = false;
        bool sent = false;
        try
        {
            await this.requests.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            string id = Guid.NewGuid().ToString("N");
            sent = true;
            await RuntimeControlTransport.WriteAsync(this.stream, new ControlRequest(RuntimeControlTransport.Version, id, kind, sessionId,
                RuntimeControlJson.Value(payload)), RuntimeControlTransport.RequestLimit, deadline.Token).ConfigureAwait(false);
            ControlResponse response = await RuntimeControlTransport.ReadAsync<ControlResponse>(this.stream, RuntimeControlTransport.ResponseLimit, deadline.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Runtime disconnected.");
            if (response.ProtocolVersion != RuntimeControlTransport.Version)
            { this.Disconnect(); return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.Unsupported, "Incompatible control protocol.")); }
            if (response.RequestId != id) { throw new InvalidDataException("Invalid control response identity."); }
            if (!response.Success) { return new ApplicationResult<T>.Failure(response.Error ?? new(ApplicationErrorCode.TransportFailure, "Missing control error.")); }
            JsonElement result = response.Result ?? throw new JsonException("Missing control result.");
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out _) && result.TryGetProperty("continuation", out _))
            {
                ControlPage page = RuntimeControlJson.Read<ControlPage>(result);
                List<JsonElement> values = [.. page.Items];
                while (page.Continuation != null)
                {
                    string pageId = Guid.NewGuid().ToString("N");
                    await RuntimeControlTransport.WriteAsync(this.stream, new ControlRequest(RuntimeControlTransport.Version, pageId, "page", null,
                        RuntimeControlJson.Value(new ControlPoll(page.Continuation))), RuntimeControlTransport.RequestLimit, deadline.Token).ConfigureAwait(false);
                    ControlResponse next = await RuntimeControlTransport.ReadAsync<ControlResponse>(this.stream, RuntimeControlTransport.ResponseLimit, deadline.Token).ConfigureAwait(false)
                        ?? throw new EndOfStreamException();
                    if (next.ProtocolVersion != RuntimeControlTransport.Version)
                    { this.Disconnect(); return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.Unsupported, "Incompatible control protocol.")); }
                    if (next.RequestId != pageId) { throw new InvalidDataException("Invalid page response identity."); }
                    if (!next.Success) { return new ApplicationResult<T>.Failure(next.Error ?? new(ApplicationErrorCode.TransportFailure, "Page failed.")); }
                    page = RuntimeControlJson.Read<ControlPage>(next.Result ?? throw new JsonException()); values.AddRange(page.Items);
                }
                result = RuntimeControlJson.Value(values);
            }
            return new ApplicationResult<T>.Success(RuntimeControlJson.Read<T>(result));
        }
        catch (Exception exception) when (exception is IOException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException)
        {
            if (sent) { this.Disconnect(); }
            token.ThrowIfCancellationRequested();
            return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.TransportFailure,
                sent ? "Runtime connection failed; the command outcome may be uncertain. " + exception.Message : "Runtime is disconnected."));
        }
        finally { if (entered) { this.requests.Release(); } }
    }

    public Task<ApplicationResult<TimerSessionSnapshot>> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) =>
        this.CallAsync<TimerSessionSnapshot, object>("create", request, null, cancellationToken);

    public Task<ApplicationResult<TimerSessionSnapshot>> ExecuteAsync(SessionCommand command, CancellationToken cancellationToken = default) =>
        this.CallAsync<TimerSessionSnapshot, SessionCommand>("execute", command, command.SessionId, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ExecuteAllAsync(SessionBatchCommand command, CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<TimerSessionSnapshot>, object>("batch", command, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> StartSavedSessionsAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<TimerSessionSnapshot>, SavedTimerSelection>("saved-start", selection, null, cancellationToken);

    public Task<ApplicationResult<TimerSessionSnapshot>> CreateSavedSessionAsync(SavedTimerDefinition saved, CancellationToken cancellationToken = default) =>
        this.CallAsync<TimerSessionSnapshot, object>("saved-create", saved, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ChangeSavedTimersAsync(SavedTimerChange change, CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<SavedTimerDefinition>, SavedTimerChange>("saved-change", change, null, cancellationToken);

    public Task<ApplicationResult<TimerSessionSnapshot>> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        this.CallAsync<TimerSessionSnapshot, object>("get", sessionId, sessionId, cancellationToken);

    public Task<ApplicationResult<bool>> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("close", sessionId, sessionId, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ListSessionsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<TimerSessionSnapshot>, object>("sessions", true, null, cancellationToken);

    public Task<ApplicationResult<ApplicationDataSnapshot>> GetApplicationDataAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ApplicationDataSnapshot, object>("data", true, null, cancellationToken);

    public Task<ApplicationResult<LinuxAppSettings>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<LinuxAppSettings, object>("settings", true, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ListSavedTimersAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<SavedTimerDefinition>, object>("saved", true, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<string>>> ListRecentInputsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<string>, object>("recent", true, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<string>>> ClearRecentInputsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<string>, object>("recent-clear", true, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<CustomThemeDefinition>>> ListThemesAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<CustomThemeDefinition>, object>("themes", true, null, cancellationToken);

    public Task<ApplicationResult<bool>> FlushPersistenceAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("flush", true, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<SoundAvailability>>> ListSoundsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<SoundAvailability>, object>("sounds", true, null, cancellationToken);

    public Task<ApplicationResult<bool>> PreviewSoundAsync(string soundId, CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("preview", soundId, null, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<ApplicationDiagnostic>>> ListDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        this.CallAsync<ImmutableArray<ApplicationDiagnostic>, object>("diagnostics", true, null, cancellationToken);

    public Task<ApplicationResult<LinuxAppSettings>> ChangeSettingsAsync(LinuxAppSettings previous, LinuxAppSettings requested, CancellationToken cancellationToken = default) =>
        this.CallAsync<LinuxAppSettings, object>("settings-change", new SettingsChange(previous, requested), null, cancellationToken);

    public Task<ApplicationResult<bool>> SaveSavedTimersChangeAsync(SavedTimersDocument previous, SavedTimersDocument requested, CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("saved-document-change", new SavedDocumentChange(previous, requested), null, cancellationToken);

    public Task<ApplicationResult<bool>> ChangeThemesAsync(CustomThemesDocument previous, CustomThemesDocument requested, CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("themes-change", new ThemeDocumentChange(previous, requested), null, cancellationToken);

    public Task<ApplicationResult<bool>> UpdatePresentationAsync(string sessionId, SessionPresentation presentation, CancellationToken cancellationToken = default) =>
        this.CallAsync<bool, object>("presentation", new PresentationChange(sessionId, presentation), sessionId, cancellationToken);

    public Task<ApplicationResult<ForegroundOutcome>> RunForegroundAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) =>
        this.RunOperationAsync<ForegroundOutcome, CreateSessionRequest>("foreground", request, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<ForegroundOutcome>>> RunSavedForegroundAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) =>
        this.RunOperationAsync<ImmutableArray<ForegroundOutcome>, SavedTimerSelection>("saved-foreground", selection, cancellationToken);

    private async Task<ApplicationResult<T>> RunOperationAsync<T, TPayload>(string kind, TPayload payload, CancellationToken token)
    {
        ApplicationResult<ControlOperation> started = await this.CallAsync<ControlOperation, TPayload>(kind, payload, null, token).ConfigureAwait(false);
        if (started is ApplicationResult<ControlOperation>.Failure failed) { return new ApplicationResult<T>.Failure(failed.Error); }
        string id = ((ApplicationResult<ControlOperation>.Success)started).Value.Id;
        try
        {
            using PeriodicTimer timer = new(RuntimeControlTransport.PollInterval);
            while (true)
            {
                ApplicationResult<ControlOperation> result = await this.CallAsync<ControlOperation, ControlPoll>("operation", new(id), null, token).ConfigureAwait(false);
                if (result is ApplicationResult<ControlOperation>.Failure failure) { return new ApplicationResult<T>.Failure(failure.Error); }
                ControlOperation operation = ((ApplicationResult<ControlOperation>.Success)result).Value;
                if (operation.Completed)
                {
                    ControlResponse outcome = operation.Outcome ?? throw new InvalidDataException("Missing foreground result.");
                    return outcome.Success ? new ApplicationResult<T>.Success(RuntimeControlJson.Read<T>(outcome.Result ?? throw new JsonException()))
                        : new ApplicationResult<T>.Failure(outcome.Error ?? new(ApplicationErrorCode.InternalFailure, "Missing operation error."));
                }
                await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (token.IsCancellationRequested && this.IsConnected)
            {
                await this.CallAsync<ControlOperation, ControlPoll>("cancel-operation", new(id), null, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Func<SessionNotification, Task> publish, CancellationToken cancellationToken = default)
    {
        ApplicationResult<ControlSubscription> result = await this.CallAsync<ControlSubscription, bool>("subscribe", true, sessionId, cancellationToken).ConfigureAwait(false);
        if (result is ApplicationResult<ControlSubscription>.Failure failed) { return new ApplicationResult<SessionSubscription>.Failure(failed.Error); }
        ControlSubscription initial = ((ApplicationResult<ControlSubscription>.Success)result).Value;
        SessionSubscription subscription = new(initial.InitialSnapshot, initial.InitialSequence, publish, NoOpDiagnosticSink.Instance);
        Task task = this.PollSubscriptionAsync(initial.Id, subscription);
        this.polling.TryAdd(task, 0);
        _ = task.ContinueWith(completed => this.polling.TryRemove(completed, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new ApplicationResult<SessionSubscription>.Success(subscription);
    }
    private async Task PollSubscriptionAsync(string id, SessionSubscription subscription)
    {
        try
        {
            using PeriodicTimer timer = new(RuntimeControlTransport.PollInterval);
            while (!subscription.IsDisposed)
            {
                ApplicationResult<ControlNotifications> result = await this.CallAsync<ControlNotifications, ControlPoll>("notifications", new(id), null, this.disconnected.Token).ConfigureAwait(false);
                if (result is ApplicationResult<ControlNotifications>.Failure failed) { subscription.Fail(failed.Error); return; }
                ControlNotifications updates = ((ApplicationResult<ControlNotifications>.Success)result).Value;
                foreach (SessionNotification notification in updates.Items)
                {
                    subscription.Publish(notification);
                    // Drain each delivery to bound remote callback backlog and preserve notification order.
                    await subscription.PendingDelivery.WaitAsync(this.disconnected.Token).ConfigureAwait(false);
                }
                if (updates.Completed) { subscription.Complete(); return; }
                await timer.WaitForNextTickAsync(this.disconnected.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { subscription.Fail(new(ApplicationErrorCode.TransportFailure, "Runtime disconnected.")); }
        finally
        {
            if (this.IsConnected) { await this.CallAsync<bool, ControlPoll>("unsubscribe", new(id), null, CancellationToken.None).ConfigureAwait(false); }
        }
    }
    private void Disconnect() { this.disconnected.Cancel(); this.socket.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0) { return; }
        this.Disconnect();
        try { await Task.WhenAll(this.polling.Keys).WaitAsync(RuntimeControlTransport.Deadline).ConfigureAwait(false); }
        catch (TimeoutException) { }
        await this.stream.DisposeAsync().ConfigureAwait(false);
    }
}
