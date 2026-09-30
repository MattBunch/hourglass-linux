namespace Hourglass.Linux.Services.Tests;

using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text.Json;
using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class RuntimeControlTests
{
    [Fact]
    public async Task TwoClientsMutateTheSameAuthoritativeSession()
    {
        await using Fixture fixture = new();
        await using RemoteHourglassClient first = await fixture.ConnectAsync();
        await using RemoteHourglassClient second = await fixture.ConnectAsync();
        Assert.Equal(first.AuthorityId, second.AuthorityId);
        Success(await first.CreateSessionAsync(Request("shared")));
        Success(await first.ExecuteAsync(new SessionCommand.Start("shared")));
        TimerSessionSnapshot paused = Success(await second.ExecuteAsync(new SessionCommand.Pause("shared")));
        Assert.Equal(TimerState.Paused, paused.Countdown.State);
        Assert.Equal(RuntimeControlJson.Value(paused).GetRawText(), RuntimeControlJson.Value(Success(await first.GetSessionAsync("shared"))).GetRawText());
        Assert.Single(Success(await fixture.Runtime.ListSessionsAsync()));
        await first.DisposeAsync();
        Assert.Equal(TimerState.Paused, Success(await second.GetSessionAsync("shared")).Countdown.State);
    }

    [Fact]
    public async Task SubscriptionPreservesOrderedEffectsAndCompletion()
    {
        await using Fixture fixture = new();
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        Success(await client.CreateSessionAsync(Request("events")));
        List<SessionNotification> observed = [];
        TaskCompletionSource removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using SessionSubscription subscription = Success(await client.SubscribeAsync("events", notification =>
        {
            lock (observed) { observed.Add(notification); }
            if (notification.Removed) { removed.SetResult(); }
            return Task.CompletedTask;
        }));
        Success(await client.ExecuteAsync(new SessionCommand.Start("events")));
        Success(await client.ExecuteAsync(new SessionCommand.Pause("events")));
        Success(await client.CloseSessionAsync("events"));
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await subscription.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        lock (observed)
        {
            Assert.Equal(3, observed.Count);
            Assert.Equal(observed.Select(item => item.Sequence).Order(), observed.Select(item => item.Sequence));
            Assert.Equal(CountdownEffect.Started, observed[0].Effects.First);
            Assert.Contains(CountdownEffect.Paused, new[] { observed[1].Effects.First, observed[1].Effects.Second });
            Assert.True(observed[2].Removed);
        }
    }

    [Fact]
    public async Task CollectionPagesAreAggregatedWithoutMissingSessions()
    {
        await using Fixture fixture = new();
        for (int index = 0; index < 130; index++) { Success(await fixture.Runtime.CreateSessionAsync(Request("session-" + index))); }
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        ImmutableArray<TimerSessionSnapshot> sessions = Success(await client.ListSessionsAsync());
        Assert.Equal(130, sessions.Length);
        Assert.Equal(130, sessions.Select(item => item.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task SettingsSavedSelectorsAndThemeDocumentsCrossTheBoundary()
    {
        await using Fixture fixture = new();
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        LinuxAppSettings before = Success(await client.GetSettingsAsync());
        Assert.False(Success(await client.ChangeSettingsAsync(before, before with { NotificationsEnabled = false })).NotificationsEnabled);
        SavedTimerDefinition saved = new("tea", "25m", "Tea");
        Assert.Single(Success(await client.ChangeSavedTimersAsync(new SavedTimerChange.Add(saved))));
        Assert.Single(Success(await client.StartSavedSessionsAsync(new SavedTimerSelection.ByNameOrId("tea"))));
        ApplicationDataSnapshot data = Success(await client.GetApplicationDataAsync());
        Assert.Single(data.SavedTimers.Timers);
        Success(await client.ChangeThemesAsync(data.CustomThemes, data.CustomThemes));
    }

    [Fact]
    public async Task AuthorityShutdownDisconnectsClientsWithoutRemovingRecoverySessions()
    {
        await using Fixture fixture = new();
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        Success(await client.CreateSessionAsync(Request("recover")));
        await fixture.Server.DisposeAsync();
        ApplicationResult<TimerSessionSnapshot>.Failure failure = Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await client.GetSessionAsync("recover"));
        Assert.Equal(ApplicationErrorCode.TransportFailure, failure.Error.Code);
        Assert.False(client.IsConnected);
        Assert.Single(Success(await fixture.Runtime.ListSessionsAsync()));
    }

    [Theory]
    [InlineData(99, "hello", ApplicationErrorCode.Unsupported)]
    [InlineData(1, "unknown", ApplicationErrorCode.Unsupported)]
    public async Task UnsupportedVersionOrRequestReturnsTypedError(int version, string kind, ApplicationErrorCode code)
    {
        await using Fixture fixture = new();
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(fixture.Path));
        await using NetworkStream stream = new(socket, ownsSocket: false);
        await RuntimeControlTransport.WriteAsync(stream, new ControlRequest(1, "hello", "hello", null, RuntimeControlJson.Value(true)), RuntimeControlTransport.RequestLimit, CancellationToken.None);
        Assert.True((await RuntimeControlTransport.ReadAsync<ControlResponse>(stream, RuntimeControlTransport.ResponseLimit, CancellationToken.None))?.Success);
        await RuntimeControlTransport.WriteAsync(stream, new ControlRequest(version, "request", kind, null, RuntimeControlJson.Value(true)), RuntimeControlTransport.RequestLimit, CancellationToken.None);
        ControlResponse response = await RuntimeControlTransport.ReadAsync<ControlResponse>(stream, RuntimeControlTransport.ResponseLimit, CancellationToken.None) ?? throw new IOException();
        Assert.False(response.Success);
        Assert.Equal(code, response.Error?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    public async Task InvalidFrameLengthsAreRejectedBeforeAllocation(int length)
    {
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        await using MemoryStream stream = new(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeControlTransport.ReadAsync<ControlRequest>(stream, RuntimeControlTransport.RequestLimit, CancellationToken.None));
    }

    [Fact]
    public async Task PartialFramesFailAndEofIsDistinct()
    {
        await using MemoryStream partial = new([0, 0]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => RuntimeControlTransport.ReadAsync<ControlRequest>(partial, RuntimeControlTransport.RequestLimit, CancellationToken.None));
        await using MemoryStream empty = new();
        Assert.Null(await RuntimeControlTransport.ReadAsync<ControlRequest>(empty, RuntimeControlTransport.RequestLimit, CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotRoundTripPreservesCountdownWithoutAdvancingTime()
    {
        await using Fixture fixture = new();
        Success(await fixture.Runtime.CreateSessionAsync(Request("snapshot")));
        TimerSessionSnapshot started = Success(await fixture.Runtime.ExecuteAsync(new SessionCommand.Start("snapshot")));
        TimerSessionSnapshot copy = RuntimeControlJson.Read<TimerSessionSnapshot>(RuntimeControlJson.Value(started));
        Assert.Equal(started.Countdown.TimeLeft, copy.Countdown.TimeLeft);
        Assert.Equal(started.Countdown.RunStartedAt, copy.Countdown.RunStartedAt);
        Assert.Equal(started.Countdown.TimerStart?.ToString(), copy.Countdown.TimerStart?.ToString());
        Assert.Equal(started.Countdown.SupportsRestart, copy.Countdown.SupportsRestart);
    }

    [Fact]
    public async Task PublicEndpointPermissionsAreRejected()
    {
        if (!OperatingSystem.IsLinux()) { return; }
        await using Fixture fixture = new();
        File.SetUnixFileMode(fixture.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
        Assert.IsType<ApplicationResult<RemoteHourglassClient>.Failure>(await RemoteHourglassClient.ConnectAsync(fixture.Path));
    }

    [Fact]
    public async Task DisconnectCancelsOnlyItsOwnForegroundOperation()
    {
        await using Fixture fixture = new();
        Success(await fixture.Runtime.CreateSessionAsync(Request("unrelated")));
        RemoteHourglassClient client = await fixture.ConnectAsync();
        Task<ApplicationResult<ForegroundOutcome>> operation = client.RunForegroundAsync(Request("foreground"));
        await WaitForSessionAsync(fixture.Runtime, "foreground");
        await client.DisposeAsync();
        Assert.IsType<ApplicationResult<ForegroundOutcome>.Failure>(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        // The server drains cancellation and persistence on connection disposal.
        await fixture.Server.DisposeAsync();
        Assert.Equal("unrelated", Assert.Single(Success(await fixture.Runtime.ListSessionsAsync())).SessionId);
    }

    [Fact]
    public async Task ForegroundCancellationLeavesOtherClientSessionUntouched()
    {
        await using Fixture fixture = new();
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        Success(await fixture.Runtime.CreateSessionAsync(Request("unrelated")));
        using CancellationTokenSource cancellation = new();
        Task<ApplicationResult<ForegroundOutcome>> operation = client.RunForegroundAsync(Request("foreground"), cancellation.Token);
        await WaitForSessionAsync(fixture.Runtime, "foreground");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        await client.DisposeAsync();
        await fixture.Server.DisposeAsync();
        Assert.Equal("unrelated", Assert.Single(Success(await fixture.Runtime.ListSessionsAsync())).SessionId);
    }

    [Fact]
    public async Task FactoryConnectsToAuthorityWithoutConstructingAnotherRuntime()
    {
        await using Fixture fixture = new();
        Success(await fixture.Runtime.CreateSessionAsync(Request("existing")));
        int constructed = 0;
        var factory = new ExclusiveRuntimeFactory(() => new ContendedAuthority(), () => { constructed++; return new HourglassRuntime(); }, controlPath: fixture.Path);
        ExclusiveRuntimeLease lease = Success(await factory.OpenAsync(ExclusiveRuntimePurpose.Query, CancellationToken.None));
        Assert.IsType<RemoteHourglassClient>(lease.Client);
        Assert.Single(Success(await lease.Client.ListSessionsAsync()));
        Assert.Equal(0, constructed);
        await lease.DisposeAsync();
        Assert.Single(Success(await fixture.Runtime.ListSessionsAsync()));
    }

    [Fact]
    public async Task MalformedTypedPayloadDoesNotMutateSessions()
    {
        await using Fixture fixture = new();
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(fixture.Path));
        await using NetworkStream stream = new(socket, ownsSocket: false);
        await RuntimeControlTransport.WriteAsync(stream, new ControlRequest(1, "hello", "hello", null, RuntimeControlJson.Value(true)), RuntimeControlTransport.RequestLimit, CancellationToken.None);
        await RuntimeControlTransport.ReadAsync<ControlResponse>(stream, RuntimeControlTransport.ResponseLimit, CancellationToken.None);
        await RuntimeControlTransport.WriteAsync(stream, new ControlRequest(1, "bad", "create", null, RuntimeControlJson.Value("invalid payload")), RuntimeControlTransport.RequestLimit, CancellationToken.None);
        ControlResponse response = await RuntimeControlTransport.ReadAsync<ControlResponse>(stream, RuntimeControlTransport.ResponseLimit, CancellationToken.None) ?? throw new IOException();
        Assert.Equal(ApplicationErrorCode.Validation, response.Error?.Code);
        Assert.Empty(Success(await fixture.Runtime.ListSessionsAsync()));
    }

    [Fact]
    public async Task OrderedSubscriptionBufferFailsVisiblyOnOverflow()
    {
        await using Fixture fixture = new();
        TimerSessionSnapshot snapshot = Success(await fixture.Runtime.CreateSessionAsync(Request("buffer")));
        RuntimeControlServer.RemoteSubscription buffer = new();
        for (int index = 0; index < 257; index++) { await buffer.PublishAsync(new(snapshot, index, CountdownEffects.None)); }
        ControlResponse response = buffer.Poll("poll");
        Assert.False(response.Success);
        Assert.Equal(ApplicationErrorCode.TransportFailure, response.Error?.Code);
    }

    private sealed class ContendedAuthority : ISingleInstanceService
    {
        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task SendLaunchRequestAsync(SingleInstanceLaunchRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task StartRequestListenerAsync(Func<SingleInstanceLaunchRequest, CancellationToken, Task> handler, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public void Dispose() { }
    }

    [Fact]
    public async Task OwnerCompletionDrainsTheStoppingClientsDurabilityCheck()
    {
        await using Fixture fixture = new();
        RemoteHourglassClient client = await fixture.ConnectAsync();
        Task owner = OwnAsync();
        await WaitForSessionAsync(fixture.Runtime, "owner");
        Success(await client.ExecuteAsync(new SessionCommand.Stop("owner")));
        Success(await client.FlushPersistenceAsync());
        Success(await client.ListDiagnosticsAsync());
        await client.DisposeAsync();
        await owner.WaitAsync(TimeSpan.FromSeconds(5));
        async Task OwnAsync()
        {
            Success(await fixture.Runtime.RunForegroundAsync(Request("owner")));
            await fixture.Server.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(1001u, 1000u, 12u)]
    [InlineData(1000u, 1000u, 8u)]
    public void DifferentUserOrMalformedPeerCredentialsAreRejected(uint peer, uint user, uint length)
    {
        Assert.Throws<UnauthorizedAccessException>(() => RuntimeControlTransport.VerifyPeerIdentity(peer, user, length));
    }

    [Fact]
    public async Task SymlinkEndpointIsRejectedWithoutChangingItsTarget()
    {
        await using Fixture fixture = new();
        string alias = fixture.Path + ".alias";
        File.CreateSymbolicLink(alias, fixture.Path);
        Assert.IsType<ApplicationResult<RemoteHourglassClient>.Failure>(await RemoteHourglassClient.ConnectAsync(alias));
        await using RemoteHourglassClient client = await fixture.ConnectAsync();
        Assert.Empty(Success(await client.ListSessionsAsync()));
    }

    [Fact]
    public async Task WireCountdownDoesNotDependOnFrontendCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            await using Fixture fixture = new();
            Success(await fixture.Runtime.CreateSessionAsync(Request("culture") with { TimerInput = "1.5 seconds" }));
            TimerSessionSnapshot started = Success(await fixture.Runtime.ExecuteAsync(new SessionCommand.Start("culture")));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            JsonElement wire = RuntimeControlJson.Value(started);
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            TimerSessionSnapshot copy = RuntimeControlJson.Read<TimerSessionSnapshot>(wire);
            DateTime now = new(2026, 9, 30, 12, 0, 0);
            Assert.True(started.Countdown.TimerStart?.TryGetEndTime(now, out _) == true);
            Assert.Equal(started.Countdown.TimerStart?.GetEndTime(now), copy.Countdown.TimerStart?.GetEndTime(now));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task OwnerShutdownDeliversQueuedRemovalBeforeDisconnectingObservers()
    {
        await using Fixture fixture = new();
        RemoteHourglassClient client = await fixture.ConnectAsync();
        Task owner = OwnAsync();
        await WaitForSessionAsync(fixture.Runtime, "owner");
        TaskCompletionSource removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using SessionSubscription subscription = Success(await client.SubscribeAsync("owner", notification =>
        {
            if (notification.Removed) { removed.TrySetResult(); }
            return Task.CompletedTask;
        }));
        Success(await client.ExecuteAsync(new SessionCommand.Stop("owner")));
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await subscription.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(subscription.Failure);
        await client.DisposeAsync();
        await owner.WaitAsync(TimeSpan.FromSeconds(5));
        async Task OwnAsync()
        {
            Success(await fixture.Runtime.RunForegroundAsync(Request("owner")));
            await fixture.Server.DisposeAsync();
        }
    }

    private static async Task WaitForSessionAsync(HourglassRuntime runtime, string id)
    {
        await ObserveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        async Task ObserveAsync()
        {
            while (!Success(await runtime.ListSessionsAsync()).Any(session => session.SessionId == id)) { await Task.Yield(); }
        }
    }

    private static CreateSessionRequest Request(string id) => new(id, "25m", "Tea", new(AudioAlertsEnabled: false, DoNotKeepComputerAwake: true), new(NotificationsEnabled: false));
    private static T Success<T>(ApplicationResult<T> result) => Assert.IsType<ApplicationResult<T>.Success>(result).Value;
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hg-control-" + Guid.NewGuid().ToString("N"));
        internal HourglassRuntime Runtime { get; } = new(clock: new Clock(), wallClockNow: () => new DateTime(2026, 9, 30, 12, 0, 0));
        internal RuntimeControlServer Server { get; }
        internal string Path => System.IO.Path.Combine(this.directory, "control.sock");
        internal Fixture()
        {
            if (!OperatingSystem.IsLinux()) { throw new PlatformNotSupportedException(); }
            Directory.CreateDirectory(this.directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            this.Server = new(this.Runtime, this.Path);
            this.Server.Start();
        }
        internal async Task<RemoteHourglassClient> ConnectAsync() => Success(await RemoteHourglassClient.ConnectAsync(this.Path));
        public async ValueTask DisposeAsync()
        {
            await this.Server.DisposeAsync();
            await this.Runtime.DisposeAsync();
            Directory.Delete(this.directory, recursive: true);
        }
    }
}
