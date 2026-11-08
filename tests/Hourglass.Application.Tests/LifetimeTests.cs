namespace Hourglass.Application.Tests;

using Hourglass.Settings;
using Hourglass.Timing;
using Hourglass.Platform;
using Xunit;

public sealed class LifetimeTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0);
    [Fact]
    public async Task DetachingPreservesCountdownAndDisconnectClosesOnlyOwnedSessions()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.InitializeHostAsync();
        await using RuntimeClientLease tui = await runtime.OpenClientAsync(RuntimeClientKind.Tui);
        TimerSessionSnapshot first = Success(await tui.Client.CreateSessionAsync(Request("first")));
        Success(await tui.Client.CreateSessionAsync(Request("second")));
        first = Success(await tui.Client.ExecuteAsync(new SessionCommand.Start("first")));
        TimerSessionSnapshot detached = Success(await tui.Client.DetachSessionAsync("first", first.Revision));
        Assert.Same(first.Countdown, detached.Countdown);
        Assert.Equal(SessionLifetime.Detached, detached.Lifetime);
        await tui.DisposeAsync();
        Assert.Equal("first", Assert.Single(Success(await runtime.ListSessionsAsync())).SessionId);
        Assert.True(await runtime.RequiresAuthorityAsync());
    }
    [Fact]
    public async Task GuiDisconnectPreservesRecoveryAndReinitializesOnlyOnce()
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        await runtime.InitializeHostAsync();
        await using RuntimeClientLease gui = await runtime.OpenClientAsync(RuntimeClientKind.Gui);
        Success(await gui.Client.InitializeGuiAsync());
        TimerSessionSnapshot session = Assert.Single(Success(await runtime.ListSessionsAsync()));
        Success(await gui.Client.ExecuteAsync(new SessionCommand.Start(session.SessionId, "5m")));
        await gui.DisposeAsync();
        Assert.Empty(Success(await runtime.ListSessionsAsync()));
        Assert.Single(store.Sessions.Sessions);
        await using RuntimeClientLease reopened = await runtime.OpenClientAsync(RuntimeClientKind.Gui);
        Success(await reopened.Client.InitializeGuiAsync());
        Success(await reopened.Client.InitializeGuiAsync());
        Assert.Equal(session.SessionId, Assert.Single(Success(await runtime.ListSessionsAsync())).SessionId);
    }
    [Fact]
    public async Task HostRestoresDetachedAndKeepsLegacyGuiRecordAcrossWrites()
    {
        Store store = new();
        await using (HourglassRuntime seed = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store))
        {
            await seed.InitializeHostAsync();
            Success(await seed.StartDetachedAsync(Request("background")));
            Success(await seed.CreateSessionAsync(Request("gui")));
            await seed.FlushPersistenceAsync();
        }
        await using HourglassRuntime host = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        Success(await host.InitializeHostAsync());
        Assert.Equal("background", Assert.Single(Success(await host.ListSessionsAsync())).SessionId);
        Success(await host.StartDetachedAsync(Request("another")));
        Assert.Equal(3, store.Sessions.Sessions.Length);
        Assert.Contains(store.Sessions.Sessions, item => item.SessionId == "gui");
    }
    [Fact]
    public async Task DetachRejectsStaleRevisionAndLockedTimer()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        TimerSessionSnapshot session = Success(await runtime.CreateSessionAsync(Request("locked") with { Options = new(LockInterface: true) }));
        session = Success(await runtime.ExecuteAsync(new SessionCommand.Start("locked")));
        Assert.Equal(ApplicationErrorCode.Conflict, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await runtime.DetachSessionAsync("locked", session.Revision - 1)).Error.Code);
        Assert.Equal(ApplicationErrorCode.Locked, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await runtime.DetachSessionAsync("locked", session.Revision)).Error.Code);
    }
    [Fact]
    public async Task ExpiredDetachedSessionRetainsAuthorityUntilDismissed()
    {
        Clock clock = new();
        await using HourglassRuntime runtime = new(clock: clock, wallClockNow: () => Now + clock.Elapsed);
        await runtime.InitializeHostAsync();
        Success(await runtime.StartDetachedAsync(Request("expired")));
        clock.Elapsed = TimeSpan.FromMinutes(5);
        await runtime.TickAsync();
        await runtime.WaitForSessionEffectsAsync("expired");
        TimerSessionSnapshot session = Success(await runtime.GetSessionAsync("expired"));
        Assert.Equal(TimerState.Expired, session.Countdown.State);
        Assert.True(await runtime.RequiresAuthorityAsync());
        Success(await runtime.ExecuteAsync(new SessionCommand.Dismiss("expired")));
        Assert.True(await runtime.TryBeginIdleShutdownAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.OpenClientAsync(RuntimeClientKind.Control));
        Assert.Equal(ApplicationErrorCode.RuntimeUnavailable, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await runtime.CreateSessionAsync(Request("late"))).Error.Code);
    }
    [Fact]
    public async Task PresentationCloseCannotRemoveDetachedOrAnotherClientsSession()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.InitializeHostAsync();
        await using RuntimeClientLease first = await runtime.OpenClientAsync(RuntimeClientKind.Tui);
        await using RuntimeClientLease second = await runtime.OpenClientAsync(RuntimeClientKind.Gui);
        TimerSessionSnapshot session = Success(await first.Client.CreateSessionAsync(Request("own")));
        Success(await second.Client.CloseSessionAsync("own"));
        Assert.Single(Success(await runtime.ListSessionsAsync()));
        Success(await first.Client.DetachSessionAsync("own", session.Revision));
        Success(await first.Client.CloseSessionAsync("own"));
        Assert.Equal(SessionLifetime.Detached, Assert.Single(Success(await runtime.ListSessionsAsync())).Lifetime);
    }
    [Fact]
    public async Task HostSkipsTransientRecoveryAndHonorsDisabledRestoration()
    {
        Store store = new();
        ActiveTimerSessionDocument document = new(timerInput: "5m");
        store.Seed("app", LinuxAppSettings.Default with { RestoreActiveSessionOnStartup = false });
        store.Seed("active-sessions", new ActiveTimerSessionsDocument(sessions:
            [new("gui", document), new("detached", document, SessionLifetime.Detached),
             new("tui", document, SessionLifetime.Tui), new("foreground", document, SessionLifetime.Foreground)]));
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        Success(await runtime.InitializeHostAsync());
        await runtime.FlushPersistenceAsync();
        Assert.Empty(Success(await runtime.ListSessionsAsync()));
        Assert.Equal("gui", Assert.Single(store.Sessions.Sessions).SessionId);
        await using RuntimeClientLease gui = await runtime.OpenClientAsync(RuntimeClientKind.Gui);
        Success(await gui.Client.InitializeGuiAsync());
        Assert.NotEqual("gui", Assert.Single(Success(await runtime.ListSessionsAsync())).SessionId);
    }
    [Fact]
    public async Task DetachedPersistenceFailureKeepsIdentifiableLiveSession()
    {
        Store store = new();
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
        Success(await runtime.InitializeHostAsync());
        await runtime.FlushPersistenceAsync();
        store.FailSave = true;
        var failure = Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Failure>(await runtime.StartDetachedAsync(Request("inspect")));
        Assert.Equal(ApplicationErrorCode.PersistenceFailure, failure.Error.Code);
        Assert.Contains("inspect", failure.Error.Message);
        Assert.Equal(SessionLifetime.Detached, Success(await runtime.GetSessionAsync("inspect")).Lifetime);
        store.FailSave = false;
        Success(await runtime.ClearRecentInputsAsync());
        Success(await runtime.ExecuteAsync(new SessionCommand.Stop("inspect")));
        Success(await runtime.FlushPersistenceAsync());
    }
    [Fact]
    public void LifetimeEnvelopePreservesLegacyDefaultAndSnapshotRoundTrip()
    {
        const string legacy = "{\"sessions\":[{\"sessionId\":\"legacy\",\"session\":{\"timerInput\":\"5m\"}}]}";
        ActiveTimerSessionsDocument old = System.Text.Json.JsonSerializer.Deserialize<ActiveTimerSessionsDocument>(legacy, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? throw new InvalidDataException();
        Assert.Equal(SessionLifetime.Gui, Assert.Single(old.Sessions).Lifetime);
        ActiveTimerSessionsDocument detached = new(sessions: [new("durable", new(timerInput: "5m"), SessionLifetime.Detached)]);
        ActiveTimerSessionsSnapshot snapshot = ActiveTimerSessionsSnapshot.FromDocument(detached, Now, TimeSpan.Zero);
        Assert.Equal(SessionLifetime.Detached, Assert.Single(snapshot.ToDocument().Sessions).Lifetime);
    }

    private static CreateSessionRequest Request(string id) => new(id, "5m", "Tea", new(), new());
    private static T Success<T>(ApplicationResult<T> result) => Assert.IsType<ApplicationResult<T>.Success>(result).Value;
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed { get; set; } }
    private sealed class Store : ISettingsStore
    {
        private readonly Dictionary<string, object> documents = [];
        public bool FailSave { get; set; }
        public void Seed(string key, object document) => this.documents[key] = document;
        public ActiveTimerSessionsDocument Sessions => (ActiveTimerSessionsDocument)this.documents["active-sessions"];
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(this.documents.TryGetValue(key, out object? item) ? (T?)item : default);
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default) { if (this.FailSave) { throw new IOException("Intentional checkpoint failure."); } if (value != null) { this.documents[key] = value; } return Task.CompletedTask; }
    }
}
