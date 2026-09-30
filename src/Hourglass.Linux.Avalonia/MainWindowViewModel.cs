using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Serialization;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    internal Task PendingSessionEffects => this.WaitForApplicationEffectsAsync();
    internal Task PendingExpiryEffects => this.WaitForApplicationEffectsAsync();
    public Task PendingCommands { get; private set; } = Task.CompletedTask;

    private const string ActiveSessionKey = "active-session";
    private const string CustomThemesKey = "custom-themes";

    private readonly IAudioAlertService audioAlertService;
    private readonly IHourglassClient client;
    private TimerSessionSnapshot sessionSnapshot = new(string.Empty, 0, TimerViewState.DefaultTimerInput, string.Empty, CountdownState.Stopped, new(), SessionActions.Start);
    private SessionSubscription? subscription;
    private int pendingCommands;
    private long? editRevision;
    private bool projectingSnapshot;
    private readonly Dictionary<long, long> acknowledgedRevisions = [];
    private readonly HourglassRuntime? runtime;
    private readonly bool removeSessionOnDispose;
    private bool isDisconnected;
    private readonly bool ownsRuntime;
    private readonly bool attachExistingSession;
    private WindowGeometrySnapshot? sessionGeometry;
    private bool isDisposed;
    private Task? disposal;
    private readonly object disposalGate = new();
    private Task pendingAudioPreview = Task.CompletedTask;
    private bool automaticTicksSuspended;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly bool persistActiveSessionDirectly;
    private readonly bool restoreActiveSessionOnLoad;
    private readonly bool statusIconCanRecoverHiddenWindow;
    private readonly bool statusIconSupported;
    private readonly ISystemPowerService systemPowerService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly Func<DateTime> wallClockNow;
    private CancellationTokenSource? audioPreviewCancellation;
    private Task pendingActiveSessionSave = Task.CompletedTask;
    private Task pendingCustomThemesSave = Task.CompletedTask;
    private Task pendingSavedTimersSave = Task.CompletedTask;
    private Task pendingSettingsSave = Task.CompletedTask;
    private long settingsSaveRevision;
    private string publishedWindowTitle = ApplicationStrings.ApplicationTitle;
    private CustomThemesDocument customThemes = CustomThemesDocument.Empty;
    private SavedTimersDocument savedTimers = SavedTimersDocument.Empty;
    private LinuxAppSettings settings = LinuxAppSettings.Default;
    private TimerViewState viewState = TimerViewState.Initial;

    public MainWindowViewModel()
        : this(
            new SystemMonotonicClock(),
            () => DateTime.Now,
            UnsupportedNotificationService.Instance,
            UnsupportedSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            UnsupportedAudioAlertService.Instance,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public MainWindowViewModel(IMonotonicClock clock, Func<DateTime> wallClockNow)
        : this(
            clock,
            wallClockNow,
            UnsupportedNotificationService.Instance,
            UnsupportedSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            UnsupportedAudioAlertService.Instance,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService)
        : this(
            clock,
            wallClockNow,
            notificationService,
            UnsupportedSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            UnsupportedAudioAlertService.Instance,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISettingsStore settingsStore)
        : this(
            clock,
            wallClockNow,
            notificationService,
            UnsupportedSessionInhibitor.Instance,
            settingsStore,
            UnsupportedAudioAlertService.Instance,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore)
        : this(
            clock,
            wallClockNow,
            notificationService,
            sessionInhibitor,
            settingsStore,
            UnsupportedAudioAlertService.Instance,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService,
        ISystemPowerService systemPowerService,
        bool statusIconSupported = false,
        bool statusIconCanRecoverHiddenWindow = false,
        string? sessionId = null,
        bool persistActiveSessionDirectly = true,
        bool restoreActiveSessionOnLoad = true)
        : this(
            clock,
            wallClockNow,
            notificationService,
            sessionInhibitor,
            settingsStore,
            audioAlertService,
            systemPowerService,
            statusIconSupported,
            statusIconCanRecoverHiddenWindow,
            sessionId,
            persistActiveSessionDirectly,
            restoreActiveSessionOnLoad,
            uiDispatcher: null)
    {
    }

    internal MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService,
        ISystemPowerService systemPowerService,
        bool statusIconSupported = false,
        bool statusIconCanRecoverHiddenWindow = false,
        string? sessionId = null,
        bool persistActiveSessionDirectly = true,
        bool restoreActiveSessionOnLoad = true,
        IUiDispatcher? uiDispatcher = null,
        IDiagnosticSink? diagnosticSink = null,
        HourglassRuntime? runtime = null,
        IHourglassClient? client = null,
        bool removeSessionOnDispose = true)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(sessionInhibitor);
        ArgumentNullException.ThrowIfNull(settingsStore);
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.systemPowerService = systemPowerService ?? throw new ArgumentNullException(nameof(systemPowerService));
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
        this.statusIconSupported = statusIconSupported;
        this.statusIconCanRecoverHiddenWindow = statusIconCanRecoverHiddenWindow;
        this.SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();
        this.persistActiveSessionDirectly = persistActiveSessionDirectly;
        this.restoreActiveSessionOnLoad = restoreActiveSessionOnLoad;
        this.removeSessionOnDispose = removeSessionOnDispose;
        this.ownsRuntime = runtime == null && client == null;
        this.attachExistingSession = (runtime != null || client != null) && sessionId != null;
        this.runtime = runtime ?? (client == null ? new HourglassRuntime(this.diagnosticSink, clock: clock, wallClockNow: wallClockNow,
            services: new SessionRuntimeServices(notificationService, audioAlertService, sessionInhibitor, systemPowerService,
                ApplicationStrings.ApplicationTitle, ApplicationStrings.StatusTimerComplete, ApplicationStrings.SessionInhibitionReason), settingsStore: settingsStore) : null);
        this.client = client ?? this.runtime ?? throw new InvalidOperationException(nameof(MainWindowViewModel));

        this.StartCommand = new RelayCommand(
            this.Start,
            () => !this.isDisconnected && this.pendingCommands == 0 && this.viewState.PresentationMode == TimerPresentationMode.Input);
        this.PauseResumeCommand = new RelayCommand(
            this.PauseOrResume,
            () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && (this.sessionSnapshot.Countdown.State is TimerState.Running or TimerState.Paused));
        this.ResetCommand = new RelayCommand(this.Reset, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.sessionSnapshot.Countdown.State != TimerState.Stopped);
        this.RestartCommand = new RelayCommand(this.Restart, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.viewState.IsRestartVisible);
        this.CancelEditCommand = new RelayCommand(
            this.CancelActiveTimerEdit,
            () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.viewState.IsCancelVisible);
        this.ToggleNotificationsCommand = new RelayCommand(this.ToggleNotifications, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleAudioAlertsCommand = new RelayCommand(this.ToggleAudioAlerts, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleAlwaysOnTopCommand = new RelayCommand(this.ToggleAlwaysOnTop, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleShowProgressInTaskbarCommand = new RelayCommand(this.ToggleShowProgressInTaskbar, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleShowInNotificationAreaCommand = new RelayCommand(
            this.ToggleShowInNotificationArea,
            () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.IsStatusIconSupported);
        this.HideToNotificationAreaCommand = new RelayCommand(
            this.RequestHideToNotificationArea,
            () => this.CanHideToNotificationArea);
        this.TogglePopUpWhenExpiredCommand = new RelayCommand(this.TogglePopUpWhenExpired, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.TogglePromptOnExitCommand = new RelayCommand(this.TogglePromptOnExit, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleReverseProgressBarCommand = new RelayCommand(this.ToggleReverseProgressBar, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleShowTimeElapsedCommand = new RelayCommand(this.ToggleShowTimeElapsed, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleLoopTimerCommand = new RelayCommand(this.ToggleLoopTimer, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleLoopSoundCommand = new RelayCommand(this.ToggleLoopSound, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleCloseWhenExpiredCommand = new RelayCommand(this.ToggleCloseWhenExpired, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleLockInterfaceCommand = new RelayCommand(this.ToggleLockInterface, () => !this.isDisconnected && this.pendingCommands == 0);
        this.ToggleDoNotKeepComputerAwakeCommand = new RelayCommand(this.ToggleDoNotKeepComputerAwake, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleShutDownWhenExpiredCommand = new RelayCommand(
            this.ToggleShutDownWhenExpired,
            () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.systemPowerService.IsShutdownSupported);
        this.ToggleRestoreActiveSessionOnStartupCommand = new RelayCommand(this.ToggleRestoreActiveSessionOnStartup, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.ToggleOpenSavedTimersOnStartupCommand = new RelayCommand(this.ToggleOpenSavedTimersOnStartup, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.SelectThemePreferenceCommand = new RelayCommand<string>(this.SelectThemePreference, value => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(value) && !this.IsTimerModificationLocked);
        this.SelectCustomThemeCommand = new RelayCommand<string>(this.SelectCustomTheme, id => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.DuplicateCustomThemeCommand = new RelayCommand<string>(this.DuplicateCustomTheme, id => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.DeleteCustomThemeCommand = new RelayCommand<string>(this.DeleteCustomTheme, id => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.SelectWindowTitleModeCommand = new RelayCommand<string>(this.SelectWindowTitleMode, value => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(value) && !this.IsTimerModificationLocked);
        this.SelectAudioAlertSoundCommand = new RelayCommand<string>(this.SelectAudioAlertSound, value => this.CanSelectAudioAlertSound(value));
        this.PreviewAudioAlertSoundCommand = new RelayCommand(this.PreviewAudioAlertSound, () => this.CanPreviewAudioAlertSound);
        this.StopAudioAlertPreviewCommand = new RelayCommand(this.StopAudioAlertPreview, () => this.IsAudioPreviewActive);
        this.NewTimerCommand = new RelayCommand(this.RequestNewTimer, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked);
        this.SelectRecentInputCommand = new RelayCommand<string>(this.SelectRecentInput, input => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(input) && !this.IsTimerModificationLocked);
        this.ClearRecentInputsCommand = new RelayCommand(this.ClearRecentInputs, () => this.RecentInputMenuItems.Length > 0 && !this.IsTimerModificationLocked);
        this.SaveCurrentTimerCommand = new RelayCommand(this.SaveCurrentTimer, () => !this.isDisconnected && this.pendingCommands == 0 && !this.IsTimerModificationLocked && this.CanSaveCurrentTimer);
        this.OpenSavedTimerCommand = new RelayCommand<string>(this.OpenSavedTimer, id => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.RemoveSavedTimerCommand = new RelayCommand<string>(this.RemoveSavedTimer, id => this.pendingCommands == 0 && !string.IsNullOrWhiteSpace(id) && !this.IsTimerModificationLocked);
        this.ClearSavedTimersCommand = new RelayCommand(this.ClearSavedTimers, () => this.SavedTimerMenuItems.Length > 0 && !this.IsTimerModificationLocked);
        this.OpenAllSavedTimersCommand = new RelayCommand(this.RequestOpenAllSavedTimers, () => this.SavedTimerMenuItems.Length > 0 && !this.IsTimerModificationLocked);

        this.RefreshDisplay();
        this.QueueOperation(this.InitializeSessionAsync);
    }

    public MainWindowViewModel(
        IMonotonicClock clock,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService)
        : this(
            clock,
            wallClockNow,
            notificationService,
            sessionInhibitor,
            settingsStore,
            audioAlertService,
            UnsupportedSystemPowerService.Instance,
            uiDispatcher: null)
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? WindowAttentionRequested;

    public event EventHandler? ExpiryVisualFeedbackRequested;

    public event EventHandler? ValidationFeedbackRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? HideToNotificationAreaRequested;

    public event EventHandler? ActiveSessionChanged;

    public event EventHandler? NewTimerRequested;

    public event EventHandler<OpenAllSavedTimersRequestedEventArgs>? OpenAllSavedTimersRequested;

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

    public RelayCommand<string> SelectThemePreferenceCommand { get; }

    public RelayCommand<string> SelectCustomThemeCommand { get; }

    public RelayCommand<string> DuplicateCustomThemeCommand { get; }

    public RelayCommand<string> DeleteCustomThemeCommand { get; }

    public RelayCommand<string> SelectWindowTitleModeCommand { get; }

    public RelayCommand<string> SelectAudioAlertSoundCommand { get; }

    public RelayCommand PreviewAudioAlertSoundCommand { get; }

    public RelayCommand StopAudioAlertPreviewCommand { get; }

    public RelayCommand NewTimerCommand { get; }

    public RelayCommand<string> SelectRecentInputCommand { get; }

    public RelayCommand ClearRecentInputsCommand { get; }

    public RelayCommand SaveCurrentTimerCommand { get; }

    public RelayCommand<string> OpenSavedTimerCommand { get; }

    public RelayCommand<string> RemoveSavedTimerCommand { get; }

    public RelayCommand ClearSavedTimersCommand { get; }

    public RelayCommand OpenAllSavedTimersCommand { get; }

    public string SessionId { get; }

    public string TimerInput
    {
        get => this.viewState.TimerInput;
        set
        {
            value ??= string.Empty;

            if (value != this.viewState.TimerInput)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = value, HasValidationError = false });
                this.RefreshDisplay(this.sessionSnapshot.Countdown.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText);
                this.OnPropertyChanged(nameof(this.CanSaveCurrentTimer));
                this.RaiseCommandCanExecuteChanged(this.SaveCurrentTimerCommand);
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

            bool expired = this.sessionSnapshot.Countdown.State == TimerState.Expired;
            this.ReplaceViewState(this.viewState with { TimerTitle = nextTitle }, nameof(this.TimerTitle));
            this.PublishWindowTitleIfChanged();

            if (expired)
            {
                string input = this.TimerInput;
                TimerDefaults options = TimerDefaults.FromSettings(this.settings);
                ApplicationPreferences preferences = ApplicationPreferences.FromSettings(this.settings);
                this.QueueOperation(async () =>
                {
                    if (!await this.ExecuteAndApplyAsync(new SessionCommand.Prepare(this.SessionId, input, nextTitle, options, preferences)).ConfigureAwait(false)) { return; }
                    await this.DispatchAsync(() =>
                    {
                        this.ReplaceViewState(this.viewState with { TimerInput = input, PresentationMode = TimerPresentationMode.Input, InputBeforeEdit = null, HasValidationError = false });
                        this.RefreshDisplay(TimerViewState.ReadyStatusText);
                        this.QueueActiveSessionSave();
                    }).ConfigureAwait(false);
                });
            }
            else { this.SubmitMetadataUpdate(title: nextTitle); }
            this.QueueActiveSessionSave();
        }
    }

    public string WindowTitle => WindowTitleFormatter.Format(
        this.settings.WindowTitleMode,
        ApplicationStrings.ApplicationTitle,
        this.TimerTitle,
        FormatOptionalTimerTime(this.sessionSnapshot.Countdown.TimeLeft),
        FormatOptionalTimerTime(this.sessionSnapshot.Countdown.TimeElapsed));

    public string RemainingTime => this.viewState.RemainingTime;

    public string StatusText => this.viewState.StatusText;

    public string TimerInputHelpText =>
        this.HasValidationError ? this.StatusText : ApplicationStrings.TimerInputDefaultHelpText;

    public string PauseResumeText => this.viewState.PauseResumeText;

    public bool IsInputEnabled => this.viewState.IsInputEnabled;

    public bool IsRunning => this.viewState.IsRunning;

    public TimerState State => this.viewState.State;

    public DateTime? EndTime => this.sessionSnapshot.Countdown.EndTime;

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

    public string AudioAlertSoundId => this.settings.AudioAlertSoundId;

    public bool IsNoSoundSelected => !this.settings.AudioAlertsEnabled
        || StringComparer.Ordinal.Equals(this.settings.AudioAlertSoundId, AudioAlertSoundIds.None);

    public bool IsLoudBeepSoundSelected => this.settings.AudioAlertsEnabled
        && StringComparer.Ordinal.Equals(this.settings.AudioAlertSoundId, AudioAlertSoundIds.LoudBeep);

    public bool IsNormalBeepSoundSelected => this.settings.AudioAlertsEnabled
        && StringComparer.Ordinal.Equals(this.settings.AudioAlertSoundId, AudioAlertSoundIds.NormalBeep);

    public bool IsQuietBeepSoundSelected => this.settings.AudioAlertsEnabled
        && StringComparer.Ordinal.Equals(this.settings.AudioAlertSoundId, AudioAlertSoundIds.QuietBeep);

    public bool CanPreviewAudioAlertSound =>
        !this.IsTimerModificationLocked
        && !this.IsNoSoundSelected
        && this.audioAlertService.IsSoundAvailable(this.settings.AudioAlertSoundId);

    public bool IsAudioPreviewActive => this.audioPreviewCancellation != null;

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

    public bool WakeFromSuspendEnabled => this.settings.WakeFromSuspendEnabled;

    public LinuxThemePreference ThemePreference => this.settings.ThemePreference;

    public string? CustomThemeId => this.settings.CustomThemeId;

    public bool IsSystemThemeSelected => this.settings.ThemePreference == LinuxThemePreference.System;

    public bool IsLightThemeSelected => this.settings.ThemePreference == LinuxThemePreference.Light;

    public bool IsDarkThemeSelected => this.settings.ThemePreference == LinuxThemePreference.Dark;

    public bool IsCustomThemeSelected => this.settings.ThemePreference == LinuxThemePreference.Custom;

    public CustomThemeDefinition? CurrentCustomTheme => this.customThemes.Find(this.settings.CustomThemeId);

    public bool CanExportCustomTheme => this.CurrentCustomTheme != null;

    public WindowTitleMode WindowTitleMode => this.settings.WindowTitleMode;

    public bool IsApplicationNameTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.ApplicationName;

    public bool IsTimeLeftTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimeLeft;

    public bool IsTimeElapsedTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimeElapsed;

    public bool IsTimerTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimerTitle;

    public bool IsTimeLeftPlusTimerTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimeLeftPlusTimerTitle;

    public bool IsTimeElapsedPlusTimerTitleModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimeElapsedPlusTimerTitle;

    public bool IsTimerTitlePlusTimeLeftModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimerTitlePlusTimeLeft;

    public bool IsTimerTitlePlusTimeElapsedModeSelected => this.settings.WindowTitleMode == WindowTitleMode.TimerTitlePlusTimeElapsed;

    public RecentInputMenuItem[] RecentInputMenuItems =>
        this.settings.RecentTimerInputs.Select(input => new RecentInputMenuItem(input)).ToArray();

    public SavedTimerMenuItem[] SavedTimerMenuItems =>
        this.savedTimers.Timers.Select(timer => new SavedTimerMenuItem(timer.Id, timer.Header)).ToArray();

    public CustomThemeMenuItem[] CustomThemeMenuItems =>
        this.customThemes.Themes
            .Select(theme => new CustomThemeMenuItem(
                theme.Id,
                theme.Name,
                this.settings.ThemePreference == LinuxThemePreference.Custom
                    && StringComparer.Ordinal.Equals(this.settings.CustomThemeId, theme.Id)))
            .ToArray();

    public bool CanSaveCurrentTimer =>
        TimerStart.FromString(this.TimerInput) is { IsValid: true };

    public bool IsTimerModificationLocked => this.isDisconnected ||
        this.settings.LockInterface && (this.sessionSnapshot.Countdown.State is TimerState.Running or TimerState.Paused);

    public bool CanModifyCustomThemes => !this.IsTimerModificationLocked;

    public bool ShouldPromptOnExit =>
        this.settings.PromptOnExit && (this.sessionSnapshot.Countdown.State is TimerState.Running or TimerState.Paused);

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

    internal Task PendingSettingsSave => this.WaitForSettingsAsync();

    private async Task WaitForSettingsAsync()
    {
        while (true)
        {
            Task commands = this.PendingCommands;
            await commands.ConfigureAwait(false);
            await this.DrainNotificationsAsync().ConfigureAwait(false);
            await Task.WhenAll(this.pendingSettingsSave, this.pendingCustomThemesSave, this.pendingSavedTimersSave, this.pendingActiveSessionSave).ConfigureAwait(false);
            if (ReferenceEquals(commands, this.PendingCommands)) { return; }
        }
    }

    internal DesktopProgressRequest DesktopProgressRequest =>
        DesktopProgressProjection.FromViewState(this.viewState, this.settings.ShowProgressInTaskbar);

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await this.PendingCommands;
        ApplicationResult<ApplicationDataSnapshot> loadedResult = await this.client.GetApplicationDataAsync(cancellationToken);
        if (loadedResult is not ApplicationResult<ApplicationDataSnapshot>.Success loadedData) { throw new InvalidOperationException(nameof(LoadSettingsAsync)); }
        ApplicationDataSnapshot loaded = loadedData.Value;
        LinuxAppSettings loadedSettings = loaded.Settings;
        SavedTimersDocument loadedSavedTimers = loaded.SavedTimers;
        CustomThemesDocument loadedCustomThemes = loaded.CustomThemes;

        if (this.attachExistingSession)
        {
            await this.PendingCommands.ConfigureAwait(false);
            loadedSettings = new LinuxSettingsSnapshot(loadedSettings.RecentTimerInputs, this.sessionSnapshot.Preferences, this.sessionSnapshot.Options).ToSettings();
        }
        this.ReplaceCustomThemes(loadedCustomThemes, save: false);
        this.ReplaceSettings(NormalizeThemeSelection(loadedSettings, loadedCustomThemes), save: false);
        this.ReplaceSavedTimers(loadedSavedTimers, save: false);

        if (this.restoreActiveSessionOnLoad
            && loadedSettings.RestoreActiveSessionOnStartup
            && await this.TryRestoreActiveSessionAsync(cancellationToken))
        {
            await this.PendingCommands.ConfigureAwait(false);
            return;
        }

        await this.PendingCommands.ConfigureAwait(false);
        await this.DispatchAsync(() =>
        {
            if (!this.attachExistingSession && this.sessionSnapshot.Countdown.State == TimerState.Stopped)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = loadedSettings.GetInitialTimerInput(TimerViewState.DefaultTimerInput) });
                this.RefreshDisplay(TimerViewState.ReadyStatusText);
            }
        }).ConfigureAwait(false);
    }

    private async Task<bool> TryRestoreActiveSessionAsync(CancellationToken cancellationToken)
    {
        ActiveTimerSessionSnapshot? snapshot = await (this.runtime ?? throw new InvalidOperationException(nameof(MainWindowViewModel))).LoadLegacyRestorationAsync(cancellationToken);
        if (snapshot == null) { return false; }
        this.RestoreActiveSessionSnapshot(snapshot);
        return true;
    }

    private void RestoreActiveSessionSnapshot(ActiveTimerSessionSnapshot snapshot, bool restoredExpiry = false)
    {
        if (snapshot.HasOptions)
        {
            this.ReplaceSettings(NormalizeThemeSelection(snapshot.Options.ApplyTo(this.settings), this.customThemes), save: false);
        }
        ApplicationPreferences preferences = ApplicationPreferences.FromSettings(this.settings);
        this.QueueOperation(async () =>
        {
            ApplicationResult<TimerSessionSnapshot> result = this.attachExistingSession
                ? await this.client.GetSessionAsync(this.SessionId).ConfigureAwait(false)
                : await (this.runtime ?? throw new InvalidOperationException(nameof(MainWindowViewModel))).RestoreSessionAsync(this.SessionId, snapshot, preferences).ConfigureAwait(false);
            await this.ApplyResultAsync(result).ConfigureAwait(false);
            await this.DispatchAsync(() =>
            {
                TimerPresentationMode presentation = snapshot.CountdownState.State == TimerState.Expired
                    ? TimerPresentationMode.Status : ToTimerPresentationMode(snapshot.PresentationMode);
                string input = string.IsNullOrWhiteSpace(snapshot.TimerInput) ? TimerViewState.DefaultTimerInput : snapshot.TimerInput;
                bool editing = presentation == TimerPresentationMode.Input && this.sessionSnapshot.Countdown.State is TimerState.Running or TimerState.Paused;
                this.editRevision = editing ? this.sessionSnapshot.Revision : null;
                this.ReplaceViewState(this.viewState with { TimerInput = input, TimerTitle = snapshot.TimerTitle, PresentationMode = presentation, InputBeforeEdit = editing ? snapshot.TimerStartInput : null, HasValidationError = false });
                this.RefreshDisplay(hasValidationError: false);
                if (this.attachExistingSession && restoredExpiry)
                {
                    PublishSafely(this.ExpiryVisualFeedbackRequested);
                    if (this.sessionSnapshot.Preferences.PopUpWhenExpired) { PublishSafely(this.WindowAttentionRequested); }
                }
            }).ConfigureAwait(false);
        });
    }

    public void Dispose() => _ = this.ObserveDisposalAsync();

    private async Task ObserveDisposalAsync()
    {
        try { await this.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* DisposeAsync already recorded the failure. */ }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (this.disposalGate)
        {
            if (this.disposal != null)
            {
                return new ValueTask(this.disposal);
            }

            this.isDisposed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.disposal = completion.Task;
        }

        _ = CompleteAsync();
        return new ValueTask(completion.Task);

        async Task CompleteAsync()
        {
            try
            {
                await this.DisposeCoreAsync().ConfigureAwait(false);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                this.RecordBestEffort("session-effects", "dispose", nameof(MainWindowViewModel), "Session cleanup failed.", exception);
                completion.SetException(exception);
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        await this.PendingCommands.ConfigureAwait(false);
        this.subscription?.Dispose();
        Task removal = this.removeSessionOnDispose ? this.client.CloseSessionAsync(this.SessionId) : Task.CompletedTask;
        Task cancelPreview = this.StopAudioPreviewAsync();
        if (this.runtime != null) { await this.runtime.DrainAsync(Task.WhenAll(cancelPreview, this.pendingAudioPreview), "audio-preview").ConfigureAwait(false); }
        else { await Task.WhenAll(cancelPreview, this.pendingAudioPreview).ConfigureAwait(false); }
        await removal.ConfigureAwait(false);
        if (this.ownsRuntime && this.runtime != null)
        {
            await this.runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal ActiveTimerSessionDocument CreateActiveSessionDocument(WindowGeometrySnapshot? windowGeometry = null)
    {
        return ActiveTimerSessionSnapshot.FromState(
            this.TimerInput,
            this.TimerTitle ?? string.Empty,
            ToActiveTimerPresentationMode(this.viewState.PresentationMode),
            this.sessionSnapshot.Countdown,
            this.wallClockNow(),
            SavedTimerOptions.FromSettings(this.settings),
            windowGeometry)
            .ToDocument();
    }

    internal bool RestoreActiveSession(ActiveTimerSessionDocument session, bool restoredExpiry = false)
    {
        ArgumentNullException.ThrowIfNull(session);

        ActiveTimerSessionSnapshot? snapshot = ActiveTimerSessionSnapshot.FromDocument(
            session,
            this.wallClockNow(),
            TimeSpan.Zero);
        if (snapshot == null)
        {
            return false;
        }

        this.RestoreActiveSessionSnapshot(snapshot, restoredExpiry);
        return true;
    }

    public async Task AdvanceAsync()
    {
        await this.PendingCommands.ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        if (this.runtime != null) { await this.runtime.TickAsync().ConfigureAwait(false); }
        await this.DrainNotificationsAsync().ConfigureAwait(false);
        await this.ApplyResultAsync(await this.client.GetSessionAsync(this.SessionId).ConfigureAwait(false)).ConfigureAwait(false);
    }

    internal void StartRuntimeScheduler() { if (this.ownsRuntime) { this.runtime?.StartScheduler(); } }
    internal Task TickRuntimeAsync() => this.AdvanceAsync();
    internal void SuspendAutomaticTicks()
    {
        this.automaticTicksSuspended = true;
        this.runtime?.SuspendTicks(this.SessionId);
    }

    internal bool TryEnterInputModeFromExpired()
    {
        if (this.sessionSnapshot.Countdown.State != TimerState.Expired)
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

        if (this.sessionSnapshot.Countdown.State == TimerState.Expired)
        {
            return this.TryEnterInputModeFromExpired();
        }

        if (this.sessionSnapshot.Countdown.State is not (TimerState.Running or TimerState.Paused))
        {
            return false;
        }

        this.editRevision = this.sessionSnapshot.Revision;
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
        string input = this.TimerInput;
        string title = this.TimerTitle ?? string.Empty;
        long revision = this.editRevision ?? this.sessionSnapshot.Revision;
        this.QueueOperation(async () =>
        {
            SessionCommand command = this.sessionSnapshot.Countdown.State is TimerState.Running or TimerState.Paused
                ? new SessionCommand.Update(this.SessionId, this.ResolveAcknowledgedRevision(revision), input, title)
                : new SessionCommand.Start(this.SessionId, input, title, TimerDefaults.FromSettings(this.settings));
            if (!await this.ExecuteAndApplyAsync(command).ConfigureAwait(false)) { return; }
            ApplicationResult<LinuxAppSettings> recentSettings = await this.client.GetSettingsAsync().ConfigureAwait(false);
            await this.DispatchAsync(() =>
            {
                this.editRevision = null;
                this.ReplaceViewState(this.viewState with { PresentationMode = TimerPresentationMode.Status, InputBeforeEdit = null, TimerInput = input });
                this.RefreshDisplay(hasValidationError: false);
                if (recentSettings is ApplicationResult<LinuxAppSettings>.Success saved)
                {
                    this.ReplaceSettings(new LinuxSettingsSnapshot(saved.Value.RecentTimerInputs,
                        ApplicationPreferences.FromSettings(this.settings), TimerDefaults.FromSettings(this.settings)).ToSettings(), save: false);
                }
                this.QueueActiveSessionSave();
            }).ConfigureAwait(false);
        });
    }

    private void PauseOrResume() => this.SubmitLifecycle(this.sessionSnapshot.Countdown.State == TimerState.Running
        ? new SessionCommand.Pause(this.SessionId) : new SessionCommand.Resume(this.SessionId));

    private void Reset() => this.StopAndShowInput(this.TimerInput);

    private void Restart() => this.SubmitLifecycle(new SessionCommand.Restart(this.SessionId));

    private void SubmitLifecycle(SessionCommand command) => this.QueueOperation(async () =>
    {
        if (!await this.ExecuteAndApplyAsync(command).ConfigureAwait(false)) { return; }
        await this.DispatchAsync(() =>
        {
            this.editRevision = null;
            this.ReplaceViewState(this.viewState with { PresentationMode = TimerPresentationMode.Status, InputBeforeEdit = null, HasValidationError = false });
            this.RefreshDisplay();
            this.QueueActiveSessionSave();
        }).ConfigureAwait(false);
    });

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
        this.QueueOperation(async () =>
        {
            if (!await this.ExecuteAndApplyAsync(new SessionCommand.Stop(this.SessionId)).ConfigureAwait(false)) { return; }
            await this.DispatchAsync(() =>
            {
                this.editRevision = null;
                this.ReplaceViewState(this.viewState with { TimerInput = timerInput, PresentationMode = TimerPresentationMode.Input, InputBeforeEdit = null, HasValidationError = false });
                this.RefreshDisplay(TimerViewState.ReadyStatusText);
                if (this.settings.LockInterface) { this.ReplaceSettings(this.settings with { LockInterface = false }, save: true); }
                this.QueueActiveSessionSave();
            }).ConfigureAwait(false);
        });
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
            _ = this.StopAudioPreviewAsync();
        }

        string nextSoundId = this.settings.AudioAlertsEnabled
            ? AudioAlertSoundIds.None
            : BuiltInAudioAlertSounds.Default.Id;
        this.ReplaceSettings(
            this.settings with
            {
                AudioAlertsEnabled = !this.settings.AudioAlertsEnabled,
                AudioAlertSoundId = nextSoundId
            },
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
        if (this.IsTimerModificationLocked)
        {
            this.QueueOperation(async () => { await this.ExecuteAndApplyAsync(new SessionCommand.Unlock(this.SessionId)).ConfigureAwait(false); });
            return;
        }
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

    private void SelectThemePreference(string? value)
    {
        if (!Enum.TryParse(value, ignoreCase: false, out LinuxThemePreference themePreference))
        {
            return;
        }

        if (themePreference == LinuxThemePreference.Custom)
        {
            return;
        }

        this.ReplaceSettings(this.settings with { ThemePreference = themePreference, CustomThemeId = null }, save: true);
    }

    private void SelectCustomTheme(string? id)
    {
        CustomThemeDefinition? theme = this.customThemes.Find(id);
        if (theme == null)
        {
            return;
        }

        this.ReplaceSettings(this.settings with
        {
            ThemePreference = LinuxThemePreference.Custom,
            CustomThemeId = theme.Id
        }, save: true);
    }

    public void SaveCustomTheme(CustomThemeDefinition theme, bool select)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (!this.CanModifyCustomThemes)
        {
            return;
        }

        if (!theme.IsValid)
        {
            return;
        }

        this.ReplaceCustomThemes(this.customThemes.AddOrReplace(theme), save: true);
        if (select)
        {
            this.SelectCustomTheme(theme.Id);
        }
    }

    public CustomThemeDefinition? FindCustomTheme(string? id)
    {
        return this.customThemes.Find(id);
    }

    private void DuplicateCustomTheme(string? id)
    {
        CustomThemeDefinition? theme = this.customThemes.Find(id);
        if (theme == null)
        {
            return;
        }

        this.SaveCustomTheme(theme.Duplicate(), select: true);
    }

    private void DeleteCustomTheme(string? id)
    {
        if (!this.CanModifyCustomThemes)
        {
            return;
        }

        CustomThemeDefinition? theme = this.customThemes.Find(id);
        if (theme == null)
        {
            return;
        }

        if (StringComparer.Ordinal.Equals(this.settings.CustomThemeId, theme.Id))
        {
            this.ReplaceSettings(this.settings with
            {
                ThemePreference = LinuxThemePreference.System,
                CustomThemeId = null
            }, save: true);
        }

        this.ReplaceCustomThemes(this.customThemes.Remove(theme.Id), save: true);
    }

    private void SelectWindowTitleMode(string? value)
    {
        if (!Enum.TryParse(value, ignoreCase: false, out WindowTitleMode windowTitleMode))
        {
            return;
        }

        this.ReplaceSettings(this.settings with { WindowTitleMode = windowTitleMode }, save: true);
    }

    private void SelectAudioAlertSound(string? value)
    {
        if (!this.CanSelectAudioAlertSound(value))
        {
            return;
        }

        string soundId = BuiltInAudioAlertSounds.NormalizeId(value);
        bool audioAlertsEnabled = !StringComparer.Ordinal.Equals(soundId, AudioAlertSoundIds.None);
        if (!audioAlertsEnabled)
        {
            _ = this.StopAudioPreviewAsync();
        }

        this.ReplaceSettings(
            this.settings with
            {
                AudioAlertsEnabled = audioAlertsEnabled,
                AudioAlertSoundId = soundId
            },
            save: true);
    }

    private bool CanSelectAudioAlertSound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || this.IsTimerModificationLocked)
        {
            return false;
        }

        if (!BuiltInAudioAlertSounds.TryGet(value, out AudioAlertSoundDefinition? sound))
        {
            return false;
        }

        return sound?.IsNone == true || (sound != null && this.audioAlertService.IsSoundAvailable(sound.Id));
    }

    private void PreviewAudioAlertSound()
    {
        if (!this.CanPreviewAudioAlertSound)
        {
            return;
        }

        Task preview = this.PreviewAudioAlertSoundAsync();
        this.pendingAudioPreview = this.pendingAudioPreview.IsCompleted ? preview : Task.WhenAll(this.pendingAudioPreview, preview);
    }

    private async Task PreviewAudioAlertSoundAsync()
    {
        await this.StopAudioPreviewAsync().ConfigureAwait(false);
        if (this.isDisposed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        this.audioPreviewCancellation = cancellation;
        this.OnPropertyChanged(nameof(this.IsAudioPreviewActive));
        this.RaiseCommandCanExecuteChanged(this.StopAudioAlertPreviewCommand);

        try
        {
            await using IAsyncDisposable? preview = await this.audioAlertService.PlayAlertAsync(this.settings.AudioAlertSoundId, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            if (ReferenceEquals(this.audioPreviewCancellation, cancellation))
            {
                this.audioPreviewCancellation = null;
                this.OnPropertyChanged(nameof(this.IsAudioPreviewActive));
                this.RaiseCommandCanExecuteChanged(this.StopAudioAlertPreviewCommand);
            }

            cancellation.Dispose();
        }
    }

    private void StopAudioAlertPreview()
    {
        _ = this.StopAudioPreviewAsync();
    }

    private void SelectRecentInput(string? timerInput)
    {
        if (string.IsNullOrWhiteSpace(timerInput))
        {
            return;
        }

        if (this.sessionSnapshot.Countdown.State == TimerState.Expired)
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
        this.RefreshDisplay(this.sessionSnapshot.Countdown.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText, hasValidationError: false);
        this.QueueActiveSessionSave();
    }

    private void ClearRecentInputs()
    {
        this.ReplaceSettings(this.settings.ClearRecentTimerInputs(), save: true);
    }

    private void RequestNewTimer()
    {
        PublishSafely(this.NewTimerRequested);
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

        this.ApplySavedTimer(savedTimer);
    }

    internal void ApplySavedTimer(SavedTimerDefinition savedTimer)
    {
        ArgumentNullException.ThrowIfNull(savedTimer);
        LinuxAppSettings previous = this.settings;
        LinuxAppSettings requested = NormalizeThemeSelection(savedTimer.Options.ApplyTo(this.settings), this.customThemes);
        this.QueueOperation(async () =>
        {
            if (!await this.ExecuteAndApplyAsync(new SessionCommand.Prepare(this.SessionId, savedTimer.TimerInput, savedTimer.TimerTitle,
                TimerDefaults.FromSettings(requested), ApplicationPreferences.FromSettings(requested))).ConfigureAwait(false)) { return; }
            await this.DispatchAsync(() =>
            {
                this.ReplaceSettings(requested, save: false);
                this.QueueSettingsSave(previous, requested);
                this.ReplaceViewState(this.viewState with { TimerInput = savedTimer.TimerInput, TimerTitle = savedTimer.TimerTitle, PresentationMode = TimerPresentationMode.Input, InputBeforeEdit = null, HasValidationError = false });
                this.RefreshDisplay(TimerViewState.ReadyStatusText, hasValidationError: false);
                this.QueueActiveSessionSave();
            }).ConfigureAwait(false);
        });
    }

    internal void ApplyLaunchTimerRequest(string timerInput, string? timerTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerInput);
        this.ReplaceViewState(this.viewState with { TimerInput = timerInput, TimerTitle = timerTitle ?? string.Empty, PresentationMode = TimerPresentationMode.Input, InputBeforeEdit = null, HasValidationError = false });
        this.Start();
    }

    private void RequestOpenAllSavedTimers()
    {
        PublishOpenAllSavedTimersRequested(this.savedTimers);
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
        lock (this.presentationGate)
        {

            explicitStatus ??= (hasValidationError ?? this.viewState.HasValidationError) ? this.viewState.StatusText : null;
            bool wasTimerModificationLocked = this.viewState.IsLocked;
            TimerViewState nextViewState = TimerViewState.FromTimerState(
                this.TimerInput,
                this.sessionSnapshot.Countdown,
                this.TimerTitle ?? string.Empty,
                explicitStatus,
                this.viewState.PresentationMode,
                this.viewState.InputBeforeEdit,
                hasValidationError ?? this.viewState.HasValidationError,
                this.settings.ShowTimeElapsed,
                this.settings.ReverseProgressBar,
                this.IsTimerModificationLocked);

            this.ReplaceViewState(nextViewState);
            this.OnPropertyChanged(nameof(this.TimerInput));
            this.OnPropertyChanged(nameof(this.RemainingTime));
            this.OnPropertyChanged(nameof(this.StatusText));
            this.OnPropertyChanged(nameof(this.TimerInputHelpText));
            this.OnPropertyChanged(nameof(this.PauseResumeText));
            this.OnPropertyChanged(nameof(this.IsInputEnabled));
            this.OnPropertyChanged(nameof(this.IsRunning));
            this.OnPropertyChanged(nameof(this.State));
            this.OnPropertyChanged(nameof(this.ShouldPromptOnExit));
            this.OnPropertyChanged(nameof(this.IsTimerModificationLocked));
            if (wasTimerModificationLocked != nextViewState.IsLocked)
            {
                this.OnPropertyChanged(nameof(this.CanModifyCustomThemes));
            }

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
            this.PublishWindowTitleIfChanged();
            this.OnPropertyChanged(nameof(this.HasValidationError));
            this.OnPropertyChanged(nameof(this.HasCompletionEmphasis));
            this.OnPropertyChanged(nameof(this.CanSaveCurrentTimer));
            this.RefreshCommandAvailability();
        }
    }

    private void RefreshCommandAvailability()
    {
        this.RaiseCommandCanExecuteChanged(this.StartCommand);
        this.RaiseCommandCanExecuteChanged(this.PauseResumeCommand);
        this.RaiseCommandCanExecuteChanged(this.ResetCommand);
        this.RaiseCommandCanExecuteChanged(this.RestartCommand);
        this.RaiseCommandCanExecuteChanged(this.CancelEditCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.DuplicateCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.DeleteCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectRecentInputCommand);
        this.RaiseCommandCanExecuteChanged(this.ClearRecentInputsCommand);
        this.RaiseCommandCanExecuteChanged(this.SaveCurrentTimerCommand);
        this.RaiseCommandCanExecuteChanged(this.OpenSavedTimerCommand);
        this.RaiseCommandCanExecuteChanged(this.RemoveSavedTimerCommand);
        this.RaiseCommandCanExecuteChanged(this.ClearSavedTimersCommand);
        this.RaiseCommandCanExecuteChanged(this.OpenAllSavedTimersCommand);
        this.RaiseSettingsCommandCanExecuteChanged();
    }

    private void ReplaceViewState(TimerViewState next, string? changedPropertyName = null)
    {
        lock (this.presentationGate)
        {

            if (this.viewState == next)
            {
                return;
            }

            this.viewState = next;
            this.OnPropertyChanged(changedPropertyName);
        }
    }

    private void ReplaceSettings(LinuxAppSettings next, bool save)
    {
        lock (this.presentationGate)
        {

            ArgumentNullException.ThrowIfNull(next);
            next = NormalizeThemeSelection(next, this.customThemes);

            LinuxAppSettings previous = this.settings;
            if (previous == next)
            {
                return;
            }

            this.settings = next;
            bool applicationChange = !this.projectingSnapshot && (TimerDefaults.FromSettings(previous) != TimerDefaults.FromSettings(next)
                || ApplicationPreferences.FromSettings(previous) != ApplicationPreferences.FromSettings(next));
            if (applicationChange)
            {
                this.SubmitMetadataUpdate(options: TimerDefaults.FromSettings(next), preferences: ApplicationPreferences.FromSettings(next), previousSettings: save ? previous : null, requestedSettings: save ? next : null);
            }

            if (!previous.RecentTimerInputs.SequenceEqual(next.RecentTimerInputs, StringComparer.Ordinal))
            {
                this.OnPropertyChanged(nameof(this.RecentInputMenuItems));
                this.RaiseCommandCanExecuteChanged(this.ClearRecentInputsCommand);
            }

            if (previous.NotificationsEnabled != next.NotificationsEnabled)
            {
                this.OnPropertyChanged(nameof(this.NotificationsEnabled));
            }

            if (previous.AudioAlertsEnabled != next.AudioAlertsEnabled)
            {
                this.OnPropertyChanged(nameof(this.AudioAlertsEnabled));
                this.OnPropertyChanged(nameof(this.IsNoSoundSelected));
                this.OnPropertyChanged(nameof(this.IsLoudBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.IsNormalBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.IsQuietBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.CanPreviewAudioAlertSound));
                this.RaiseCommandCanExecuteChanged(this.PreviewAudioAlertSoundCommand);
            }

            if (!StringComparer.Ordinal.Equals(previous.AudioAlertSoundId, next.AudioAlertSoundId))
            {
                this.OnPropertyChanged(nameof(this.AudioAlertSoundId));
                this.OnPropertyChanged(nameof(this.IsNoSoundSelected));
                this.OnPropertyChanged(nameof(this.IsLoudBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.IsNormalBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.IsQuietBeepSoundSelected));
                this.OnPropertyChanged(nameof(this.CanPreviewAudioAlertSound));
                this.RaiseCommandCanExecuteChanged(this.PreviewAudioAlertSoundCommand);
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

            if (previous.ThemePreference != next.ThemePreference)
            {
                this.OnPropertyChanged(nameof(this.ThemePreference));
                this.OnPropertyChanged(nameof(this.CustomThemeId));
                this.OnPropertyChanged(nameof(this.IsSystemThemeSelected));
                this.OnPropertyChanged(nameof(this.IsLightThemeSelected));
                this.OnPropertyChanged(nameof(this.IsDarkThemeSelected));
                this.OnPropertyChanged(nameof(this.IsCustomThemeSelected));
                this.OnPropertyChanged(nameof(this.CurrentCustomTheme));
                this.OnPropertyChanged(nameof(this.CanExportCustomTheme));
                this.OnPropertyChanged(nameof(this.CustomThemeMenuItems));
            }

            if (!StringComparer.Ordinal.Equals(previous.CustomThemeId, next.CustomThemeId))
            {
                this.OnPropertyChanged(nameof(this.CustomThemeId));
                this.OnPropertyChanged(nameof(this.CurrentCustomTheme));
                this.OnPropertyChanged(nameof(this.CanExportCustomTheme));
                this.OnPropertyChanged(nameof(this.CustomThemeMenuItems));
            }

            if (previous.WindowTitleMode != next.WindowTitleMode)
            {
                this.OnPropertyChanged(nameof(this.WindowTitleMode));
                this.OnPropertyChanged(nameof(this.IsApplicationNameTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimeLeftTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimeElapsedTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimerTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimeLeftPlusTimerTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimeElapsedPlusTimerTitleModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimerTitlePlusTimeLeftModeSelected));
                this.OnPropertyChanged(nameof(this.IsTimerTitlePlusTimeElapsedModeSelected));
                this.PublishWindowTitleIfChanged();
            }

            if (previous.LockInterface != next.LockInterface)
            {
                this.OnPropertyChanged(nameof(this.LockInterface));
                this.OnPropertyChanged(nameof(this.IsTimerModificationLocked));
                this.OnPropertyChanged(nameof(this.CanModifyCustomThemes));
                this.OnPropertyChanged(nameof(this.CanHideToNotificationArea));
                this.OnPropertyChanged(nameof(this.StatusIconMenuState));
            }

            if (save)
            {
                if (!applicationChange) { this.QueueSettingsSave(previous, next); }
                this.QueueActiveSessionSave();
            }

            this.RaiseSettingsCommandCanExecuteChanged();
        }
    }

    private void ReplaceCustomThemes(CustomThemesDocument next, bool save)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (this.customThemes == next)
        {
            return;
        }

        CustomThemesDocument previous = this.customThemes;
        this.customThemes = next;
        this.OnPropertyChanged(nameof(this.CustomThemeMenuItems));
        this.OnPropertyChanged(nameof(this.CurrentCustomTheme));
        this.OnPropertyChanged(nameof(this.CanExportCustomTheme));

        if (this.settings.ThemePreference == LinuxThemePreference.Custom
            && this.customThemes.Find(this.settings.CustomThemeId) == null)
        {
            this.ReplaceSettings(this.settings with
            {
                ThemePreference = LinuxThemePreference.System,
                CustomThemeId = null
            }, save: true);
        }

        if (save)
        {
            this.QueueCustomThemesSave(previous, this.customThemes);
        }
    }

    private void ReplaceSavedTimers(SavedTimersDocument next, bool save)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (this.savedTimers == next)
        {
            return;
        }

        SavedTimersDocument previous = this.savedTimers;
        this.savedTimers = next;
        this.OnPropertyChanged(nameof(this.SavedTimerMenuItems));
        this.RaiseCommandCanExecuteChanged(this.ClearSavedTimersCommand);

        if (save)
        {
            this.QueueSavedTimersSave(previous, next);
        }
    }

    private void PublishWindowTitleIfChanged()
    {
        string currentWindowTitle = this.WindowTitle;
        if (StringComparer.Ordinal.Equals(this.publishedWindowTitle, currentWindowTitle))
        {
            return;
        }

        this.publishedWindowTitle = currentWindowTitle;
        this.OnPropertyChanged(nameof(this.WindowTitle));
        this.OnPropertyChanged(nameof(this.StatusIconMenuState));
    }

    private void ShowValidationError()
    {
        this.RefreshDisplay(ApplicationStrings.StatusInvalidTimer, hasValidationError: true);
        PublishSafely(this.ValidationFeedbackRequested);
    }

    private void PublishSafely(EventHandler? handlers)
    {
        if (handlers == null)
        {
            return;
        }

        if (!this.uiDispatcher.CheckAccess())
        {
            this.uiDispatcher.Post(() => this.PublishSafely(handlers));
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                this.RecordBestEffort("event", "publish", "handler", "Event handler failed.", exception);
            }
        }
    }

    private void PublishOpenAllSavedTimersRequested(SavedTimersDocument savedTimersSnapshot)
    {
        EventHandler<OpenAllSavedTimersRequestedEventArgs>? handlers = this.OpenAllSavedTimersRequested;
        if (handlers == null)
        {
            return;
        }

        if (!this.uiDispatcher.CheckAccess())
        {
            this.uiDispatcher.Post(() => this.PublishOpenAllSavedTimersRequested(savedTimersSnapshot));
            return;
        }

        var args = new OpenAllSavedTimersRequestedEventArgs(savedTimersSnapshot);
        foreach (EventHandler<OpenAllSavedTimersRequestedEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<OpenAllSavedTimersRequestedEventArgs>>())
        {
            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception exception)
            {
                this.RecordBestEffort("event", "publish-open-all-saved-timers", "handler", "Event handler failed.", exception);
            }
        }
    }

    private async Task StopAudioPreviewAsync()
    {
        CancellationTokenSource? cancellation = this.audioPreviewCancellation;
        if (cancellation == null)
        {
            return;
        }

        this.audioPreviewCancellation = null;
        this.OnPropertyChanged(nameof(this.IsAudioPreviewActive));
        this.RaiseCommandCanExecuteChanged(this.StopAudioAlertPreviewCommand);

        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            this.RecordBestEffort("audio-preview", "cancel", this.audioAlertService.GetType().Name,
                "Audio preview cancellation failed.", exception);
        }
    }

    private void QueueSettingsSave(LinuxAppSettings previousSettings, LinuxAppSettings requestedSettings, long? saveRevision = null)
    {
        this.pendingSettingsSave = this.SaveSettingsAfterAsync(
            this.pendingSettingsSave,
            previousSettings,
            requestedSettings,
            saveRevision ?? ++this.settingsSaveRevision);
    }

    private void QueueSavedTimersSave(SavedTimersDocument previousSavedTimers, SavedTimersDocument requestedSavedTimers)
    {
        this.pendingSavedTimersSave = this.SaveSavedTimersAfterAsync(
            this.pendingSavedTimersSave,
            previousSavedTimers,
            requestedSavedTimers);
    }

    private void QueueCustomThemesSave(CustomThemesDocument previousCustomThemes, CustomThemesDocument requestedCustomThemes)
    {
        this.pendingCustomThemesSave = this.SaveCustomThemesAfterAsync(
            this.pendingCustomThemesSave,
            previousCustomThemes,
            requestedCustomThemes);
    }

    internal void UpdateSessionGeometry(WindowGeometrySnapshot? geometry)
    {
        this.sessionGeometry = geometry;
        this.QueueActiveSessionSave();
    }

    private void QueueActiveSessionSave()
    {
        SessionPresentation presentation = new(this.TimerInput, ToActiveTimerPresentationMode(this.viewState.PresentationMode), this.sessionGeometry);
        Task previous = this.pendingActiveSessionSave;
        this.pendingActiveSessionSave = SaveAsync();
        PublishSafely(this.ActiveSessionChanged);
        async Task SaveAsync()
        {
            try
            {
                await previous.ConfigureAwait(false);
                await this.PendingCommands.ConfigureAwait(false);
                await this.client.UpdatePresentationAsync(this.SessionId, presentation).ConfigureAwait(false);
                if (this.persistActiveSessionDirectly)
                {
                    await (this.runtime ?? throw new InvalidOperationException(nameof(MainWindowViewModel))).SaveLegacySessionAsync(this.SessionId).ConfigureAwait(false);
                }
            }
            catch (Exception exception) { this.RecordDataRecovery("save", ActiveSessionKey, "Active session save failed.", exception); }
        }
    }

    private async Task SaveSettingsAfterAsync(
        Task previousSave,
        LinuxAppSettings previousSettings,
        LinuxAppSettings requestedSettings,
        long saveRevision)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", "app", "Previous settings save failed before a queued save.", exception);
        }

        try
        {
            ApplicationResult<LinuxAppSettings> result = await this.client.ChangeSettingsAsync(previousSettings, requestedSettings).ConfigureAwait(false);
            if (result is not ApplicationResult<LinuxAppSettings>.Success saved)
            {
                this.RecordDataRecovery("save", "app", "Settings save failed.", new IOException(nameof(SaveSettingsAfterAsync)));
                return;
            }
            LinuxAppSettings mergedSettings = saved.Value;
            await this.DispatchAsync(() => { if (saveRevision == this.settingsSaveRevision) { this.ReplaceSettings(mergedSettings, save: false); } })
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", "app", "Settings save failed.", exception);
        }
    }

    private async Task SaveSavedTimersAfterAsync(
        Task previousSave,
        SavedTimersDocument previousSavedTimers,
        SavedTimersDocument requestedSavedTimers)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", "saved-timers", "Previous saved timers save failed before a queued save.", exception);
        }

        try
        {
            ApplicationResult<bool> result = await this.client.SaveSavedTimersChangeAsync(previousSavedTimers, requestedSavedTimers).ConfigureAwait(false);
            if (result is ApplicationResult<bool>.Failure failed) { this.RecordDataRecovery("save", "saved-timers", failed.Error.Message, new IOException(failed.Error.Message)); }
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", "saved-timers", "Saved timers save failed.", exception);
        }
    }

    private async Task SaveCustomThemesAfterAsync(Task previousSave, CustomThemesDocument previous, CustomThemesDocument requested)
    {
        try { await previousSave.ConfigureAwait(false); } catch (Exception) { }
        try
        {
            ApplicationResult<bool> result = await this.client.ChangeThemesAsync(previous, requested).ConfigureAwait(false);
            if (result is ApplicationResult<bool>.Failure failed) { this.RecordDataRecovery("save", CustomThemesKey, failed.Error.Message, new IOException(failed.Error.Message)); }
        }
        catch (Exception exception) { this.RecordDataRecovery("save", CustomThemesKey, "Custom themes save failed.", exception); }
    }

    private static LinuxAppSettings NormalizeThemeSelection(LinuxAppSettings settings, CustomThemesDocument customThemes) =>
        HourglassRuntime.NormalizeThemeSelection(settings, customThemes);

    private static string? FormatOptionalTimerTime(TimeSpan? time)
    {
        return time.HasValue ? TimerViewState.FormatTimerTime(time.Value) : null;
    }

    private static T SelectChanged<T>(T previous, T requested, T latest)
        where T : struct, Enum
    {
        return EqualityComparer<T>.Default.Equals(previous, requested) ? latest : requested;
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
        void Publish()
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        if (this.uiDispatcher.CheckAccess())
        {
            Publish();
            return;
        }

        this.uiDispatcher.Post(Publish);
    }

    private void RaiseSettingsCommandCanExecuteChanged()
    {
        this.RaiseCommandCanExecuteChanged(this.ToggleNotificationsCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleAudioAlertsCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleAlwaysOnTopCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleShowProgressInTaskbarCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleShowInNotificationAreaCommand);
        this.RaiseCommandCanExecuteChanged(this.HideToNotificationAreaCommand);
        this.RaiseCommandCanExecuteChanged(this.TogglePopUpWhenExpiredCommand);
        this.RaiseCommandCanExecuteChanged(this.TogglePromptOnExitCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleReverseProgressBarCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleShowTimeElapsedCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleLoopTimerCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleLoopSoundCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleCloseWhenExpiredCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleLockInterfaceCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleDoNotKeepComputerAwakeCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleShutDownWhenExpiredCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleRestoreActiveSessionOnStartupCommand);
        this.RaiseCommandCanExecuteChanged(this.ToggleOpenSavedTimersOnStartupCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectThemePreferenceCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.DuplicateCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.DeleteCustomThemeCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectWindowTitleModeCommand);
        this.RaiseCommandCanExecuteChanged(this.SelectAudioAlertSoundCommand);
        this.RaiseCommandCanExecuteChanged(this.PreviewAudioAlertSoundCommand);
        this.RaiseCommandCanExecuteChanged(this.StopAudioAlertPreviewCommand);
        this.RaiseCommandCanExecuteChanged(this.NewTimerCommand);
    }

    private void RaiseCommandCanExecuteChanged(RelayCommand command)
    {
        if (this.uiDispatcher.CheckAccess())
        {
            command.RaiseCanExecuteChanged();
            return;
        }

        this.uiDispatcher.Post(command.RaiseCanExecuteChanged);
    }

    private void RaiseCommandCanExecuteChanged<T>(RelayCommand<T> command)
    {
        if (this.uiDispatcher.CheckAccess())
        {
            command.RaiseCanExecuteChanged();
            return;
        }

        this.uiDispatcher.Post(command.RaiseCanExecuteChanged);
    }

    private void PublishSettingsChange(bool previous, bool next, string propertyName)
    {
        if (previous != next)
        {
            this.OnPropertyChanged(propertyName);
        }
    }

    private void RecordBestEffort(
        string category,
        string operation,
        string backend,
        string message,
        Exception exception)
    {
        this.RecordDiagnostic(
            DiagnosticFailureClass.BestEffort,
            category,
            operation,
            backend,
            message,
            exception);
    }

    private void RecordUserRequested(
        string category,
        string operation,
        string backend,
        string message,
        Exception exception)
    {
        this.RecordDiagnostic(
            DiagnosticFailureClass.UserRequested,
            category,
            operation,
            backend,
            message,
            exception);
    }

    private void RecordDataRecovery(
        string operation,
        string documentKey,
        string message,
        Exception exception)
    {
        this.RecordDiagnostic(
            DiagnosticFailureClass.DataRecovery,
            "settings",
            operation,
            documentKey,
            message,
            exception);
    }

    private void RecordDiagnostic(
        DiagnosticFailureClass failureClass,
        string category,
        string operation,
        string backend,
        string message,
        Exception exception)
    {
        this.diagnosticSink.TryRecord(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            failureClass,
            category,
            operation,
            backend,
            message,
            exception));
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

public sealed class OpenAllSavedTimersRequestedEventArgs(SavedTimersDocument savedTimers) : EventArgs
{
    public SavedTimersDocument SavedTimers { get; } = savedTimers ?? throw new ArgumentNullException(nameof(savedTimers));
}
