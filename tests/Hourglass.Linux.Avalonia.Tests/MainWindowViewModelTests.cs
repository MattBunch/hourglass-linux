namespace Hourglass.Linux.Avalonia.Tests;

using System.Globalization;
using System.Xml.Linq;
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
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:02:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsInputEnabled);
        Assert.True(viewModel.IsRunning);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.True(viewModel.ResetCommand.CanExecute(null));
        Assert.Equal(1, sessionInhibitor.AcquireCount);
        Assert.Equal(0, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void StartWithAbsoluteTimeInputRunsTimer()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 6, 8, 12, 30, 0);
        var viewModel = CreateViewModel(clock, () => now);

        viewModel.TimerInput = "1pm";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:30:00", viewModel.RemainingTime);
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
    public void StartCommandDoesNotRestartRunningTimer()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        viewModel.TimerInput = "5 minutes";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.Equal(1, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void RelayCommandDoesNotExecuteWhenCanExecuteIsFalse()
    {
        int callCount = 0;
        var command = new RelayCommand(() => callCount++, () => false);

        command.Execute(null);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void TimerInputEnterKeyBindingsUseStartCommand()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement timerInput = Assert.Single(
            document.Descendants(avalonia + "TextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerInputTextBox");
        string[] gestures = timerInput
            .Element(avalonia + "TextBox.KeyBindings")?
            .Elements(avalonia + "KeyBinding")
            .Where(element => element.Attribute("Command")?.Value == "{Binding StartCommand}")
            .Select(element => element.Attribute("Gesture")?.Value)
            .OfType<string>()
            .Order()
            .ToArray() ?? [];

        Assert.Equal(["Enter", "Return"], gestures);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTime()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        DateTime now = new(2026, 6, 8, 10, 0, 0);
        var viewModel = CreateViewModel(clock, () => now, sessionInhibitor: sessionInhibitor);

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
        Assert.Equal(1, sessionInhibitor.ReleaseCount);

        now = now.AddSeconds(8);
        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:00:05", viewModel.RemainingTime);
        Assert.Equal("Pause", viewModel.PauseResumeText);
        Assert.Equal(2, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void ResetReturnsTimerToReadyState()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

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
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void TickTransitionsExpiredTimerToCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsInputEnabled);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void InhibitionFailureDoesNotPreventTimerStart()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor { ThrowOnAcquire = true };
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal(1, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void DisposeReleasesActiveInhibition()
    {
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);
        viewModel.Dispose();

        Assert.Equal(1, sessionInhibitor.ReleaseCount);
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
    public void TickTransitionsExpiredTimerPlaysAudioOnce()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(clock, audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.NormalBeep, audioAlertService.SoundId);
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
    public void AudioFailureDoesNotPreventCompleteDisplayOrNotification()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService { ThrowOnPlay = true };
        var notificationService = new RecordingNotificationService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
    }

    [Fact]
    public void NotificationFailureDoesNotPreventAudio()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService { ThrowOnNotify = true };
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.NormalBeep, audioAlertService.SoundId);
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
    public async Task LoadSettingsResumesOnCapturedSchedulerBeforePublishingState()
    {
        var scheduler = new QueuedTaskScheduler();
        var taskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            scheduler);
        var settingsStore = new DeferredSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        Task<Task> scheduledLoadTask = taskFactory.StartNew(() =>
        {
            SynchronizationContext? originalContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                return viewModel.LoadSettingsAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }
        });
        scheduler.RunNext();
        Task loadTask = await scheduledLoadTask;

        settingsStore.Complete(new LinuxAppSettings(["15 minutes"], notificationsEnabled: true));

        Assert.False(loadTask.IsCompleted);
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal("5 minutes", viewModel.TimerInput);

        scheduler.RunNext();
        await loadTask;

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
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["1 second"], notificationsEnabled: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
    }

    [Fact]
    public async Task ExpiredTimerDoesNotPlayAudioWhenAudioAlertsAreDisabled()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(
                ["1 second"],
                notificationsEnabled: true,
                audioAlertsEnabled: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(0, audioAlertService.CallCount);
    }

    [Theory]
    [InlineData(TimerState.Stopped, "Ready", "Pause", true, false, "00:00:00", 0, true, false, false, true, false, false, false)]
    [InlineData(TimerState.Running, "Running", "Pause", false, true, "00:01:05", 0, false, true, false, false, true, false, true)]
    [InlineData(TimerState.Paused, "Paused", "Resume", false, false, "00:01:05", 0, false, true, false, false, false, true, true)]
    [InlineData(TimerState.Expired, "Timer complete", "Pause", false, false, "00:00:00", 100, false, false, true, false, false, false, true)]
    public void TimerViewStateProjectsDomainState(
        TimerState state,
        string statusText,
        string pauseResumeText,
        bool isInputEnabled,
        bool isRunning,
        string remainingTime,
        double progressPercent,
        bool isTimerInputVisible,
        bool isRemainingTimeVisible,
        bool isCompletionTextVisible,
        bool isStartVisible,
        bool isPauseVisible,
        bool isResumeVisible,
        bool isStopVisible)
    {
        CountdownState countdownState = CreateCountdownState(state);

        TimerViewState viewState = TimerViewState.FromTimerState("65 seconds", countdownState);

        Assert.Equal(remainingTime, viewState.RemainingTime);
        Assert.Equal(statusText, viewState.StatusText);
        Assert.Equal(pauseResumeText, viewState.PauseResumeText);
        Assert.Equal(isInputEnabled, viewState.IsInputEnabled);
        Assert.Equal(isRunning, viewState.IsRunning);
        Assert.Equal(state, viewState.State);
        Assert.Equal(progressPercent, viewState.ProgressPercent);
        Assert.Equal(isTimerInputVisible, viewState.IsTimerInputVisible);
        Assert.Equal(isRemainingTimeVisible, viewState.IsRemainingTimeVisible);
        Assert.Equal(isCompletionTextVisible, viewState.IsCompletionTextVisible);
        Assert.Equal(isStartVisible, viewState.IsStartVisible);
        Assert.Equal(isPauseVisible, viewState.IsPauseVisible);
        Assert.Equal(isResumeVisible, viewState.IsResumeVisible);
        Assert.Equal(isStopVisible, viewState.IsStopVisible);
    }

    [Fact]
    public void TimerViewStateCalculatesRunningProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState halfway = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(5)).State;

        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", halfway);

        Assert.Equal(50, viewState.ProgressPercent);
    }

    [Fact]
    public void TimerViewStateKeepsPausedProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState halfway = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(5)).State;
        CountdownState paused = CountdownTransitions.Pause(halfway, TimeSpan.FromSeconds(5)).State;

        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", paused);

        Assert.Equal(50, viewState.ProgressPercent);
    }

    [Fact]
    public void TimerViewStateClampsProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(60)).State;

        Assert.Equal(100, TimerViewState.GetProgressPercent(expired));
        Assert.Equal(0, TimerViewState.GetProgressPercent(CountdownState.Stopped));
        Assert.InRange(TimerViewState.GetProgressPercent(running), 0, 100);
    }

    [Fact]
    public void TimerViewStateProgressHandlesZeroDuration()
    {
        CountdownState zeroDuration = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.Zero,
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;

        double progress = TimerViewState.GetProgressPercent(zeroDuration);

        Assert.False(double.IsNaN(progress));
        Assert.False(double.IsInfinity(progress));
        Assert.Equal(100, progress);
    }

    [Fact]
    public void ProgressWidthConverterUsesClampedProgressAndAvailableWidth()
    {
        var converter = new ProgressWidthConverter();

        Assert.Equal(175d, converter.Convert([50d, 350d], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(350d, converter.Convert([150d, 350d], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0d, converter.Convert([double.NaN, 350d], typeof(double), null, CultureInfo.InvariantCulture));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            string path = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
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
        ISessionInhibitor? sessionInhibitor = null,
        ISettingsStore? settingsStore = null,
        IAudioAlertService? audioAlertService = null)
    {
        return new MainWindowViewModel(
            new CountdownEngine(clock),
            wallClockNow ?? (() => new DateTime(2026, 6, 8, 10, 0, 0)),
            notificationService ?? new RecordingNotificationService(),
            sessionInhibitor ?? new RecordingSessionInhibitor(),
            settingsStore ?? new RecordingSettingsStore(),
            audioAlertService ?? new RecordingAudioAlertService());
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

    private sealed class RecordingAudioAlertService : IAudioAlertService
    {
        public int CallCount { get; private set; }

        public string? SoundId { get; private set; }

        public bool ThrowOnPlay { get; init; }

        public Task PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.SoundId = soundId;

            if (this.ThrowOnPlay)
            {
                return Task.FromException(new InvalidOperationException("Audio failed."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionInhibitor : ISessionInhibitor
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public bool ThrowOnAcquire { get; init; }

        public ValueTask<IAsyncDisposable?> InhibitAsync(
            string reason,
            bool inhibitSuspend,
            bool inhibitIdle,
            CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;

            if (this.ThrowOnAcquire)
            {
                throw new InvalidOperationException("Inhibition failed.");
            }

            Assert.Equal("Hourglass timer is running", reason);
            Assert.True(inhibitSuspend);
            Assert.True(inhibitIdle);

            return ValueTask.FromResult<IAsyncDisposable?>(new RecordingInhibitionLease(this));
        }

        private sealed class RecordingInhibitionLease(RecordingSessionInhibitor owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.ReleaseCount++;
                return ValueTask.CompletedTask;
            }
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

    private sealed class DeferredSettingsStore : ISettingsStore
    {
        private readonly TaskCompletionSource<LinuxAppSettings?> loadTask = new();

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            Assert.Equal("app", key);
            Assert.Equal(typeof(LinuxAppSettings), typeof(T));

            return (Task<T?>)(object)this.loadTask.Task;
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Complete(LinuxAppSettings settings)
        {
            this.loadTask.SetResult(settings);
        }
    }

    private sealed class QueuedTaskScheduler : TaskScheduler
    {
        private readonly Queue<Task> tasks = [];

        public int PendingCount => this.tasks.Count;

        protected override IEnumerable<Task>? GetScheduledTasks()
        {
            return this.tasks.ToArray();
        }

        protected override void QueueTask(Task task)
        {
            this.tasks.Enqueue(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        public void RunNext()
        {
            this.TryExecuteTask(this.tasks.Dequeue());
        }
    }
}
