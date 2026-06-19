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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTimerTitleUsesApplicationWindowTitle(string? timerTitle)
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerTitle = timerTitle;

        Assert.Equal(timerTitle ?? string.Empty, viewModel.TimerTitle);
        Assert.Equal("Hourglass", viewModel.WindowTitle);
    }

    [Fact]
    public void ChangingTimerTitleImmediatelyUpdatesWindowTitleAndRaisesNotifications()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TimerTitle = "Tea";

        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal("Tea", viewModel.WindowTitle);
        Assert.Equal([nameof(viewModel.TimerTitle), nameof(viewModel.WindowTitle)], changedProperties);
    }

    [Fact]
    public void WhitespaceTimerTitlePreservesInputWithoutRepublishingFallbackWindowTitle()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TimerTitle = "   ";

        Assert.Equal("   ", viewModel.TimerTitle);
        Assert.Equal("Hourglass", viewModel.WindowTitle);
        Assert.Equal([nameof(viewModel.TimerTitle)], changedProperties);
    }

    [Fact]
    public void TimerTickDoesNotRepublishUnchangedWindowTitle()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.TimerTitle = "Tea";
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.Tick();

        Assert.DoesNotContain(nameof(viewModel.WindowTitle), changedProperties);
    }

    [Fact]
    public void ChangingTimerTitleWhileRunningDoesNotAlterCountdown()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();
        string remainingTime = viewModel.RemainingTime;

        viewModel.TimerTitle = "Coffee";

        Assert.Equal("Coffee", viewModel.WindowTitle);
        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal(remainingTime, viewModel.RemainingTime);
    }

    [Fact]
    public void ChangingTimerTitleWhilePausedDoesNotAlterCountdown()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();
        viewModel.PauseResumeCommand.Execute(null);
        string remainingTime = viewModel.RemainingTime;

        viewModel.TimerTitle = "Paused tea";

        Assert.Equal("Paused tea", viewModel.WindowTitle);
        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal(remainingTime, viewModel.RemainingTime);
    }

    [Fact]
    public void ExpiredAndResetTimerRetainTimerTitle()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.TimerTitle = "Eggs";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Eggs", viewModel.TimerTitle);
        Assert.Equal("Eggs", viewModel.WindowTitle);

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Eggs", viewModel.TimerTitle);
        Assert.Equal("Eggs", viewModel.WindowTitle);
    }

    [Fact]
    public void TimerTitleAndTimerInputRemainIndependent()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        string initialTimerInput = viewModel.TimerInput;

        viewModel.TimerTitle = "Laundry";

        Assert.Equal(initialTimerInput, viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);

        viewModel.TimerInput = "10 minutes";

        Assert.Equal("Laundry", viewModel.TimerTitle);
        Assert.Equal("Laundry", viewModel.WindowTitle);
        Assert.Equal(TimerState.Stopped, viewModel.State);
    }

    [Fact]
    public void TimerTitleControlUsesImmediateBindingAndDrivesWindowTitle()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        XElement titleInput = Assert.Single(
            document.Descendants(avalonia + "TextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerTitleTextBox");
        XElement timerInput = Assert.Single(
            document.Descendants(avalonia + "TextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerInputTextBox");
        XElement timerDisplay = Assert.IsType<XElement>(timerInput.Parent);

        Assert.Equal("{Binding WindowTitle}", window.Attribute("Title")?.Value);
        Assert.Equal(
            "{Binding TimerTitle, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            titleInput.Attribute("Text")?.Value);
        Assert.Equal("Click to enter title", titleInput.Attribute("PlaceholderText")?.Value);
        Assert.Equal("titleInput", titleInput.Attribute("Classes")?.Value);
        Assert.Null(titleInput.Attribute("IsVisible"));
        Assert.Same(titleInput.Parent, timerDisplay.Parent);
        Assert.Contains(timerDisplay, titleInput.ElementsAfterSelf());
    }

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
    public void ContextMenuUsesExistingTimerCommandsAndPersistentOptionBindings()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement contextMenu = Assert.Single(
            rootGrid.Element(avalonia + "Grid.ContextMenu")?.Elements(avalonia + "ContextMenu") ?? []);
        Dictionary<string, XElement> menuItems = contextMenu
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => element.Attribute("Header")!.Value, StringComparer.Ordinal);

        Assert.Equal("{Binding AlwaysOnTop}", window.Attribute("Topmost")?.Value);
        Assert.Equal("Transparent", rootGrid.Attribute("Background")?.Value);
        Assert.Equal("{Binding StartCommand}", menuItems["Start"].Attribute("Command")?.Value);
        Assert.Equal("{Binding PauseResumeCommand}", menuItems["{Binding PauseResumeText}"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ResetCommand}", menuItems["Stop"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Notifications"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding NotificationsEnabled, Mode=OneWay}", menuItems["Notifications"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleNotificationsCommand}", menuItems["Notifications"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Sound"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding AudioAlertsEnabled, Mode=OneWay}", menuItems["Sound"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleAudioAlertsCommand}", menuItems["Sound"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Always on top"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding AlwaysOnTop, Mode=OneWay}", menuItems["Always on top"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleAlwaysOnTopCommand}", menuItems["Always on top"].Attribute("Command")?.Value);
        Assert.Equal("ExitMenuItemClick", menuItems["Exit"].Attribute("Click")?.Value);
    }

    [Fact]
    public void ContextMenuTimerCommandAvailabilityMatchesEveryTimerState()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        AssertCommandAvailability(viewModel, canStart: true, canPauseResume: false, canStop: false);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: true, canStop: true);

        viewModel.PauseResumeCommand.Execute(null);
        Assert.Equal("Resume", viewModel.PauseResumeText);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: true, canStop: true);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();
        Assert.Equal(TimerState.Expired, viewModel.State);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: false, canStop: true);
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
    public void ProgressAdvancesFreezesResumesExpiresAndResets()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(0, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(2.5));
        viewModel.Tick();

        Assert.Equal(25, viewModel.ProgressPercent);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(4));
        viewModel.Tick();

        Assert.Equal(25, viewModel.ProgressPercent);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2.5));
        viewModel.Tick();

        Assert.Equal(50, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(100, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(100, viewModel.ProgressPercent);

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(0, viewModel.ProgressPercent);
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
    public async Task LoadSettingsRestoresAllOptions()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(
                notificationsEnabled: false,
                audioAlertsEnabled: false,
                alwaysOnTop: true)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.False(viewModel.NotificationsEnabled);
        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.AlwaysOnTop);
    }

    [Fact]
    public async Task ToggleNotificationsChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.NotificationsEnabled);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.NotificationsEnabled);
    }

    [Fact]
    public async Task ToggleAudioAlertsChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleAudioAlertsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.AudioAlertsEnabled);
    }

    [Fact]
    public async Task ToggleAlwaysOnTopChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleAlwaysOnTopCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.AlwaysOnTop);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.AlwaysOnTop);
    }

    [Fact]
    public async Task SettingsSaveFailureDoesNotCrashOrChangeTimerState()
    {
        var settingsStore = new RecordingSettingsStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.NotificationsEnabled);
        Assert.Equal(TimerState.Stopped, viewModel.State);
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

    [Fact]
    public async Task ExpiredTimerUsesLatestNotificationAndAudioSettings()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        viewModel.ToggleAudioAlertsCommand.Execute(null);
        await viewModel.PendingSettingsSave;
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, notificationService.CallCount);
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

    [Theory]
    [InlineData(0, 400, 0)]
    [InlineData(25, 400, 100)]
    [InlineData(50, 400, 200)]
    [InlineData(100, 400, 400)]
    [InlineData(-25, 400, 0)]
    [InlineData(125, 400, 400)]
    public void ProgressWidthConverterMapsClampedProgressToAvailableWidth(
        double progressPercent,
        double availableWidth,
        double expectedWidth)
    {
        double width = ProgressWidthConverter.CalculateWidth(progressPercent, availableWidth);

        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void ProgressWidthConverterRejectsInvalidAvailableWidth(double availableWidth)
    {
        double width = ProgressWidthConverter.CalculateWidth(50, availableWidth);

        Assert.Equal(0, width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ProgressWidthConverterRejectsNonFiniteProgress(double progressPercent)
    {
        double width = ProgressWidthConverter.CalculateWidth(progressPercent, 400);

        Assert.Equal(0, width);
    }

    [Fact]
    public void ProgressWidthConverterHandlesUnavailableBindingValues()
    {
        var converter = new ProgressWidthConverter();

        Assert.Equal(0d, converter.Convert([], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0d, converter.Convert([50d, null], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0d, converter.Convert([50d, new object()], typeof(double), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProgressWidthConverterRecalculatesForResizedWidth()
    {
        const double progressPercent = 50;

        double initialWidth = ProgressWidthConverter.CalculateWidth(progressPercent, 250);
        double resizedWidth = ProgressWidthConverter.CalculateWidth(progressPercent, 500);

        Assert.Equal(125, initialWidth);
        Assert.Equal(250, resizedWidth);
        Assert.Equal(50, progressPercent);
    }

    [Fact]
    public void ProgressLayerMatchesLegacyFullWindowBackgroundTreatment()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement progressLayer = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "ProgressLayer");
        XElement innerGrid = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "InnerGrid");
        XElement progressIndicator = Assert.Single(progressLayer.Elements(avalonia + "Border"));
        XElement fillBrush = Assert.Single(
            document.Descendants(avalonia + "SolidColorBrush"),
            element => element.Attribute(xaml + "Key")?.Value == "TimerProgressFillBrush");
        XElement widthBinding = Assert.Single(progressIndicator.Elements(avalonia + "Border.Width"));
        XElement multiBinding = Assert.Single(widthBinding.Elements(avalonia + "MultiBinding"));
        XElement[] bindings = multiBinding.Elements(avalonia + "Binding").ToArray();

        Assert.Equal("True", progressLayer.Attribute("ClipToBounds")?.Value);
        Assert.Equal("False", progressLayer.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("Left", progressIndicator.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("{StaticResource TimerProgressFillBrush}", progressIndicator.Attribute("Background")?.Value);
        Assert.Equal("#3665B3", fillBrush.Attribute("Color")?.Value);
        Assert.Equal("0.45", fillBrush.Attribute("Opacity")?.Value);
        Assert.Contains(innerGrid, progressLayer.ElementsAfterSelf());
        Assert.Equal("{StaticResource ProgressWidthConverter}", multiBinding.Attribute("Converter")?.Value);
        Assert.Equal("ProgressPercent", bindings[0].Attribute("Path")?.Value);
        Assert.Equal("ProgressLayer", bindings[1].Attribute("ElementName")?.Value);
        Assert.Equal("Bounds.Width", bindings[1].Attribute("Path")?.Value);
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

    private static void AssertCommandAvailability(
        MainWindowViewModel viewModel,
        bool canStart,
        bool canPauseResume,
        bool canStop)
    {
        Assert.Equal(canStart, viewModel.StartCommand.CanExecute(null));
        Assert.Equal(canPauseResume, viewModel.PauseResumeCommand.CanExecute(null));
        Assert.Equal(canStop, viewModel.ResetCommand.CanExecute(null));
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

        public bool ThrowOnSave { get; init; }

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
            Assert.Equal("app", key);

            if (this.ThrowOnSave)
            {
                return Task.FromException(new InvalidOperationException("Settings save failed."));
            }

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
