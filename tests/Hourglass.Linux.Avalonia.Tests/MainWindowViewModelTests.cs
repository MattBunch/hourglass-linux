namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Linux.Avalonia;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void StartWithValidInputRunsTimer()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.False(viewModel.IsInputEnabled);
        Assert.True(viewModel.IsRunning);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.True(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void StartWithInvalidInputKeepsTimerStopped()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerInput = "not a timer";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Enter a valid current timer.", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTime()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 6, 8, 10, 0, 0);
        var viewModel = CreateViewModel(clock, () => now);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal("00:00:07", viewModel.RemainingTime);
        Assert.Equal("Resume", viewModel.PauseResumeText);

        now = now.AddSeconds(8);
        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:00:05", viewModel.RemainingTime);
        Assert.Equal("Pause", viewModel.PauseResumeText);
    }

    [Fact]
    public void ResetReturnsTimerToReadyState()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.True(viewModel.IsInputEnabled);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.False(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void TickTransitionsExpiredTimerToCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsInputEnabled);
    }

    [Fact]
    public void TickTransitionsExpiredTimerShowsNotificationOnce()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var viewModel = CreateViewModel(clock, notificationService: notificationService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal("Hourglass", notificationService.Title);
        Assert.Equal("Timer complete", notificationService.Body);
    }

    [Fact]
    public void NotificationFailureDoesNotPreventCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService { ThrowOnNotify = true };
        var viewModel = CreateViewModel(clock, notificationService: notificationService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
    }

    [Fact]
    public async Task LoadSettingsUsesMostRecentTimerInput()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["15 minutes"], notificationsEnabled: true)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal("15 minutes", viewModel.TimerInput);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task LoadSettingsFailureKeepsDefaultTimerInput()
    {
        var settingsStore = new RecordingSettingsStore { ThrowOnLoad = true };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal("5 minutes", viewModel.TimerInput);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public void StartWithValidInputSavesRecentTimerInput()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(["90 seconds"], settingsStore.SavedSettings.RecentTimerInputs);
    }

    [Fact]
    public async Task ExpiredTimerDoesNotNotifyWhenNotificationsAreDisabled()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["1 second"], notificationsEnabled: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, notificationService.CallCount);
    }

    [Theory]
    [InlineData(TimerState.Stopped, "Ready", "Pause", true, false, "00:00:00")]
    [InlineData(TimerState.Running, "Running", "Pause", false, true, "00:01:05")]
    [InlineData(TimerState.Paused, "Paused", "Resume", false, false, "00:01:05")]
    [InlineData(TimerState.Expired, "Timer complete", "Pause", false, false, "00:00:00")]
    public void TimerViewStateProjectsDomainState(
        TimerState state,
        string statusText,
        string pauseResumeText,
        bool isInputEnabled,
        bool isRunning,
        string remainingTime)
    {
        CountdownState countdownState = CreateCountdownState(state);

        TimerViewState viewState = TimerViewState.FromTimerState("65 seconds", countdownState);

        Assert.Equal(remainingTime, viewState.RemainingTime);
        Assert.Equal(statusText, viewState.StatusText);
        Assert.Equal(pauseResumeText, viewState.PauseResumeText);
        Assert.Equal(isInputEnabled, viewState.IsInputEnabled);
        Assert.Equal(isRunning, viewState.IsRunning);
        Assert.Equal(state, viewState.State);
    }

    private static CountdownState CreateCountdownState(TimerState state)
    {
        DateTime start = new(2026, 6, 8, 10, 0, 0);
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(65),
            start,
            TimeSpan.Zero).State;

        return state switch
        {
            TimerState.Stopped => CountdownState.Stopped,
            TimerState.Running => running,
            TimerState.Paused => CountdownTransitions.Pause(running, TimeSpan.Zero).State,
            TimerState.Expired => CountdownTransitions.Tick(running, TimeSpan.FromSeconds(65)).State,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static MainWindowViewModel CreateViewModel(
        ManualMonotonicClock clock,
        Func<DateTime>? wallClockNow = null,
        INotificationService? notificationService = null,
        ISettingsStore? settingsStore = null)
    {
        return new MainWindowViewModel(
            new CountdownEngine(clock),
            wallClockNow ?? (() => new DateTime(2026, 6, 8, 10, 0, 0)),
            notificationService ?? new RecordingNotificationService(),
            settingsStore ?? new RecordingSettingsStore());
    }

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan elapsed)
        {
            this.Elapsed += elapsed;
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public int CallCount { get; private set; }

        public string? Title { get; private set; }

        public string? Body { get; private set; }

        public bool ThrowOnNotify { get; init; }

        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.Title = title;
            this.Body = body;

            if (this.ThrowOnNotify)
            {
                return Task.FromException(new InvalidOperationException("Notification failed."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        public LinuxAppSettings? LoadedSettings { get; init; }

        public LinuxAppSettings? SavedSettings { get; private set; }

        public bool ThrowOnLoad { get; init; }

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (this.ThrowOnLoad)
            {
                return Task.FromException<T?>(new InvalidOperationException("Settings failed."));
            }

            return Task.FromResult((T?)(object?)this.LoadedSettings);
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            this.SavedSettings = Assert.IsType<LinuxAppSettings>(value);
            return Task.CompletedTask;
        }
    }
}
