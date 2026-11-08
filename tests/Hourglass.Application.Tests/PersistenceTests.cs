namespace Hourglass.Application.Tests;

using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class PersistenceTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0);

    [Fact]
    public async Task PersistenceCommandsReportUnavailableAfterShutdown()
    {
        var runtime = Runtime(new Store());
        await runtime.DisposeAsync();
        Assert.Equal(ApplicationErrorCode.RuntimeUnavailable,
            Assert.IsType<ApplicationResult<bool>.Failure>(await runtime.FlushPersistenceAsync()).Error.Code);
        Assert.Equal(ApplicationErrorCode.RuntimeUnavailable,
            Assert.IsType<ApplicationResult<LinuxAppSettings>.Failure>(await runtime.GetSettingsAsync()).Error.Code);
    }

    [Fact]
    public async Task LoadingDataDoesNotRestoreSessionsOrWriteDocuments()
    {
        var store = new Store();
        store.Documents["active-session"] = Session("25m");
        await using var runtime = Runtime(store);
        await runtime.LoadApplicationDataAsync();
        Assert.Empty(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
        Assert.Empty(store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyCollectionSuppressesLegacyFallback(bool emptyCollection)
    {
        var store = new Store();
        store.Documents["active-session"] = Session("25m");
        if (emptyCollection) { store.Documents["active-sessions"] = ActiveTimerSessionsDocument.Empty; }
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        RestoredSession restored = Assert.Single(started.Sessions);
        Assert.Equal(emptyCollection ? TimerState.Stopped : TimerState.Running, restored.Session.CountdownState.State);
    }

    [Fact]
    public async Task RestoredSessionsPreserveIdsDraftsAndOptionsWithoutWindows()
    {
        var store = new Store();
        var original = Assert.IsType<ActiveTimerSessionSnapshot>(ActiveTimerSessionSnapshot.FromDocument(Session("25m"), Now, TimeSpan.Zero)) with { TimerInput = "invalid draft" };
        store.Documents["active-sessions"] = new ActiveTimerSessionsDocument(sessions: [new("stable", original.ToDocument())]);
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        Assert.Equal("stable", Assert.Single(started.Sessions).SessionId);
        Assert.Equal("invalid draft", started.Sessions[0].Session.TimerInput);
        Assert.Equal(TimerStart.FromString("25m")?.ToString(), started.Sessions[0].Session.TimerStartInput);
        await runtime.FlushPersistenceAsync();
        var written = Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]);
        Assert.Equal("invalid draft", Assert.Single(written.Sessions).Session!.TimerInput);
    }

    [Fact]
    public async Task InitializationIsSingleFlightAndExplicitLaunchAddsToRestoration()
    {
        var store = new Store();
        store.Documents["active-sessions"] = new ActiveTimerSessionsDocument(sessions: [new("existing", Session("25m"))]);
        await using var runtime = Runtime(store);
        Task<ApplicationResult<RuntimeStartupSnapshot>> first = runtime.InitializeSessionsAsync("5m", "Tea");
        await Task.WhenAll(first, runtime.InitializeSessionsAsync("5m", "Tea"));
        var result = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await first).Value;
        Assert.Equal(2, result.Sessions.Length);
        Assert.Contains(result.Sessions, item => item.Session.TimerTitle == "Tea");
        await runtime.FlushPersistenceAsync();
        Assert.All(store.Writes.Where(item => item.Key == "active-sessions"), item => Assert.Equal(2, ((ActiveTimerSessionsDocument)item.Value).Sessions.Length));
    }

    [Fact]
    public async Task RemovingLastSessionPersistsEmptyCollectionAndIsIdempotent()
    {
        var store = new Store();
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        string id = Assert.Single(started.Sessions).SessionId;
        await runtime.RemoveAsync(id);
        await runtime.RemoveAsync(id);
        Assert.IsType<ApplicationResult<bool>.Success>(await runtime.FlushPersistenceAsync());
        Assert.Empty(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions);
    }

    [Fact]
    public async Task FailureRemainsVisibleAfterUnrelatedSaveAndRecoversAfterRetry()
    {
        var store = new Store { FailKey = "app" };
        var data = new ApplicationData(store);
        await Assert.ThrowsAsync<IOException>(() => data.SaveAsync("app", LinuxAppSettings.Default));
        await data.SaveAsync("custom-themes", CustomThemesDocument.Empty);
        Assert.IsType<ApplicationResult<bool>.Failure>(await data.FlushAsync());
        store.FailKey = null;
        await data.SaveAsync("app", LinuxAppSettings.Default);
        Assert.IsType<ApplicationResult<bool>.Success>(await data.FlushAsync());
    }

    [Fact]
    public async Task InvalidRestoredCollectionFallsBackToSavedTemplatesWithoutStartingThem()
    {
        var store = new Store();
        LinuxAppSettings settings = LinuxAppSettings.Default with { OpenSavedTimersOnStartup = true };
        store.Documents["app"] = settings;
        store.Documents["active-sessions"] = new ActiveTimerSessionsDocument(sessions:
            [new("invalid", new ActiveTimerSessionDocument(state: TimerState.Running, timerInput: "bad"))]);
        store.Documents["saved-timers"] = new SavedTimersDocument(timers:
            [new("saved", "5m", "Tea", options: SavedTimerOptions.FromSettings(settings with { LoopTimer = true }))]);
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        RestoredSession template = Assert.Single(started.Sessions);
        Assert.Equal("Tea", template.Session.TimerTitle);
        Assert.Equal(TimerState.Stopped, template.Session.CountdownState.State);
        Assert.True(template.Session.Options.LoopTimer);
        Assert.True(Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(await runtime.GetSettingsAsync()).Value.LoopTimer);
    }

    [Fact]
    public async Task CorruptCollectionRecoversThroughLegacyDocument()
    {
        var store = new Store();
        store.ThrowLoads.Add("active-sessions");
        store.Documents["active-session"] = Session("25m");
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        Assert.Equal(TimerState.Running, Assert.Single(started.Sessions).Session.CountdownState.State);
    }

    [Fact]
    public async Task CancelingOneStartupWaiterDoesNotCancelSharedInitializationOrWritePartialState()
    {
        var store = new Store { HeldLoadKey = "app" };
        await using var runtime = Runtime(store);
        using var cancellation = new CancellationTokenSource();
        Task<ApplicationResult<RuntimeStartupSnapshot>> first = runtime.InitializeSessionsAsync("5m", cancellationToken: cancellation.Token);
        await store.LoadEntered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Empty(store.Writes);
        Task<ApplicationResult<RuntimeStartupSnapshot>> second = runtime.InitializeSessionsAsync();
        store.ReleaseLoad.SetResult();
        Assert.Single(Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await second).Value.Sessions);
    }

    [Fact]
    public async Task SlowStorageDoesNotBlockPauseAndQueuedSnapshotsRemainOrdered()
    {
        var store = new Store { HeldSaveKey = "active-sessions" };
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync("5m")).Value;
        string id = Assert.Single(started.Sessions).SessionId;
        await store.SaveEntered.Task;
        Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.ExecuteAsync(new SessionCommand.Pause(id)));
        Task<ApplicationResult<bool>> flush = runtime.FlushPersistenceAsync();
        Assert.False(flush.IsCompleted);
        store.ReleaseSave.SetResult();
        Assert.IsType<ApplicationResult<bool>.Success>(await flush);
        Assert.Equal(TimerState.Paused, Assert.Single(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions).Session!.State);
    }

    [Fact]
    public async Task FailedCheckpointKeepsTimerRunningAndDisposalPreservesItsPersistedState()
    {
        var store = new Store { FailKey = "active-sessions" };
        var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync("5m")).Value;
        string id = Assert.Single(started.Sessions).SessionId;
        Assert.IsType<ApplicationResult<bool>.Failure>(await runtime.FlushPersistenceAsync());
        Assert.Equal(TimerState.Running, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync(id)).Value.Countdown.State);
        store.FailKey = null;
        await runtime.UpdatePresentationAsync(id, new("bad draft", ActiveTimerPresentationMode.Input, new(10, 20, 300, 200)));
        Assert.IsType<ApplicationResult<bool>.Success>(await runtime.FlushPersistenceAsync());
        await runtime.DisposeAsync();
        var document = Assert.Single(Assert.IsType<ActiveTimerSessionsDocument>(store.Documents["active-sessions"]).Sessions).Session!;
        Assert.Equal(TimerState.Running, document.State);
        Assert.Equal("bad draft", document.TimerInput);
        Assert.Equal(300, document.WindowGeometry?.Width);
    }

    [Fact]
    public async Task SharedCatalogCommandsPreserveConcurrentAdditionsAndRecentInputs()
    {
        var store = new Store();
        await using var runtime = Runtime(store);
        await Task.WhenAll(runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("a", "5m"))),
            runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("b", "10m"))));
        Assert.Equal(2, Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success>(await runtime.ListSavedTimersAsync()).Value.Length);
        await runtime.InitializeSessionsAsync("5m");
        Assert.Equal(new[] { "5m" }, Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Success>(await runtime.ListRecentInputsAsync()).Value.ToArray());
        await runtime.ClearRecentInputsAsync();
        Assert.Empty(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Success>(await runtime.ListRecentInputsAsync()).Value);
        await Task.WhenAll(runtime.ChangeThemesAsync(CustomThemesDocument.Empty, new(themes: [new("a", "A")])),
            runtime.ChangeThemesAsync(CustomThemesDocument.Empty, new(themes: [new("b", "B")])));
        Assert.Equal(2, Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<CustomThemeDefinition>>.Success>(await runtime.ListThemesAsync()).Value.Length);
    }

    [Fact]
    public async Task RestoredExpiryRetainsCompletionFactAndUnlockedOptions()
    {
        var store = new Store();
        store.Documents["active-sessions"] = new ActiveTimerSessionsDocument(sessions:
            [new("expired", ActiveTimerSessionSnapshot.FromState("1s", "", ActiveTimerPresentationMode.Status,
                CountdownTransitions.StartDuration(CountdownState.Stopped, TimeSpan.FromSeconds(1), Now.AddSeconds(-2), TimeSpan.Zero).State,
                Now.AddSeconds(-2)).ToDocument())]);
        await using var runtime = Runtime(store);
        var started = Assert.IsType<ApplicationResult<RuntimeStartupSnapshot>.Success>(await runtime.InitializeSessionsAsync()).Value;
        Assert.True(Assert.Single(started.Sessions).ExpiredWhileClosed);
        Assert.Equal(TimerState.Expired, started.Sessions[0].Session.CountdownState.State);
    }

    [Fact]
    public async Task InvalidSavedMutationsReturnTypedFailuresWithoutWriting()
    {
        var store = new Store();
        await using var runtime = Runtime(store);
        var invalid = Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure>(
            await runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("bad", "invalid"))));
        Assert.Equal(ApplicationErrorCode.Validation, invalid.Error.Code);
        var missing = Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure>(
            await runtime.ChangeSavedTimersAsync(new SavedTimerChange.Remove("missing")));
        Assert.Equal(ApplicationErrorCode.NotFound, missing.Error.Code);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task ChangingDefaultsDoesNotChangeExistingSessions()
    {
        var store = new Store();
        await using var runtime = Runtime(store);
        await runtime.LoadApplicationDataAsync();
        await runtime.CreateSessionAsync(new("existing", "5m", "", new(LoopTimer: false), new()));
        LinuxAppSettings previous = Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(await runtime.GetSettingsAsync()).Value;
        await runtime.ChangeSettingsAsync(previous, previous with { LoopTimer = true });
        Assert.False(Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("existing")).Value.Options.LoopTimer);
        var created = Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.CreateSavedSessionAsync(new("saved", "10m")));
        Assert.Equal(TimerState.Running, created.Value.Countdown.State);
    }

    private static HourglassRuntime Runtime(Store store) => new(clock: new Clock(), wallClockNow: () => Now, settingsStore: store);
    private static ActiveTimerSessionDocument Session(string input) => ActiveTimerSessionSnapshot.FromState(input, "", ActiveTimerPresentationMode.Status,
        CountdownTransitions.Start(CountdownState.Stopped, TimerStart.FromString(input), Now, TimeSpan.Zero).State, Now).ToDocument();
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Store : ISettingsStore
    {
        public Dictionary<string, object> Documents { get; } = new();
        public List<(string Key, object Value)> Writes { get; } = [];
        public string? FailKey { get; set; }
        public string? HeldLoadKey { get; init; }
        public string? HeldSaveKey { get; init; }
        public HashSet<string> ThrowLoads { get; } = [];
        public TaskCompletionSource LoadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (this.ThrowLoads.Contains(key)) { throw new InvalidDataException("Intentional corrupt document."); }
            if (key == this.HeldLoadKey) { this.LoadEntered.TrySetResult(); await this.ReleaseLoad.Task.WaitAsync(cancellationToken); }
            lock (this.Documents) { return this.Documents.TryGetValue(key, out object? value) ? (T)value : default; }
        }
        public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (key == this.HeldSaveKey) { this.SaveEntered.TrySetResult(); await this.ReleaseSave.Task.WaitAsync(cancellationToken); }
            if (key == this.FailKey) { throw new IOException("Intentional save failure."); }
            lock (this.Documents)
            {
                if (value != null) { this.Documents[key] = value; this.Writes.Add((key, value)); }
            }
        }
    }
}
