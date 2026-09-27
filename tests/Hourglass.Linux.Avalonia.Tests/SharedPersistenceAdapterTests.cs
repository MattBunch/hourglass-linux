namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class SharedPersistenceAdapterTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0);

    [Fact]
    public async Task AttachingViewPreservesExistingSessionAndRestoredEditorDraft()
    {
        var clock = new Clock();
        var store = new Store();
        await using var runtime = new HourglassRuntime(clock: clock, wallClockNow: () => Now, settingsStore: store);
        Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.CreateSessionAsync(new("existing", "5m", "Tea", new(LoopTimer: true), new())));
        Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.ExecuteAsync(new SessionCommand.Start("existing")));
        TimerSessionSnapshot before = Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("existing")).Value;
        await using var viewModel = ViewModel(runtime, clock, store);
        await viewModel.LoadSettingsAsync();
        await viewModel.PendingCommands;
        ActiveTimerSessionDocument document = ActiveTimerSessionSnapshot.FromState("invalid draft", "Tea", ActiveTimerPresentationMode.Input,
            before.Countdown, Now, SavedTimerOptions.FromSettings(LinuxAppSettings.Default with { LoopTimer = true })).ToDocument();
        Assert.True(viewModel.RestoreActiveSession(document));
        await viewModel.PendingCommands;
        Assert.Equal("invalid draft", viewModel.TimerInput);
        Assert.True(viewModel.LoopTimer);
        Assert.Equal(before.Countdown, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("existing")).Value.Countdown);
        Assert.Single(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
    }

    [Fact]
    public async Task AttachingRestoredExpiredViewPublishesAttentionWithoutRepeatingApplicationEffects()
    {
        var clock = new Clock();
        var store = new Store();
        await using var runtime = new HourglassRuntime(clock: clock, wallClockNow: () => Now, settingsStore: store);
        await runtime.CreateSessionAsync(new("existing", "1s", "Tea", new(), new(PopUpWhenExpired: true)));
        CountdownState expired = CountdownTransitions.Tick(CountdownTransitions.StartDuration(CountdownState.Stopped,
            TimeSpan.FromSeconds(1), Now, TimeSpan.Zero).State, TimeSpan.FromSeconds(2)).State;
        ActiveTimerSessionSnapshot restored = ActiveTimerSessionSnapshot.FromState("1s", "Tea", ActiveTimerPresentationMode.Status, expired, Now);
        await runtime.RestoreSessionAsync("existing", restored, new(PopUpWhenExpired: true));
        await using var viewModel = ViewModel(runtime, clock, store);
        await viewModel.LoadSettingsAsync();
        int attention = 0;
        int visual = 0;
        viewModel.WindowAttentionRequested += (_, _) => attention++;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => visual++;
        Assert.True(viewModel.RestoreActiveSession(restored.ToDocument(), restoredExpiry: true));
        await viewModel.PendingCommands;
        Assert.Equal(1, attention);
        Assert.Equal(1, visual);
    }

    private static MainWindowViewModel ViewModel(HourglassRuntime runtime, Clock clock, Store store) => new(
        clock, () => Now, UnsupportedNotificationService.Instance, UnsupportedSessionInhibitor.Instance,
        store, UnsupportedAudioAlertService.Instance, UnsupportedSystemPowerService.Instance,
        sessionId: "existing", persistActiveSessionDirectly: false, restoreActiveSessionOnLoad: false, runtime: runtime);

    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Store : ISettingsStore
    {
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
