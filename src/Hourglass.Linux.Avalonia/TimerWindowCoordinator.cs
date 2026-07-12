using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

internal sealed class TimerWindowCoordinator : IAsyncDisposable
{
    private const string ActiveSessionKey = "active-session";
    private const string ActiveSessionsKey = "active-sessions";
    private const string SettingsKey = "app";
    private const string SavedTimersKey = "saved-timers";

    private static readonly string SoundAssetsDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds");

    private static readonly Uri StatusIconResourceUri = new("avares://hourglass-linux/Assets/hourglass.png");

    private readonly DesktopProgressController desktopProgressController;
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly IAudioAlertService audioAlertService;
    private readonly INotificationService notificationService;
    private readonly ISettingsStore settingsStore;
    private readonly ISavedTimersStore savedTimersStore;
    private readonly IStatusIconService statusIconService;
    private readonly ISystemPowerService systemPowerService;
    private readonly CoordinatedSessionInhibitor sessionInhibitor;
    private readonly HashSet<MainWindow> closingWindows = [];
    private readonly List<WindowRegistration> windows = [];
    private Task pendingSessionSave = Task.CompletedTask;
    private bool exitCloseInProgress;
    private bool isShuttingDown;
    private bool showInNotificationArea;
    private WindowRegistration? mostRecentWindow;

    public TimerWindowCoordinator(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(
            lifetime,
            new JsonFileSettingsStore(new XdgSettingsPathService()),
            new NotifySendNotificationService(),
            new LinuxAudioAlertService(SoundAssetsDirectory),
            new CoordinatedSessionInhibitor(new SystemdSessionInhibitor()),
            new UnsupportedSystemPowerService(),
            LinuxDesktopProgressServiceFactory.CreateDefault(),
            CreateStatusIconService())
    {
    }

    internal TimerWindowCoordinator(
        IClassicDesktopStyleApplicationLifetime lifetime,
        ISettingsStore settingsStore,
        INotificationService notificationService,
        IAudioAlertService audioAlertService,
        CoordinatedSessionInhibitor sessionInhibitor,
        ISystemPowerService systemPowerService,
        IDesktopProgressService desktopProgressService,
        IStatusIconService statusIconService)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.sessionInhibitor = sessionInhibitor ?? throw new ArgumentNullException(nameof(sessionInhibitor));
        this.systemPowerService = systemPowerService ?? throw new ArgumentNullException(nameof(systemPowerService));
        this.savedTimersStore = new CoordinatedSavedTimersStore(this.settingsStore);
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)));
        this.statusIconService = statusIconService ?? throw new ArgumentNullException(nameof(statusIconService));
        this.statusIconService.ActionRequested += this.StatusIconActionRequested;
    }

    public async Task StartAsync(
        SingleInstanceLaunchRequest? initialRequest = null,
        CancellationToken cancellationToken = default)
    {
        LinuxAppSettings settings = await this.LoadDocumentAsync(SettingsKey, LinuxAppSettings.Default, cancellationToken)
            .ConfigureAwait(true);
        ActiveTimerSessionsDocument activeSessions = await this.LoadActiveSessionsAsync(cancellationToken)
            .ConfigureAwait(true);
        this.showInNotificationArea = settings.ShowInNotificationArea && this.statusIconService.IsSupported;

        bool restoredAny = false;
        if (settings.RestoreActiveSessionOnStartup)
        {
            DateTime wallClockNow = DateTime.Now;
            foreach (ActiveTimerSessionDefinition session in activeSessions.Sessions)
            {
                if (IsRestorableActiveSession(session.Session, wallClockNow)
                    && this.CreateWindow(session.SessionId, session.Session) != null)
                {
                    restoredAny = true;
                }
            }
        }

        if (!restoredAny && settings.OpenSavedTimersOnStartup)
        {
            SavedTimersDocument savedTimers = await this.LoadDocumentAsync(SavedTimersKey, SavedTimersDocument.Empty, cancellationToken)
                .ConfigureAwait(true);
            foreach (SavedTimerDefinition savedTimer in savedTimers.Timers)
            {
                this.CreateWindow(savedTimer: savedTimer);
                restoredAny = true;
            }
        }

        if (initialRequest?.Kind == SingleInstanceLaunchRequestKind.StartTimer)
        {
            this.CreateWindow(launchRequest: initialRequest);
            restoredAny = true;
        }

        if (!restoredAny)
        {
            this.CreateWindow();
        }

        if (initialRequest?.Kind == SingleInstanceLaunchRequestKind.Activate)
        {
            this.ActivateMostRelevantWindow();
        }
    }

    public Task HandleLaunchRequestAsync(SingleInstanceLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind == SingleInstanceLaunchRequestKind.StartTimer)
        {
            this.CreateWindow(launchRequest: request);
            return Task.CompletedTask;
        }

        this.ActivateMostRelevantWindow();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        this.statusIconService.ActionRequested -= this.StatusIconActionRequested;
        await this.pendingSessionSave.ConfigureAwait(false);
        await this.desktopProgressController.ClearAsync().ConfigureAwait(false);
        await this.statusIconService.DisposeAsync().ConfigureAwait(false);
        await this.sessionInhibitor.DisposeAsync().ConfigureAwait(false);
    }

    private MainWindow? CreateWindow(
        string? sessionId = null,
        ActiveTimerSessionDocument? session = null,
        SavedTimerDefinition? savedTimer = null,
        SingleInstanceLaunchRequest? launchRequest = null)
    {
        var viewModel = new MainWindowViewModel(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            this.notificationService,
            this.sessionInhibitor,
            this.settingsStore,
            this.savedTimersStore,
            this.audioAlertService,
            this.systemPowerService,
            this.statusIconService.IsSupported,
            this.statusIconService.CanRecoverHiddenWindow,
            sessionId,
            persistActiveSessionDirectly: false,
            restoreActiveSessionOnLoad: false);
        var window = new MainWindow(
            viewModel,
            new UnsupportedDesktopProgressService(),
            UnsupportedStatusIconService.Instance,
            loadSettingsOnOpened: false,
            prepareCoordinatorClose: this.PrepareWindowCloseAsync,
            requestApplicationExit: this.CloseAllWindowsAsync);
        var registration = new WindowRegistration(window, viewModel);

        this.windows.Add(registration);
        this.mostRecentWindow = registration;
        window.Activated += this.WindowActivated;
        window.Closed += this.WindowClosed;
        viewModel.PropertyChanged += this.ViewModelPropertyChanged;
        viewModel.ActiveSessionChanged += this.ViewModelActiveSessionChanged;
        viewModel.NewTimerRequested += this.ViewModelNewTimerRequested;
        viewModel.OpenAllSavedTimersRequested += this.ViewModelOpenAllSavedTimersRequested;

        if (this.lifetime.MainWindow == null)
        {
            this.lifetime.MainWindow = window;
        }

        _ = this.LoadWindowAsync(registration, session, savedTimer, launchRequest);
        window.Show();
        _ = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
        return window;
    }

    private async Task LoadWindowAsync(
        WindowRegistration registration,
        ActiveTimerSessionDocument? session,
        SavedTimerDefinition? savedTimer,
        SingleInstanceLaunchRequest? launchRequest)
    {
        await registration.ViewModel.LoadSettingsAsync().ConfigureAwait(true);

        if (!this.windows.Contains(registration))
        {
            return;
        }

        if (session != null && !registration.ViewModel.RestoreActiveSession(session))
        {
            registration.Window.Close();
            return;
        }

        if (savedTimer != null)
        {
            registration.ViewModel.ApplySavedTimer(savedTimer);
        }

        if (launchRequest?.Kind == SingleInstanceLaunchRequestKind.StartTimer
            && !string.IsNullOrWhiteSpace(launchRequest.TimerInput))
        {
            registration.ViewModel.ApplyLaunchTimerRequest(
                launchRequest.TimerInput,
                launchRequest.TimerTitle);
        }

        _ = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
    }

    private async Task<ActiveTimerSessionsDocument> LoadActiveSessionsAsync(CancellationToken cancellationToken)
    {
        LoadDocumentResult<ActiveTimerSessionsDocument> activeSessions = await this.TryLoadDocumentAsync<ActiveTimerSessionsDocument>(
            ActiveSessionsKey,
            cancellationToken).ConfigureAwait(true);
        if (activeSessions.Found)
        {
            return activeSessions.Value ?? ActiveTimerSessionsDocument.Empty;
        }

        ActiveTimerSessionDocument? legacySession = await this.LoadOptionalDocumentAsync<ActiveTimerSessionDocument>(
            ActiveSessionKey,
            cancellationToken).ConfigureAwait(true);
        return legacySession == null
            ? ActiveTimerSessionsDocument.Empty
            : new ActiveTimerSessionsDocument(sessions:
            [
                new ActiveTimerSessionDefinition(Guid.NewGuid().ToString("N"), legacySession)
            ]);
    }

    private async Task<T> LoadDocumentAsync<T>(string key, T fallback, CancellationToken cancellationToken)
    {
        try
        {
            return await this.settingsStore.LoadAsync<T>(key, cancellationToken).ConfigureAwait(true) ?? fallback;
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

    private async Task<LoadDocumentResult<T>> TryLoadDocumentAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            T? value = await this.settingsStore.LoadAsync<T>(key, cancellationToken).ConfigureAwait(true);
            return value == null
                ? new LoadDocumentResult<T>(false, default)
                : new LoadDocumentResult<T>(true, value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new LoadDocumentResult<T>(false, default);
        }
    }

    private async Task<T?> LoadOptionalDocumentAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await this.settingsStore.LoadAsync<T>(key, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static bool IsRestorableActiveSession(ActiveTimerSessionDocument? session, DateTime wallClockNow)
    {
        return session?.ToTimerInfo(wallClockNow) != null;
    }

    private void ViewModelNewTimerRequested(object? sender, EventArgs e)
    {
        this.CreateWindow();
    }

    private void ViewModelOpenAllSavedTimersRequested(object? sender, OpenAllSavedTimersRequestedEventArgs e)
    {
        foreach (SavedTimerDefinition savedTimer in e.SavedTimers.Timers)
        {
            this.CreateWindow(savedTimer: savedTimer);
        }
    }

    private void ViewModelActiveSessionChanged(object? sender, EventArgs e)
    {
        _ = this.QueueSessionSave();
        this.ApplyStatusIconState();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ShowInNotificationArea)
            && sender is MainWindowViewModel viewModel)
        {
            this.showInNotificationArea = viewModel.ShowInNotificationArea;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.DesktopProgressRequest))
        {
            this.ApplyDesktopProgress();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.StatusIconMenuState)
            or nameof(MainWindowViewModel.WindowTitle)
            or nameof(MainWindowViewModel.ShowInNotificationArea))
        {
            this.ApplyStatusIconState();
        }
    }

    private void WindowActivated(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            WindowRegistration? registration = this.windows.FirstOrDefault(item => item.Window == window);
            if (registration != null)
            {
                this.mostRecentWindow = registration;
                this.ApplyStatusIconState();
                this.ApplyDesktopProgress();
            }
        }
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        WindowRegistration? registration = this.windows.FirstOrDefault(item => item.Window == window);
        if (registration == null)
        {
            return;
        }

        window.Activated -= this.WindowActivated;
        window.Closed -= this.WindowClosed;
        registration.ViewModel.PropertyChanged -= this.ViewModelPropertyChanged;
        registration.ViewModel.ActiveSessionChanged -= this.ViewModelActiveSessionChanged;
        registration.ViewModel.NewTimerRequested -= this.ViewModelNewTimerRequested;
        registration.ViewModel.OpenAllSavedTimersRequested -= this.ViewModelOpenAllSavedTimersRequested;
        this.closingWindows.Remove(window);
        this.windows.Remove(registration);

        if (this.mostRecentWindow == registration)
        {
            this.mostRecentWindow = this.windows.LastOrDefault();
        }

        Task sessionSave = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();

        if (this.windows.Count == 0 && !this.isShuttingDown)
        {
            this.isShuttingDown = true;
            _ = this.ShutdownAfterFinalSessionSaveAsync(sessionSave);
        }
    }

    private void StatusIconActionRequested(object? sender, StatusIconActionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.HandleStatusIconAction(e.Action));
    }

    private void ActivateMostRelevantWindow()
    {
        WindowRegistration? target = this.GetStatusIconTarget();
        if (target == null)
        {
            target = this.CreateWindow() == null ? null : this.mostRecentWindow;
        }

        if (target != null)
        {
            new WindowAttentionController(target.Window).RequestAttention();
        }
    }

    private void HandleStatusIconAction(StatusIconAction action)
    {
        if (action == StatusIconAction.NewTimer)
        {
            this.CreateWindow();
            return;
        }

        WindowRegistration? target = this.GetStatusIconTarget();
        if (target == null)
        {
            return;
        }

        switch (action)
        {
            case StatusIconAction.ShowWindow:
                new WindowAttentionController(target.Window).RequestAttention();
                break;
            case StatusIconAction.HideWindow:
                target.ViewModel.HideToNotificationAreaCommand.Execute(null);
                break;
            case StatusIconAction.PauseResume:
                target.ViewModel.PauseResumeCommand.Execute(null);
                break;
            case StatusIconAction.Stop:
                target.ViewModel.ResetCommand.Execute(null);
                break;
            case StatusIconAction.Restart:
                target.ViewModel.RestartCommand.Execute(null);
                break;
            case StatusIconAction.Exit:
                _ = this.CloseAllWindowsAsync();
                break;
        }
    }

    private async Task CloseAllWindowsAsync()
    {
        if (this.exitCloseInProgress)
        {
            return;
        }

        this.exitCloseInProgress = true;
        try
        {
            WindowRegistration[] snapshot = this.windows.ToArray();
            if (snapshot.Length == 0)
            {
                return;
            }

            if (snapshot.Any(registration => registration.Window.RequiresExitConfirmation))
            {
                WindowRegistration owner = this.GetStatusIconTarget() ?? snapshot[0];
                new WindowAttentionController(owner.Window).RequestAttention();

                var dialog = new ExitConfirmationWindow();
                bool approved = await dialog.ShowDialog<bool>(owner.Window).ConfigureAwait(true);
                if (!approved)
                {
                    return;
                }
            }

            foreach (WindowRegistration registration in snapshot)
            {
                if (!this.windows.Contains(registration))
                {
                    continue;
                }

                new WindowAttentionController(registration.Window).RequestAttention();
                registration.Window.CloseWithPreapprovedExit();
            }
        }
        finally
        {
            this.exitCloseInProgress = false;
        }
    }

    private WindowRegistration? GetStatusIconTarget()
    {
        return this.windows
            .Where(item => item.ViewModel.State == TimerState.Expired)
            .Concat(this.windows.Where(item => item.ViewModel.State == TimerState.Running))
            .Concat(this.mostRecentWindow == null ? [] : [this.mostRecentWindow])
            .Concat(this.windows)
            .FirstOrDefault();
    }

    private Task QueueSessionSave()
    {
        ActiveTimerSessionsDocument document = this.CreateActiveSessionsDocument();
        this.pendingSessionSave = this.SaveSessionsAfterAsync(this.pendingSessionSave, document);
        return this.pendingSessionSave;
    }

    private ActiveTimerSessionsDocument CreateActiveSessionsDocument()
    {
        ActiveTimerSessionDefinition[] sessions = this.windows
            .Where(window => !this.closingWindows.Contains(window.Window))
            .Select(window => new ActiveTimerSessionDefinition(
                window.ViewModel.SessionId,
                window.ViewModel.CreateActiveSessionDocument()))
            .ToArray();
        return new ActiveTimerSessionsDocument(sessions: sessions);
    }

    private async Task SaveSessionsAfterAsync(Task previousSave, ActiveTimerSessionsDocument document)
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
            await this.settingsStore.SaveAsync(ActiveSessionsKey, document).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void ApplyDesktopProgress()
    {
        DesktopProgressRequest request = this.GetAggregateDesktopProgressRequest();
        _ = this.desktopProgressController.ApplyAsync(request);
    }

    private DesktopProgressRequest GetAggregateDesktopProgressRequest()
    {
        WindowRegistration[] snapshot = this.windows.ToArray();
        return snapshot
            .Where(window => window.ViewModel.DesktopProgressRequest.State == DesktopProgressState.Error)
            .Concat(snapshot.Where(window => window.ViewModel.DesktopProgressRequest.State == DesktopProgressState.Normal))
            .Concat(this.mostRecentWindow == null ? [] : [this.mostRecentWindow])
            .Concat(snapshot.Where(window => window.ViewModel.DesktopProgressRequest.State == DesktopProgressState.Paused))
            .Select(window => window.ViewModel.DesktopProgressRequest)
            .FirstOrDefault(request => !request.IsHidden);
    }

    private void ApplyStatusIconState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(this.ApplyStatusIconState);
            return;
        }

        _ = this.ApplyStatusIconStateAsync();
    }

    private async Task ApplyStatusIconStateAsync()
    {
        WindowRegistration? target = this.GetStatusIconTarget();
        StatusIconMenuState targetState = target?.ViewModel.StatusIconMenuState
            ?? new StatusIconMenuState(
                "Hourglass",
                false,
                "Pause",
                false,
                false,
                false,
                false,
                true);
        StatusIconMenuState state = targetState with
        {
            IsVisible = this.showInNotificationArea,
            CanHideWindow = this.showInNotificationArea && this.statusIconService.CanRecoverHiddenWindow
        };

        try
        {
            await this.statusIconService.UpdateAsync(state).ConfigureAwait(true);
        }
        catch (Exception)
        {
        }
    }

    private async Task ShutdownAfterFinalSessionSaveAsync(Task sessionSave)
    {
        try
        {
            await sessionSave.ConfigureAwait(true);
        }
        catch (Exception)
        {
        }

        this.lifetime.Shutdown();
    }

    private async Task PrepareWindowCloseAsync(MainWindow window)
    {
        if (!this.windows.Any(registration => registration.Window == window))
        {
            return;
        }

        this.closingWindows.Add(window);
        await this.QueueSessionSave().ConfigureAwait(false);
    }

    private static IStatusIconService CreateStatusIconService()
    {
        var environmentReader = new ProcessDesktopEnvironmentReader();
        var sessionBusProbe = new EnvironmentSessionBusProbe(environmentReader);
        if (!new LinuxStatusIconCapability(environmentReader, sessionBusProbe).IsSupported())
        {
            return UnsupportedStatusIconService.Instance;
        }

        try
        {
            using Stream iconStream = AssetLoader.Open(StatusIconResourceUri);
            return new AvaloniaStatusIconService(new WindowIcon(iconStream));
        }
        catch (Exception)
        {
            return UnsupportedStatusIconService.Instance;
        }
    }

    private readonly record struct LoadDocumentResult<T>(bool Found, T? Value);

    private sealed record WindowRegistration(MainWindow Window, MainWindowViewModel ViewModel);
}
