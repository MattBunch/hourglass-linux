using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hourglass.Platform;
using Hourglass.Serialization;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string ApplicationTitle = "Hourglass";
    private const string InvalidTimerStatusText = "Enter a valid current timer.";
    private const string SessionInhibitionReason = "Hourglass timer is running";
    private const string NotificationBody = "Timer complete";
    private const string SettingsKey = "app";
    private const string SavedTimersKey = "saved-timers";
    private const string ActiveSessionKey = "active-session";

    private readonly IAudioAlertService audioAlertService;
    private readonly CountdownEngine engine;
    private readonly INotificationService notificationService;
    private readonly ISessionInhibitor sessionInhibitor;
    private readonly ISettingsStore settingsStore;
    private readonly bool statusIconCanRecoverHiddenWindow;
    private readonly bool statusIconSupported;
    private readonly ISystemPowerService systemPowerService;
    private readonly Func<DateTime> wallClockNow;
    private IAsyncDisposable? activeAudioPlayback;
    private IAsyncDisposable? inhibitionLease;
    private Task pendingActiveSessionSave = Task.CompletedTask;
    private Task pendingSavedTimersSave = Task.CompletedTask;
    private Task pendingSettingsSave = Task.CompletedTask;
    private SavedTimersDocument savedTimers = SavedTimersDocument.Empty;
    private LinuxAppSettings settings = LinuxAppSettings.Default;
    private TimerViewState viewState = TimerViewState.Initial;

    public MainWindowViewModel()
        : this(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            NoOpNotificationService.Instance,
            NoOpSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            NoOpAudioAlertService.Instance,
            NoOpSystemPowerService.Instance)
    {
    }

    public MainWindowViewModel(CountdownEngine engine, Func<DateTime> wallClockNow)
        : this(
            engine,
            wallClockNow,
            NoOpNotificationService.Instance,
            NoOpSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            NoOpAudioAlertService.Instance,
            NoOpSystemPowerService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService)
        : this(
            engine,
            wallClockNow,
            notificationService,
            NoOpSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            NoOpAudioAlertService.Instance,
            NoOpSystemPowerService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISettingsStore settingsStore)
        : this(
            engine,
            wallClockNow,
            notificationService,
            NoOpSessionInhibitor.Instance,
            settingsStore,
            NoOpAudioAlertService.Instance,
            NoOpSystemPowerService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore)
        : this(
            engine,
            wallClockNow,
            notificationService,
            sessionInhibitor,
            settingsStore,
            NoOpAudioAlertService.Instance,
            NoOpSystemPowerService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService,
        ISystemPowerService systemPowerService,
        bool statusIconSupported = false,
        bool statusIconCanRecoverHiddenWindow = false)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.sessionInhibitor = sessionInhibitor ?? throw new ArgumentNullException(nameof(sessionInhibitor));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.systemPowerService = systemPowerService ?? throw new ArgumentNullException(nameof(systemPowerService));
        this.statusIconSupported = statusIconSupported;
        this.statusIconCanRecoverHiddenWindow = statusIconCanRecoverHiddenWindow;
        this.engine.Expired += this.OnEngineExpired;

        this.StartCommand = new RelayCommand(
            this.Start,
            () => this.viewState.PresentationMode == TimerPresentationMode.Input);
        this.PauseResumeCommand = new RelayCommand(
            this.PauseOrResume,
            () => !this.IsTimerModificationLocked && (this.engine.State is TimerState.Running or TimerState.Paused));
        this.ResetCommand = new RelayCommand(this.Reset, () => !this.IsTimerModificationLocked && this.engine.State != TimerState.Stopped);
        this.RestartCommand = new RelayCommand(this.Restart, () => !this.IsTimerModificationLocked && this.viewState.IsRestartVisible);
        this.CancelEditCommand = new RelayCommand(
            this.CancelActiveTimerEdit,
            () => !this.IsTimerModificationLocked && this.viewState.IsCancelVisible);
        this.ToggleNotificationsCommand = new RelayCommand(this.ToggleNotifications, () => !this.IsTimerModificationLocked);
        this.ToggleAudioAlertsCommand = new RelayCommand(this.ToggleAudioAlerts, () => !this.IsTimerModificationLocked);
        this.ToggleAlwaysOnTopCommand = new RelayCommand(this.ToggleAlwaysOnTop, () => !this.IsTimerModificationLocked);
        this.ToggleShowProgressInTaskbarCommand = new RelayCommand(this.ToggleShowProgressInTaskbar, () => !this.IsTimerModificationLocked);
        this.ToggleShowInNotificationAreaCommand = new RelayCommand(
            this.ToggleShowInNotificationArea,
            () => !this.IsTimerModificationLocked && this.IsStatusIconSupported);
        this.HideToNotificationAreaCommand = new RelayCommand(
            this.RequestHideToNotificationArea,
            () => this.CanHideToNotificationArea);
        this.TogglePopUpWhenExpiredCommand = new RelayCommand(this.TogglePopUpWhenExpired, () => !this.IsTimerModificationLocked);
        this.TogglePromptOnExitCommand = new RelayCommand(this.TogglePromptOnExit, () => !this.IsTimerModificationLocked);
        this.ToggleReverseProgressBarCommand = new RelayCommand(this.ToggleReverseProgressBar, () => !this.IsTimerModificationLocked);
        this.ToggleShowTimeElapsedCommand = new RelayCommand(this.ToggleShowTimeElapsed, () => !this.IsTimerModificationLocked);
        this.ToggleLoopTimerCommand = new RelayCommand(this.ToggleLoopTimer, () => !this.IsTimerModificationLocked);
        this.ToggleLoopSoundCommand = new RelayCommand(this.ToggleLoopSound, () => !this.IsTimerModificationLocked);
        this.ToggleCloseWhenExpiredCommand = new RelayCommand(this.ToggleCloseWhenExpired, () => !this.IsTimerModificationLocked);
        this.ToggleLockInterfaceCommand = new RelayCommand(this.ToggleLockInterface, () => !this.IsTimerModificationLocked);
        this.ToggleDoNotKeepComputerAwakeCommand = new RelayCommand(this.ToggleDoNotKeepComputerAwake, () => !this.IsTimerModificationLocked);
        this.ToggleShutDownWhenExpiredCommand = new RelayCommand(
            this.ToggleShutDownWhenExpired,
            () => !this.IsTimerModificationLocked && this.systemPowerService.IsShutdownSupported);
        this.ToggleRestoreActiveSessionOnStartupCommand = new RelayCommand(this.ToggleRestoreActiveSessionOnStartup, () => !this.IsTimerModificationLocked);
        this.ToggleOpenSavedTimersOnStartupCommand = new RelayCommand(this.ToggleOpenSavedTimersOnStartup, () => !this.IsTimerModificationLocked);
        this.SelectRecentInputCommand = new RelayCommand<string>(this.SelectRecentInput, input => !string.IsNullOrWhiteSpace(input) && !this.IsTimerModificationLocked);
        this.ClearRecentInputsCommand = new RelayCommand(this.ClearRecentInputs, () => this.RecentInputMenuItems.Length > 0 && !this.IsTimerModificationLocked);
        this.SaveCurrentTimerCommand = new RelayCommand(this.SaveCurrentTimer, () => !this.IsTimerModificationLocked && this.CanSaveCurrentTimer);
        this.OpenSavedTimerCommand = new RelayCommand<string>(this.OpenSavedTimer, id => !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.RemoveSavedTimerCommand = new RelayCommand<string>(this.RemoveSavedTimer, id => !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.ClearSavedTimersCommand = new RelayCommand(this.ClearSavedTimers, () => this.SavedTimerMenuItems.Length > 0 && !this.IsTimerModificationLocked);
        this.OpenAllSavedTimersCommand = new RelayCommand(() => { }, () => false);

        this.RefreshDisplay();
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService)
        : this(
            engine,
            wallClockNow,
            notificationService,
            sessionInhibitor,
            settingsStore,
            audioAlertService,
            NoOpSystemPowerService.Instance)
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? WindowAttentionRequested;

    public event EventHandler? ExpiryVisualFeedbackRequested;

    public event EventHandler? ValidationFeedbackRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? HideToNotificationAreaRequested;

    public RelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand RestartCommand { get; }

    public RelayCommand CancelEditCommand { get; }

    public RelayCommand ToggleNotificationsCommand { get; }

    public RelayCommand ToggleAudioAlertsCommand { get; }

    public RelayCommand ToggleAlwaysOnTopCommand { get; }

    public RelayCommand ToggleShowProgressInTaskbarCommand { get; }

    public RelayCommand ToggleShowInNotificationAreaCommand { get; }

    public RelayCommand HideToNotificationAreaCommand { get; }

    public RelayCommand TogglePopUpWhenExpiredCommand { get; }

    public RelayCommand TogglePromptOnExitCommand { get; }

    public RelayCommand ToggleReverseProgressBarCommand { get; }

    public RelayCommand ToggleShowTimeElapsedCommand { get; }

    public RelayCommand ToggleLoopTimerCommand { get; }

    public RelayCommand ToggleLoopSoundCommand { get; }

    public RelayCommand ToggleCloseWhenExpiredCommand { get; }

    public RelayCommand ToggleLockInterfaceCommand { get; }

    public RelayCommand ToggleDoNotKeepComputerAwakeCommand { get; }

    public RelayCommand ToggleShutDownWhenExpiredCommand { get; }

    public RelayCommand ToggleRestoreActiveSessionOnStartupCommand { get; }

    public RelayCommand ToggleOpenSavedTimersOnStartupCommand { get; }

    public RelayCommand<string> SelectRecentInputCommand { get; }

    public RelayCommand ClearRecentInputsCommand { get; }

    public RelayCommand SaveCurrentTimerCommand { get; }

    public RelayCommand<string> OpenSavedTimerCommand { get; }

    public RelayCommand<string> RemoveSavedTimerCommand { get; }

    public RelayCommand ClearSavedTimersCommand { get; }

    public RelayCommand OpenAllSavedTimersCommand { get; }

    public string TimerInput
    {
        get => this.viewState.TimerInput;
        set
        {
            value ??= string.Empty;

            if (value != this.viewState.TimerInput)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = value, HasValidationError = false });
                this.RefreshDisplay(this.engine.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText);
                this.OnPropertyChanged(nameof(this.CanSaveCurrentTimer));
                this.SaveCurrentTimerCommand.RaiseCanExecuteChanged();
                this.QueueActiveSessionSave();
            }
        }
    }

    public string? TimerTitle
    {
        get => this.viewState.TimerTitle;
        set
        {
            string nextTitle = value ?? string.Empty;

            if (nextTitle == this.viewState.TimerTitle)
            {
                return;
            }

            string previousWindowTitle = this.WindowTitle;
            this.TryEnterInputModeFromExpired();
            this.ReplaceViewState(this.viewState with { TimerTitle = nextTitle }, nameof(this.TimerTitle));

            if (previousWindowTitle != this.WindowTitle)
            {
                this.OnPropertyChanged(nameof(this.WindowTitle));
                this.OnPropertyChanged(nameof(this.StatusIconMenuState));
            }

            this.QueueActiveSessionSave();
        }
    }

    public string WindowTitle => FormatWindowTitle(this.TimerTitle);

    public string RemainingTime => this.viewState.RemainingTime;

    public string StatusText => this.viewState.StatusText;

    public string PauseResumeText => this.viewState.PauseResumeText;

    public bool IsInputEnabled => this.viewState.IsInputEnabled;

    public bool IsRunning => this.viewState.IsRunning;

    public TimerState State => this.viewState.State;

    public double ProgressPercent => this.viewState.ProgressPercent;

    public bool IsTimerInputVisible => this.viewState.IsTimerInputVisible;

    public bool IsRemainingTimeVisible => this.viewState.IsRemainingTimeVisible;

    public bool IsCompletionTextVisible => this.viewState.IsCompletionTextVisible;

    public bool IsStartVisible => this.viewState.IsStartVisible;

    public bool IsPauseVisible => this.viewState.IsPauseVisible;

    public bool IsResumeVisible => this.viewState.IsResumeVisible;

    public bool IsStopVisible => this.viewState.IsStopVisible;

    public bool IsRestartVisible => this.viewState.IsRestartVisible;

    public bool IsCancelVisible => this.viewState.IsCancelVisible;

    public bool NotificationsEnabled => this.settings.NotificationsEnabled;

    public bool AudioAlertsEnabled => this.settings.AudioAlertsEnabled;

    public bool AlwaysOnTop => this.settings.AlwaysOnTop;

    public bool ShowProgressInTaskbar => this.settings.ShowProgressInTaskbar;

    public bool IsStatusIconSupported => this.statusIconSupported;

    public bool ShowInNotificationArea => this.settings.ShowInNotificationArea && this.IsStatusIconSupported;

    public bool CanHideToNotificationArea => this.ShowInNotificationArea && this.statusIconCanRecoverHiddenWindow;

    public bool PopUpWhenExpired => this.settings.PopUpWhenExpired;

    public bool PromptOnExit => this.settings.PromptOnExit;

    public bool ReverseProgressBar => this.settings.ReverseProgressBar;

    public bool ShowTimeElapsed => this.settings.ShowTimeElapsed;

    public bool LoopTimer => this.settings.LoopTimer;

    public bool LoopSound => this.settings.LoopSound;

    public bool CloseWhenExpired => this.settings.CloseWhenExpired;

    public bool LockInterface => this.settings.LockInterface;

    public bool DoNotKeepComputerAwake => this.settings.DoNotKeepComputerAwake;

    public bool ShutDownWhenExpired => this.settings.ShutDownWhenExpired;

    public bool IsShutdownSupported => this.systemPowerService.IsShutdownSupported;

    public bool RestoreActiveSessionOnStartup => this.settings.RestoreActiveSessionOnStartup;

    public bool OpenSavedTimersOnStartup => this.settings.OpenSavedTimersOnStartup;

    public RecentInputMenuItem[] RecentInputMenuItems =>
        this.settings.RecentTimerInputs.Select(input => new RecentInputMenuItem(input)).ToArray();

    public SavedTimerMenuItem[] SavedTimerMenuItems =>
        this.savedTimers.Timers.Select(timer => new SavedTimerMenuItem(timer.Id, timer.Header)).ToArray();

    public bool CanSaveCurrentTimer =>
        TimerStart.FromString(this.TimerInput) is { IsValid: true };

    public bool IsTimerModificationLocked =>
        this.settings.LockInterface && (this.engine.State is TimerState.Running or TimerState.Paused);

    public bool ShouldPromptOnExit =>
        this.settings.PromptOnExit && (this.engine.State is TimerState.Running or TimerState.Paused);

    public StatusIconMenuState StatusIconMenuState => new(
        this.WindowTitle,
        this.ShowInNotificationArea,
        this.PauseResumeText,
        this.PauseResumeCommand.CanExecute(null),
        this.ResetCommand.CanExecute(null),
        this.RestartCommand.CanExecute(null),
        this.CanHideToNotificationArea,
        true);

    public bool HasValidationError => this.viewState.HasValidationError;

    public bool HasCompletionEmphasis => this.viewState.HasCompletionEmphasis;

    internal Task PendingSettingsSave => Task.WhenAll(
        this.pendingSettingsSave,
        this.pendingSavedTimersSave,
        this.pendingActiveSessionSave);

    internal DesktopProgressRequest DesktopProgressRequest =>
        DesktopProgressProjection.FromViewState(this.viewState, this.settings.ShowProgressInTaskbar);

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        LinuxAppSettings loadedSettings = await this.LoadDocumentAsync(SettingsKey, LinuxAppSettings.Default, cancellationToken);
        SavedTimersDocument loadedSavedTimers = await this.LoadDocumentAsync(SavedTimersKey, SavedTimersDocument.Empty, cancellationToken);

        this.ReplaceSettings(loadedSettings, save: false);
        this.ReplaceSavedTimers(loadedSavedTimers, save: false);

        if (loadedSettings.RestoreActiveSessionOnStartup
            && await this.TryRestoreActiveSessionAsync(cancellationToken))
        {
            return;
        }

        if (this.engine.State == TimerState.Stopped)
        {
            this.ReplaceViewState(this.viewState with { TimerInput = loadedSettings.GetInitialTimerInput(TimerViewState.DefaultTimerInput) });
            this.RefreshDisplay(TimerViewState.ReadyStatusText);
        }
    }

    private async Task<T> LoadDocumentAsync<T>(string key, T fallback, CancellationToken cancellationToken)
    {
        try
        {
            return await this.settingsStore.LoadAsync<T>(key, cancellationToken)
                ?? fallback;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private async Task<bool> TryRestoreActiveSessionAsync(CancellationToken cancellationToken)
    {
        ActiveTimerSessionDocument? session;

        try
        {
            session = await this.settingsStore.LoadAsync<ActiveTimerSessionDocument>(ActiveSessionKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            session = null;
        }

        TimerInfo? timerInfo = session?.ToTimerInfo(this.wallClockNow());
        if (session == null || timerInfo == null)
        {
            return false;
        }

        bool expiredWhileClosed = session.State == TimerState.Running
            && timerInfo.State == TimerState.Expired;

        this.engine.Restore(timerInfo);
        TimerPresentationMode presentationMode = timerInfo.State == TimerState.Expired
            ? TimerPresentationMode.Status
            : ToTimerPresentationMode(session.PresentationMode);
        this.ReplaceViewState(TimerViewState.FromTimerState(
            string.IsNullOrWhiteSpace(session.TimerInput) ? TimerViewState.DefaultTimerInput : session.TimerInput,
            this.engine.Snapshot,
            session.TimerTitle,
            null,
            presentationMode,
            null,
            false,
            this.settings.ShowTimeElapsed,
            this.settings.ReverseProgressBar,
            this.IsTimerModificationLocked));
        this.RefreshDisplay(timerInfo.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : null, hasValidationError: false);

        if (this.engine.State == TimerState.Running)
        {
            _ = this.AcquireInhibitionAsync();
        }
        else if (expiredWhileClosed)
        {
            _ = this.HandleRestoredExpiredAsync();
        }

        return true;
    }

    public void Dispose()
    {
        this.engine.Expired -= this.OnEngineExpired;
        _ = this.StopActiveAudioAsync();
        _ = this.ReleaseInhibitionAsync();
    }

    public void Tick()
    {
        this.engine.Update();
        this.RefreshDisplay();
    }

    internal bool TryEnterInputModeFromExpired()
    {
        if (this.engine.State != TimerState.Expired)
        {
            return false;
        }

        this.StopAndShowInput(this.TimerInput);
        return true;
    }

    internal bool TryEnterTimerInputMode()
    {
        if (this.IsTimerModificationLocked)
        {
            return false;
        }

        if (this.viewState.PresentationMode == TimerPresentationMode.Input)
        {
            return false;
        }

        if (this.engine.State == TimerState.Expired)
        {
            return this.TryEnterInputModeFromExpired();
        }

        if (this.engine.State is not (TimerState.Running or TimerState.Paused))
        {
            return false;
        }

        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Input,
            InputBeforeEdit = this.TimerInput,
            HasValidationError = false
        });
        this.RefreshDisplay();
        this.QueueActiveSessionSave();
        return true;
    }

    internal bool TryHandleEscape()
    {
        if (this.CancelEditCommand.CanExecute(null))
        {
            this.CancelEditCommand.Execute(null);
            return true;
        }

        return this.TryEnterInputModeFromExpired();
    }

    private void Start()
    {
        DateTime now = this.wallClockNow();
        TimerStart? timerStart = TimerStart.FromString(this.TimerInput);

        if (timerStart == null || !timerStart.IsValid || !timerStart.TryGetEndTime(now, out DateTime endTime) || endTime < now)
        {
            this.ShowValidationError();
            return;
        }

        if (!this.engine.Start(timerStart, now))
        {
            this.ShowValidationError();
            return;
        }

        _ = this.StopActiveAudioAsync();

        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null
        });
        this.RefreshDisplay(TimerViewState.RunningStatusText, hasValidationError: false);
        _ = this.AcquireInhibitionAsync();
        this.ReplaceSettings(this.settings.AddRecentTimerInput(this.TimerInput), save: true);
        this.QueueActiveSessionSave();
    }

    private void PauseOrResume()
    {
        if (this.engine.State == TimerState.Running)
        {
            this.engine.Pause();
            this.RefreshDisplay(TimerViewState.PausedStatusText);
            _ = this.ReleaseInhibitionAsync();
            this.QueueActiveSessionSave();
            return;
        }

        if (this.engine.State == TimerState.Paused)
        {
            this.engine.Resume(this.wallClockNow());
            this.RefreshDisplay(TimerViewState.RunningStatusText);
            _ = this.AcquireInhibitionAsync();
            this.QueueActiveSessionSave();
        }
    }

    private void Reset()
    {
        _ = this.StopActiveAudioAsync();
        this.StopAndShowInput(this.TimerInput);
    }

    private void Restart()
    {
        _ = this.StopActiveAudioAsync();

        if (!this.engine.Restart(this.wallClockNow()))
        {
            return;
        }

        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.RunningStatusText, hasValidationError: false);
        _ = this.AcquireInhibitionAsync();
        this.QueueActiveSessionSave();
    }

    private void CancelActiveTimerEdit()
    {
        if (!this.viewState.IsCancelVisible || this.viewState.InputBeforeEdit is not string originalInput)
        {
            return;
        }

        this.ReplaceViewState(this.viewState with
        {
            TimerInput = originalInput,
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay();
        this.QueueActiveSessionSave();
    }

    private void StopAndShowInput(string timerInput)
    {
        bool wasLocked = this.settings.LockInterface;
        _ = this.StopActiveAudioAsync();
        this.engine.Stop();
        this.ReplaceViewState(this.viewState with
        {
            TimerInput = timerInput,
            PresentationMode = TimerPresentationMode.Input,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.ReadyStatusText);
        _ = this.ReleaseInhibitionAsync();
        this.QueueActiveSessionSave();

        if (wasLocked)
        {
            this.ReplaceSettings(this.settings with { LockInterface = false }, save: true);
        }
    }

    private void ToggleNotifications()
    {
        this.ReplaceSettings(
            this.settings with { NotificationsEnabled = !this.settings.NotificationsEnabled },
            save: true);
    }

    private void ToggleAudioAlerts()
    {
        if (this.settings.AudioAlertsEnabled)
        {
            _ = this.StopActiveAudioAsync();
        }

        this.ReplaceSettings(
            this.settings with { AudioAlertsEnabled = !this.settings.AudioAlertsEnabled },
            save: true);
    }

    private void ToggleAlwaysOnTop()
    {
        this.ReplaceSettings(
            this.settings with { AlwaysOnTop = !this.settings.AlwaysOnTop },
            save: true);
    }

    private void ToggleShowProgressInTaskbar()
    {
        this.ReplaceSettings(
            this.settings with { ShowProgressInTaskbar = !this.settings.ShowProgressInTaskbar },
            save: true);
    }

    private void ToggleShowInNotificationArea()
    {
        if (!this.IsStatusIconSupported)
        {
            return;
        }

        this.ReplaceSettings(
            this.settings with { ShowInNotificationArea = !this.settings.ShowInNotificationArea },
            save: true);
    }

    private void RequestHideToNotificationArea()
    {
        if (this.CanHideToNotificationArea)
        {
            PublishSafely(this.HideToNotificationAreaRequested);
        }
    }

    private void TogglePopUpWhenExpired()
    {
        this.ReplaceSettings(
            this.settings with { PopUpWhenExpired = !this.settings.PopUpWhenExpired },
            save: true);
    }

    private void TogglePromptOnExit()
    {
        this.ReplaceSettings(
            this.settings with { PromptOnExit = !this.settings.PromptOnExit },
            save: true);
    }

    private void ToggleReverseProgressBar()
    {
        this.ReplaceSettings(
            this.settings with { ReverseProgressBar = !this.settings.ReverseProgressBar },
            save: true);
        this.RefreshDisplay();
    }

    private void ToggleShowTimeElapsed()
    {
        this.ReplaceSettings(
            this.settings with { ShowTimeElapsed = !this.settings.ShowTimeElapsed },
            save: true);
        this.RefreshDisplay();
    }

    private void ToggleLoopTimer()
    {
        this.ReplaceSettings(
            this.settings with
            {
                LoopTimer = !this.settings.LoopTimer,
                CloseWhenExpired = this.settings.LoopTimer ? this.settings.CloseWhenExpired : false
            },
            save: true);
    }

    private void ToggleLoopSound()
    {
        bool nextLoopSound = !this.settings.LoopSound;
        if (!nextLoopSound)
        {
            _ = this.StopActiveAudioAsync();
        }

        this.ReplaceSettings(
            this.settings with
            {
                LoopSound = nextLoopSound,
                CloseWhenExpired = nextLoopSound ? false : this.settings.CloseWhenExpired
            },
            save: true);
    }

    private void ToggleCloseWhenExpired()
    {
        bool nextCloseWhenExpired = !this.settings.CloseWhenExpired;
        this.ReplaceSettings(
            this.settings with
            {
                CloseWhenExpired = nextCloseWhenExpired,
                LoopTimer = nextCloseWhenExpired ? false : this.settings.LoopTimer,
                LoopSound = nextCloseWhenExpired ? false : this.settings.LoopSound
            },
            save: true);
    }

    private void ToggleLockInterface()
    {
        this.ReplaceSettings(
            this.settings with { LockInterface = !this.settings.LockInterface },
            save: true);
        this.RefreshDisplay();
    }

    private void ToggleDoNotKeepComputerAwake()
    {
        bool nextDoNotKeepComputerAwake = !this.settings.DoNotKeepComputerAwake;
        this.ReplaceSettings(
            this.settings with { DoNotKeepComputerAwake = nextDoNotKeepComputerAwake },
            save: true);

        if (this.engine.State == TimerState.Running)
        {
            if (nextDoNotKeepComputerAwake)
            {
                _ = this.ReleaseInhibitionAsync();
            }
            else
            {
                _ = this.AcquireInhibitionAsync();
            }
        }
    }

    private void ToggleShutDownWhenExpired()
    {
        if (!this.systemPowerService.IsShutdownSupported)
        {
            return;
        }

        this.ReplaceSettings(
            this.settings with { ShutDownWhenExpired = !this.settings.ShutDownWhenExpired },
            save: true);
    }

    private void ToggleRestoreActiveSessionOnStartup()
    {
        this.ReplaceSettings(
            this.settings with { RestoreActiveSessionOnStartup = !this.settings.RestoreActiveSessionOnStartup },
            save: true);
    }

    private void ToggleOpenSavedTimersOnStartup()
    {
        this.ReplaceSettings(
            this.settings with { OpenSavedTimersOnStartup = !this.settings.OpenSavedTimersOnStartup },
            save: true);
    }

    private void SelectRecentInput(string? timerInput)
    {
        if (string.IsNullOrWhiteSpace(timerInput))
        {
            return;
        }

        if (this.engine.State == TimerState.Expired)
        {
            this.TryEnterInputModeFromExpired();
        }
        else if (this.viewState.PresentationMode != TimerPresentationMode.Input)
        {
            this.TryEnterTimerInputMode();
        }

        this.ReplaceViewState(this.viewState with
        {
            TimerInput = timerInput,
            PresentationMode = TimerPresentationMode.Input,
            HasValidationError = false
        });
        this.RefreshDisplay(this.engine.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText, hasValidationError: false);
        this.QueueActiveSessionSave();
    }

    private void ClearRecentInputs()
    {
        this.ReplaceSettings(this.settings.ClearRecentTimerInputs(), save: true);
    }

    private void SaveCurrentTimer()
    {
        if (!this.CanSaveCurrentTimer)
        {
            this.ShowValidationError();
            return;
        }

        SavedTimerDefinition? existing = this.savedTimers.Timers.FirstOrDefault(timer =>
            StringComparer.Ordinal.Equals(timer.TimerInput, this.TimerInput.Trim())
            && StringComparer.Ordinal.Equals(timer.TimerTitle, this.TimerTitle ?? string.Empty));
        SavedTimerDefinition savedTimer = existing == null
            ? SavedTimerDefinition.Create(this.TimerInput, this.TimerTitle ?? string.Empty, this.settings)
            : new SavedTimerDefinition(
                existing.Id,
                this.TimerInput,
                this.TimerTitle ?? string.Empty,
                existing.DisplayName,
                SavedTimerOptions.FromSettings(this.settings));

        this.ReplaceSavedTimers(this.savedTimers.AddOrReplace(savedTimer), save: true);
    }

    private void OpenSavedTimer(string? id)
    {
        SavedTimerDefinition? savedTimer = this.FindSavedTimer(id);
        if (savedTimer == null)
        {
            return;
        }

        _ = this.StopActiveAudioAsync();
        this.engine.Stop();
        this.ReplaceSettings(savedTimer.Options.ApplyTo(this.settings), save: true);
        this.ReplaceViewState(this.viewState with
        {
            TimerInput = savedTimer.TimerInput,
            TimerTitle = savedTimer.TimerTitle,
            PresentationMode = TimerPresentationMode.Input,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.ReadyStatusText, hasValidationError: false);
        _ = this.ReleaseInhibitionAsync();
        this.QueueActiveSessionSave();
    }

    private void RemoveSavedTimer(string? id)
    {
        if (this.FindSavedTimer(id) == null)
        {
            return;
        }

        this.ReplaceSavedTimers(this.savedTimers.Remove(id!), save: true);
    }

    private void ClearSavedTimers()
    {
        this.ReplaceSavedTimers(SavedTimersDocument.Empty, save: true);
    }

    private SavedTimerDefinition? FindSavedTimer(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return this.savedTimers.Timers.FirstOrDefault(timer => StringComparer.Ordinal.Equals(timer.Id, id.Trim()));
    }

    private void RefreshDisplay(string? explicitStatus = null, bool? hasValidationError = null)
    {
        this.ReplaceViewState(TimerViewState.FromTimerState(
            this.TimerInput,
            this.engine.Snapshot,
            this.TimerTitle ?? string.Empty,
            explicitStatus,
            this.viewState.PresentationMode,
            this.viewState.InputBeforeEdit,
            hasValidationError ?? this.viewState.HasValidationError,
            this.settings.ShowTimeElapsed,
            this.settings.ReverseProgressBar,
            this.IsTimerModificationLocked));
        this.OnPropertyChanged(nameof(this.TimerInput));
        this.OnPropertyChanged(nameof(this.RemainingTime));
        this.OnPropertyChanged(nameof(this.StatusText));
        this.OnPropertyChanged(nameof(this.PauseResumeText));
        this.OnPropertyChanged(nameof(this.IsInputEnabled));
        this.OnPropertyChanged(nameof(this.IsRunning));
        this.OnPropertyChanged(nameof(this.State));
        this.OnPropertyChanged(nameof(this.ShouldPromptOnExit));
        this.OnPropertyChanged(nameof(this.IsTimerModificationLocked));
        this.OnPropertyChanged(nameof(this.ProgressPercent));
        this.OnPropertyChanged(nameof(this.DesktopProgressRequest));
        this.OnPropertyChanged(nameof(this.IsTimerInputVisible));
        this.OnPropertyChanged(nameof(this.IsRemainingTimeVisible));
        this.OnPropertyChanged(nameof(this.IsCompletionTextVisible));
        this.OnPropertyChanged(nameof(this.IsStartVisible));
        this.OnPropertyChanged(nameof(this.IsPauseVisible));
        this.OnPropertyChanged(nameof(this.IsResumeVisible));
        this.OnPropertyChanged(nameof(this.IsStopVisible));
        this.OnPropertyChanged(nameof(this.IsRestartVisible));
        this.OnPropertyChanged(nameof(this.IsCancelVisible));
        this.OnPropertyChanged(nameof(this.StatusIconMenuState));
        this.OnPropertyChanged(nameof(this.HasValidationError));
        this.OnPropertyChanged(nameof(this.HasCompletionEmphasis));
        this.OnPropertyChanged(nameof(this.CanSaveCurrentTimer));
        this.StartCommand.RaiseCanExecuteChanged();
        this.PauseResumeCommand.RaiseCanExecuteChanged();
        this.ResetCommand.RaiseCanExecuteChanged();
        this.RestartCommand.RaiseCanExecuteChanged();
        this.CancelEditCommand.RaiseCanExecuteChanged();
        this.SelectRecentInputCommand.RaiseCanExecuteChanged();
        this.ClearRecentInputsCommand.RaiseCanExecuteChanged();
        this.SaveCurrentTimerCommand.RaiseCanExecuteChanged();
        this.OpenSavedTimerCommand.RaiseCanExecuteChanged();
        this.RemoveSavedTimerCommand.RaiseCanExecuteChanged();
        this.ClearSavedTimersCommand.RaiseCanExecuteChanged();
        this.OpenAllSavedTimersCommand.RaiseCanExecuteChanged();
        this.RaiseSettingsCommandCanExecuteChanged();
    }

    private static string FormatWindowTitle(string? timerTitle)
    {
        return string.IsNullOrWhiteSpace(timerTitle) ? ApplicationTitle : timerTitle;
    }

    private void ReplaceViewState(TimerViewState next, string? changedPropertyName = null)
    {
        if (this.viewState == next)
        {
            return;
        }

        this.viewState = next;
        this.OnPropertyChanged(changedPropertyName);
    }

    private void ReplaceSettings(LinuxAppSettings next, bool save)
    {
        ArgumentNullException.ThrowIfNull(next);

        LinuxAppSettings previous = this.settings;
        this.settings = next;

        if (!previous.RecentTimerInputs.SequenceEqual(next.RecentTimerInputs, StringComparer.Ordinal))
        {
            this.OnPropertyChanged(nameof(this.RecentInputMenuItems));
            this.ClearRecentInputsCommand.RaiseCanExecuteChanged();
        }

        if (previous.NotificationsEnabled != next.NotificationsEnabled)
        {
            this.OnPropertyChanged(nameof(this.NotificationsEnabled));
        }

        if (previous.AudioAlertsEnabled != next.AudioAlertsEnabled)
        {
            this.OnPropertyChanged(nameof(this.AudioAlertsEnabled));
        }

        if (previous.AlwaysOnTop != next.AlwaysOnTop)
        {
            this.OnPropertyChanged(nameof(this.AlwaysOnTop));
        }

        if (previous.ShowProgressInTaskbar != next.ShowProgressInTaskbar)
        {
            this.OnPropertyChanged(nameof(this.ShowProgressInTaskbar));
            this.OnPropertyChanged(nameof(this.DesktopProgressRequest));
        }

        if (previous.ShowInNotificationArea != next.ShowInNotificationArea)
        {
            this.OnPropertyChanged(nameof(this.ShowInNotificationArea));
            this.OnPropertyChanged(nameof(this.CanHideToNotificationArea));
            this.OnPropertyChanged(nameof(this.StatusIconMenuState));
        }

        if (previous.PopUpWhenExpired != next.PopUpWhenExpired)
        {
            this.OnPropertyChanged(nameof(this.PopUpWhenExpired));
        }

        if (previous.PromptOnExit != next.PromptOnExit)
        {
            this.OnPropertyChanged(nameof(this.PromptOnExit));
            this.OnPropertyChanged(nameof(this.ShouldPromptOnExit));
        }

        PublishSettingsChange(previous.ReverseProgressBar, next.ReverseProgressBar, nameof(this.ReverseProgressBar));
        PublishSettingsChange(previous.ShowTimeElapsed, next.ShowTimeElapsed, nameof(this.ShowTimeElapsed));
        PublishSettingsChange(previous.LoopTimer, next.LoopTimer, nameof(this.LoopTimer));
        PublishSettingsChange(previous.LoopSound, next.LoopSound, nameof(this.LoopSound));
        PublishSettingsChange(previous.CloseWhenExpired, next.CloseWhenExpired, nameof(this.CloseWhenExpired));
        PublishSettingsChange(previous.DoNotKeepComputerAwake, next.DoNotKeepComputerAwake, nameof(this.DoNotKeepComputerAwake));
        PublishSettingsChange(previous.ShutDownWhenExpired, next.ShutDownWhenExpired, nameof(this.ShutDownWhenExpired));
        PublishSettingsChange(previous.RestoreActiveSessionOnStartup, next.RestoreActiveSessionOnStartup, nameof(this.RestoreActiveSessionOnStartup));
        PublishSettingsChange(previous.OpenSavedTimersOnStartup, next.OpenSavedTimersOnStartup, nameof(this.OpenSavedTimersOnStartup));

        if (previous.LockInterface != next.LockInterface)
        {
            this.OnPropertyChanged(nameof(this.LockInterface));
            this.OnPropertyChanged(nameof(this.IsTimerModificationLocked));
            this.OnPropertyChanged(nameof(this.CanHideToNotificationArea));
            this.OnPropertyChanged(nameof(this.StatusIconMenuState));
        }

        if (save)
        {
            this.QueueSettingsSave(next);
            this.QueueActiveSessionSave();
        }

        this.RaiseSettingsCommandCanExecuteChanged();
    }

    private void ReplaceSavedTimers(SavedTimersDocument next, bool save)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (this.savedTimers == next)
        {
            return;
        }

        this.savedTimers = next;
        this.OnPropertyChanged(nameof(this.SavedTimerMenuItems));
        this.ClearSavedTimersCommand.RaiseCanExecuteChanged();

        if (save)
        {
            this.QueueSavedTimersSave(next);
        }
    }

    private async void OnEngineExpired(object? sender, EventArgs e)
    {
        await this.HandleEngineExpiredAsync().ConfigureAwait(false);
    }

    private async Task HandleEngineExpiredAsync()
    {
        bool loopTimer = this.settings.LoopTimer && this.engine.SupportsRestart;
        bool closeWhenExpired = this.settings.CloseWhenExpired && !loopTimer && !this.settings.LoopSound;
        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.TimerCompleteStatusText, hasValidationError: false);
        this.QueueActiveSessionSave();

        this.ClearLockInterfaceAfterCompletion();

        PublishSafely(this.ExpiryVisualFeedbackRequested);

        if (this.settings.PopUpWhenExpired && !closeWhenExpired)
        {
            PublishSafely(this.WindowAttentionRequested);
        }

        await this.ReleaseInhibitionAsync().ConfigureAwait(false);
        await this.NotifyTimerExpiredAsync().ConfigureAwait(false);
        await this.PlayTimerExpiredAudioAsync().ConfigureAwait(false);

        if (this.settings.ShutDownWhenExpired && this.systemPowerService.IsShutdownSupported)
        {
            await this.RequestShutdownAsync().ConfigureAwait(false);
        }

        if (loopTimer)
        {
            this.RestartLoopingTimer();
            return;
        }

        if (closeWhenExpired)
        {
            PublishSafely(this.CloseRequested);
        }
    }

    private async Task HandleRestoredExpiredAsync()
    {
        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.TimerCompleteStatusText, hasValidationError: false);
        this.ClearLockInterfaceAfterCompletion();
        this.QueueActiveSessionSave();

        PublishSafely(this.ExpiryVisualFeedbackRequested);

        if (this.settings.PopUpWhenExpired)
        {
            PublishSafely(this.WindowAttentionRequested);
        }

        await this.NotifyTimerExpiredAsync().ConfigureAwait(false);
        await this.PlayTimerExpiredAudioAsync().ConfigureAwait(false);
    }

    private void ClearLockInterfaceAfterCompletion()
    {
        if (this.settings.LockInterface)
        {
            this.ReplaceSettings(this.settings with { LockInterface = false }, save: true);
        }
    }

    private void ShowValidationError()
    {
        this.RefreshDisplay(InvalidTimerStatusText, hasValidationError: true);
        PublishSafely(this.ValidationFeedbackRequested);
    }

    private void PublishSafely(EventHandler? handlers)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task NotifyTimerExpiredAsync()
    {
        if (!this.settings.NotificationsEnabled)
        {
            return;
        }

        try
        {
            string notificationTitle = FormatWindowTitle(this.TimerTitle);
            await this.notificationService.ShowTimerExpiredAsync(notificationTitle, NotificationBody).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task PlayTimerExpiredAudioAsync()
    {
        if (!this.settings.AudioAlertsEnabled)
        {
            await this.StopActiveAudioAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await this.StopActiveAudioAsync().ConfigureAwait(false);
            this.activeAudioPlayback = this.settings.LoopSound
                ? await this.audioAlertService.PlayAlertLoopingAsync(AudioAlertSoundIds.NormalBeep).ConfigureAwait(false)
                : await this.audioAlertService.PlayAlertAsync(AudioAlertSoundIds.NormalBeep).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task StopActiveAudioAsync()
    {
        IAsyncDisposable? playback = this.activeAudioPlayback;
        this.activeAudioPlayback = null;

        if (playback == null)
        {
            return;
        }

        try
        {
            await playback.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task RequestShutdownAsync()
    {
        try
        {
            await this.systemPowerService.RequestShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void RestartLoopingTimer()
    {
        if (!this.engine.Restart(this.wallClockNow()))
        {
            return;
        }

        this.ReplaceViewState(this.viewState with
        {
            PresentationMode = TimerPresentationMode.Status,
            InputBeforeEdit = null,
            HasValidationError = false
        });
        this.RefreshDisplay(TimerViewState.RunningStatusText, hasValidationError: false);
        _ = this.AcquireInhibitionAsync();
        this.QueueActiveSessionSave();
    }

    private async Task AcquireInhibitionAsync()
    {
        await this.ReleaseInhibitionAsync().ConfigureAwait(false);

        if (this.settings.DoNotKeepComputerAwake)
        {
            return;
        }

        try
        {
            this.inhibitionLease = await this.sessionInhibitor.InhibitAsync(
                SessionInhibitionReason,
                inhibitSuspend: true,
                inhibitIdle: true).ConfigureAwait(false);
        }
        catch (Exception)
        {
            this.inhibitionLease = null;
        }
    }

    private async Task ReleaseInhibitionAsync()
    {
        IAsyncDisposable? lease = this.inhibitionLease;
        this.inhibitionLease = null;

        if (lease == null)
        {
            return;
        }

        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void QueueSettingsSave(LinuxAppSettings settingsSnapshot)
    {
        this.pendingSettingsSave = this.SaveSettingsAfterAsync(this.pendingSettingsSave, settingsSnapshot);
    }

    private void QueueSavedTimersSave(SavedTimersDocument savedTimersSnapshot)
    {
        this.pendingSavedTimersSave = this.SaveDocumentAfterAsync(this.pendingSavedTimersSave, SavedTimersKey, savedTimersSnapshot);
    }

    private void QueueActiveSessionSave()
    {
        ActiveTimerSessionDocument session = ActiveTimerSessionDocument.FromTimerInfo(
            this.TimerInput,
            this.TimerTitle ?? string.Empty,
            ToActiveTimerPresentationMode(this.viewState.PresentationMode),
            this.engine.ToTimerInfo(),
            this.wallClockNow());
        this.pendingActiveSessionSave = this.SaveDocumentAfterAsync(this.pendingActiveSessionSave, ActiveSessionKey, session);
    }

    private async Task SaveSettingsAfterAsync(Task previousSave, LinuxAppSettings settingsSnapshot)
    {
        await this.SaveDocumentAfterAsync(previousSave, SettingsKey, settingsSnapshot).ConfigureAwait(false);
    }

    private async Task SaveDocumentAfterAsync<T>(Task previousSave, string key, T document)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await this.settingsStore.SaveAsync(key, document).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static ActiveTimerPresentationMode ToActiveTimerPresentationMode(TimerPresentationMode presentationMode)
    {
        return presentationMode == TimerPresentationMode.Status
            ? ActiveTimerPresentationMode.Status
            : ActiveTimerPresentationMode.Input;
    }

    private static TimerPresentationMode ToTimerPresentationMode(ActiveTimerPresentationMode presentationMode)
    {
        return presentationMode == ActiveTimerPresentationMode.Status
            ? TimerPresentationMode.Status
            : TimerPresentationMode.Input;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseSettingsCommandCanExecuteChanged()
    {
        this.ToggleNotificationsCommand.RaiseCanExecuteChanged();
        this.ToggleAudioAlertsCommand.RaiseCanExecuteChanged();
        this.ToggleAlwaysOnTopCommand.RaiseCanExecuteChanged();
        this.ToggleShowProgressInTaskbarCommand.RaiseCanExecuteChanged();
        this.ToggleShowInNotificationAreaCommand.RaiseCanExecuteChanged();
        this.HideToNotificationAreaCommand.RaiseCanExecuteChanged();
        this.TogglePopUpWhenExpiredCommand.RaiseCanExecuteChanged();
        this.TogglePromptOnExitCommand.RaiseCanExecuteChanged();
        this.ToggleReverseProgressBarCommand.RaiseCanExecuteChanged();
        this.ToggleShowTimeElapsedCommand.RaiseCanExecuteChanged();
        this.ToggleLoopTimerCommand.RaiseCanExecuteChanged();
        this.ToggleLoopSoundCommand.RaiseCanExecuteChanged();
        this.ToggleCloseWhenExpiredCommand.RaiseCanExecuteChanged();
        this.ToggleLockInterfaceCommand.RaiseCanExecuteChanged();
        this.ToggleDoNotKeepComputerAwakeCommand.RaiseCanExecuteChanged();
        this.ToggleShutDownWhenExpiredCommand.RaiseCanExecuteChanged();
        this.ToggleRestoreActiveSessionOnStartupCommand.RaiseCanExecuteChanged();
        this.ToggleOpenSavedTimersOnStartupCommand.RaiseCanExecuteChanged();
    }

    private void PublishSettingsChange(bool previous, bool next, string propertyName)
    {
        if (previous != next)
        {
            this.OnPropertyChanged(propertyName);
        }
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public static NoOpNotificationService Instance { get; } = new();

        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpAudioAlertService : IAudioAlertService
    {
        public static NoOpAudioAlertService Instance { get; } = new();

        public Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }
    }

    private sealed class NoOpSystemPowerService : ISystemPowerService
    {
        public static NoOpSystemPowerService Instance { get; } = new();

        public bool IsShutdownSupported => false;

        public Task RequestShutdownAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSessionInhibitor : ISessionInhibitor
    {
        public static NoOpSessionInhibitor Instance { get; } = new();

        public ValueTask<IAsyncDisposable?> InhibitAsync(
            string reason,
            bool inhibitSuspend,
            bool inhibitIdle,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
    }

    private sealed class NoOpSettingsStore : ISettingsStore
    {
        public static NoOpSettingsStore Instance { get; } = new();

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<T?>(default);
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
