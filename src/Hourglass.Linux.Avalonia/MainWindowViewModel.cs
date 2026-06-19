using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string ApplicationTitle = "Hourglass";
    private const string InvalidTimerStatusText = "Enter a valid current timer.";
    private const string SessionInhibitionReason = "Hourglass timer is running";
    private const string NotificationBody = "Timer complete";
    private const string NotificationTitle = "Hourglass";
    private const string SettingsKey = "app";

    private readonly IAudioAlertService audioAlertService;
    private readonly CountdownEngine engine;
    private readonly INotificationService notificationService;
    private readonly ISessionInhibitor sessionInhibitor;
    private readonly ISettingsStore settingsStore;
    private readonly Func<DateTime> wallClockNow;
    private IAsyncDisposable? inhibitionLease;
    private Task pendingSettingsSave = Task.CompletedTask;
    private LinuxAppSettings settings = LinuxAppSettings.Default;
    private TimerViewState viewState = TimerViewState.Initial;

    public MainWindowViewModel()
        : this(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            NoOpNotificationService.Instance,
            NoOpSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            NoOpAudioAlertService.Instance)
    {
    }

    public MainWindowViewModel(CountdownEngine engine, Func<DateTime> wallClockNow)
        : this(
            engine,
            wallClockNow,
            NoOpNotificationService.Instance,
            NoOpSessionInhibitor.Instance,
            NoOpSettingsStore.Instance,
            NoOpAudioAlertService.Instance)
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
            NoOpAudioAlertService.Instance)
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
            NoOpAudioAlertService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore)
        : this(engine, wallClockNow, notificationService, sessionInhibitor, settingsStore, NoOpAudioAlertService.Instance)
    {
    }

    public MainWindowViewModel(
        CountdownEngine engine,
        Func<DateTime> wallClockNow,
        INotificationService notificationService,
        ISessionInhibitor sessionInhibitor,
        ISettingsStore settingsStore,
        IAudioAlertService audioAlertService)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.sessionInhibitor = sessionInhibitor ?? throw new ArgumentNullException(nameof(sessionInhibitor));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.engine.Expired += this.OnEngineExpired;

        this.StartCommand = new RelayCommand(this.Start, () => this.engine.State == TimerState.Stopped);
        this.PauseResumeCommand = new RelayCommand(
            this.PauseOrResume,
            () => this.engine.State is TimerState.Running or TimerState.Paused);
        this.ResetCommand = new RelayCommand(this.Reset, () => this.engine.State != TimerState.Stopped);
        this.ToggleNotificationsCommand = new RelayCommand(this.ToggleNotifications);
        this.ToggleAudioAlertsCommand = new RelayCommand(this.ToggleAudioAlerts);
        this.ToggleAlwaysOnTopCommand = new RelayCommand(this.ToggleAlwaysOnTop);

        this.RefreshDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand ToggleNotificationsCommand { get; }

    public RelayCommand ToggleAudioAlertsCommand { get; }

    public RelayCommand ToggleAlwaysOnTopCommand { get; }

    public string TimerInput
    {
        get => this.viewState.TimerInput;
        set
        {
            value ??= string.Empty;

            if (value != this.viewState.TimerInput)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = value });
                this.RefreshDisplay(this.engine.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText);
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
            this.ReplaceViewState(this.viewState with { TimerTitle = nextTitle }, nameof(this.TimerTitle));

            if (previousWindowTitle != this.WindowTitle)
            {
                this.OnPropertyChanged(nameof(this.WindowTitle));
            }
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

    public bool NotificationsEnabled => this.settings.NotificationsEnabled;

    public bool AudioAlertsEnabled => this.settings.AudioAlertsEnabled;

    public bool AlwaysOnTop => this.settings.AlwaysOnTop;

    internal Task PendingSettingsSave => this.pendingSettingsSave;

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        LinuxAppSettings loadedSettings;

        try
        {
            loadedSettings = await this.settingsStore.LoadAsync<LinuxAppSettings>(SettingsKey, cancellationToken)
                ?? LinuxAppSettings.Default;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            loadedSettings = LinuxAppSettings.Default;
        }

        this.ReplaceSettings(loadedSettings, save: false);

        if (this.engine.State == TimerState.Stopped)
        {
            this.ReplaceViewState(this.viewState with { TimerInput = loadedSettings.GetInitialTimerInput(TimerViewState.DefaultTimerInput) });
            this.RefreshDisplay(TimerViewState.ReadyStatusText);
        }
    }

    public void Dispose()
    {
        this.engine.Expired -= this.OnEngineExpired;
        _ = this.ReleaseInhibitionAsync();
    }

    public void Tick()
    {
        this.engine.Update();
        this.RefreshDisplay();
    }

    private void Start()
    {
        DateTime now = this.wallClockNow();
        TimerStart? timerStart = TimerStart.FromString(this.TimerInput);

        if (timerStart == null || !timerStart.IsValid || !timerStart.TryGetEndTime(now, out DateTime endTime) || endTime < now)
        {
            this.RefreshDisplay(InvalidTimerStatusText);
            return;
        }

        if (!this.engine.Start(timerStart, now))
        {
            this.RefreshDisplay(InvalidTimerStatusText);
            return;
        }

        this.RefreshDisplay(TimerViewState.RunningStatusText);
        _ = this.AcquireInhibitionAsync();
        this.ReplaceSettings(this.settings.AddRecentTimerInput(this.TimerInput), save: true);
    }

    private void PauseOrResume()
    {
        if (this.engine.State == TimerState.Running)
        {
            this.engine.Pause();
            this.RefreshDisplay(TimerViewState.PausedStatusText);
            _ = this.ReleaseInhibitionAsync();
            return;
        }

        if (this.engine.State == TimerState.Paused)
        {
            this.engine.Resume(this.wallClockNow());
            this.RefreshDisplay(TimerViewState.RunningStatusText);
            _ = this.AcquireInhibitionAsync();
        }
    }

    private void Reset()
    {
        this.engine.Stop();
        this.RefreshDisplay(TimerViewState.ReadyStatusText);
        _ = this.ReleaseInhibitionAsync();
    }

    private void ToggleNotifications()
    {
        this.ReplaceSettings(
            this.settings with { NotificationsEnabled = !this.settings.NotificationsEnabled },
            save: true);
    }

    private void ToggleAudioAlerts()
    {
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

    private void RefreshDisplay(string? explicitStatus = null)
    {
        this.ReplaceViewState(TimerViewState.FromTimerState(
            this.TimerInput,
            this.engine.Snapshot,
            this.TimerTitle ?? string.Empty,
            explicitStatus));
        this.OnPropertyChanged(nameof(this.TimerInput));
        this.OnPropertyChanged(nameof(this.RemainingTime));
        this.OnPropertyChanged(nameof(this.StatusText));
        this.OnPropertyChanged(nameof(this.PauseResumeText));
        this.OnPropertyChanged(nameof(this.IsInputEnabled));
        this.OnPropertyChanged(nameof(this.IsRunning));
        this.OnPropertyChanged(nameof(this.State));
        this.OnPropertyChanged(nameof(this.ProgressPercent));
        this.OnPropertyChanged(nameof(this.IsTimerInputVisible));
        this.OnPropertyChanged(nameof(this.IsRemainingTimeVisible));
        this.OnPropertyChanged(nameof(this.IsCompletionTextVisible));
        this.OnPropertyChanged(nameof(this.IsStartVisible));
        this.OnPropertyChanged(nameof(this.IsPauseVisible));
        this.OnPropertyChanged(nameof(this.IsResumeVisible));
        this.OnPropertyChanged(nameof(this.IsStopVisible));
        this.StartCommand.RaiseCanExecuteChanged();
        this.PauseResumeCommand.RaiseCanExecuteChanged();
        this.ResetCommand.RaiseCanExecuteChanged();
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

        if (save)
        {
            this.QueueSettingsSave(next);
        }
    }

    private async void OnEngineExpired(object? sender, EventArgs e)
    {
        await this.HandleEngineExpiredAsync().ConfigureAwait(false);
    }

    private async Task HandleEngineExpiredAsync()
    {
        this.RefreshDisplay(TimerViewState.TimerCompleteStatusText);
        await this.ReleaseInhibitionAsync().ConfigureAwait(false);
        await this.NotifyTimerExpiredAsync().ConfigureAwait(false);
        await this.PlayTimerExpiredAudioAsync().ConfigureAwait(false);
    }

    private async Task NotifyTimerExpiredAsync()
    {
        if (!this.settings.NotificationsEnabled)
        {
            return;
        }

        try
        {
            await this.notificationService.ShowTimerExpiredAsync(NotificationTitle, NotificationBody).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task PlayTimerExpiredAudioAsync()
    {
        if (!this.settings.AudioAlertsEnabled)
        {
            return;
        }

        try
        {
            await this.audioAlertService.PlayAlertAsync(AudioAlertSoundIds.NormalBeep).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task AcquireInhibitionAsync()
    {
        await this.ReleaseInhibitionAsync().ConfigureAwait(false);

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

    private async Task SaveSettingsAfterAsync(Task previousSave, LinuxAppSettings settingsSnapshot)
    {
        await previousSave.ConfigureAwait(false);

        try
        {
            await this.settingsStore.SaveAsync(SettingsKey, settingsSnapshot).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        public Task PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
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
