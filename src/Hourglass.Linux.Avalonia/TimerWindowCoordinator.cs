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
    private readonly WakeAlarmController wakeAlarmController;
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly IAudioAlertService audioAlertService;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly INotificationService notificationService;
    private readonly ApplicationInfoProvider applicationInfoProvider;
    private readonly IExternalUriLauncher externalUriLauncher;
    private readonly ISettingsStore settingsStore;
    private readonly IAppSettingsStore appSettingsStore;
    private readonly ISavedTimersStore savedTimersStore;
    private readonly IStatusIconService statusIconService;
    private readonly ISystemPowerService systemPowerService;
    private readonly Func<IWindowAttentionTarget, IWindowAttentionService> createWindowAttentionService;
    private readonly CoordinatedSessionInhibitor sessionInhibitor;
    private readonly HashSet<MainWindow> closingWindows = [];
    private readonly List<WindowRegistration> windows = [];
    private Task pendingSessionSave = Task.CompletedTask;
    private bool exitCloseInProgress;
    private bool isShuttingDown;
    private bool showInNotificationArea;
    private bool wakeFromSuspendEnabled;
    private WindowRegistration? mostRecentWindow;

    public TimerWindowCoordinator(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(lifetime, CreateDefaultServices())
    {
    }

    private TimerWindowCoordinator(
        IClassicDesktopStyleApplicationLifetime lifetime,
        DefaultCoordinatorServices services)
        : this(
            lifetime,
            services.SettingsStore,
            services.NotificationService,
            services.AudioAlertService,
            services.SessionInhibitor,
            UnsupportedSystemPowerService.Instance,
            new RtcWakeAlarmService(),
            services.DesktopProgressService,
            services.StatusIconService,
            new ApplicationInfoProvider(),
            services.ExternalUriLauncher,
            createWindowAttentionService: target => new WindowAttentionController(target, services.DiagnosticSink),
            diagnosticSink: services.DiagnosticSink)
    {
    }

    internal TimerWindowCoordinator(
        IClassicDesktopStyleApplicationLifetime lifetime,
        ISettingsStore settingsStore,
        INotificationService notificationService,
        IAudioAlertService audioAlertService,
        CoordinatedSessionInhibitor sessionInhibitor,
        ISystemPowerService systemPowerService,
        IWakeAlarmService wakeAlarmService,
        IDesktopProgressService desktopProgressService,
        IStatusIconService statusIconService,
        ApplicationInfoProvider? applicationInfoProvider = null,
        IExternalUriLauncher? externalUriLauncher = null,
        Func<IWindowAttentionTarget, IWindowAttentionService>? createWindowAttentionService = null,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
        this.appSettingsStore = new CoordinatedAppSettingsStore(this.settingsStore);
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.sessionInhibitor = sessionInhibitor ?? throw new ArgumentNullException(nameof(sessionInhibitor));
        this.systemPowerService = systemPowerService ?? throw new ArgumentNullException(nameof(systemPowerService));
        this.wakeAlarmController = new WakeAlarmController(
            wakeAlarmService ?? throw new ArgumentNullException(nameof(wakeAlarmService)),
            () => DateTimeOffset.Now,
            this.diagnosticSink);
        this.savedTimersStore = new CoordinatedSavedTimersStore(this.settingsStore);
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)),
            this.diagnosticSink);
        this.statusIconService = statusIconService ?? throw new ArgumentNullException(nameof(statusIconService));
        this.applicationInfoProvider = applicationInfoProvider ?? new ApplicationInfoProvider();
        this.externalUriLauncher = externalUriLauncher ?? new LinuxExternalUriLauncher();
        this.createWindowAttentionService = createWindowAttentionService ?? CreateWindowAttentionService;
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
        this.wakeFromSuspendEnabled = settings.WakeFromSuspendEnabled;

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

        this.ApplyWakeAlarm();
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
        await this.wakeAlarmController.DisposeAsync().ConfigureAwait(false);
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
            this.appSettingsStore,
            this.savedTimersStore,
            this.audioAlertService,
            this.systemPowerService,
            this.statusIconService.IsSupported,
            this.statusIconService.CanRecoverHiddenWindow,
            sessionId,
            persistActiveSessionDirectly: false,
            restoreActiveSessionOnLoad: false,
            uiDispatcher: AvaloniaUiDispatcher.Instance,
            diagnosticSink: this.diagnosticSink);
        var window = new MainWindow(
            viewModel,
            UnsupportedDesktopProgressService.Instance,
            UnsupportedStatusIconService.Instance,
            this.applicationInfoProvider,
            this.externalUriLauncher,
            this.createWindowAttentionService,
            loadSettingsOnOpened: false,
            prepareCoordinatorClose: this.PrepareWindowCloseAsync,
            requestApplicationExit: this.CloseAllWindowsAsync);
        var registration = new WindowRegistration(window, viewModel);

        this.windows.Add(registration);
        this.mostRecentWindow = registration;
        window.Activated += this.WindowActivated;
        window.Closed += this.WindowClosed;
        window.WindowGeometryChanged += this.WindowGeometryChanged;
        viewModel.PropertyChanged += this.ViewModelPropertyChanged;
        viewModel.ActiveSessionChanged += this.ViewModelActiveSessionChanged;
        viewModel.NewTimerRequested += this.ViewModelNewTimerRequested;
        viewModel.OpenAllSavedTimersRequested += this.ViewModelOpenAllSavedTimersRequested;

        if (this.lifetime.MainWindow == null)
        {
            this.lifetime.MainWindow = window;
        }

        this.ApplyInitialGeometry(window, session?.WindowGeometry);
        _ = this.LoadWindowAsync(registration, session, savedTimer, launchRequest);
        window.Show();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
        this.ApplyWakeAlarm();
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
        this.ApplyWakeAlarm();
    }

    private void ApplyInitialGeometry(MainWindow window, WindowGeometrySnapshot? restoredGeometry)
    {
        if (restoredGeometry != null)
        {
            window.ApplyWindowGeometry(restoredGeometry);
            return;
        }

        WindowGeometrySnapshot? previousGeometry = this.mostRecentWindow?.Window == window
            ? this.windows
                .Where(registration => registration.Window != window)
                .LastOrDefault()
                ?.Window.CurrentWindowGeometry
            : this.mostRecentWindow?.Window.CurrentWindowGeometry;
        if (previousGeometry == null)
        {
            return;
        }

        window.ApplyWindowGeometry(window.CreateCascadedWindowGeometry(previousGeometry));
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
        catch (Exception exception)
        {
            this.RecordDataRecovery("load", key, "Document load failed; using fallback.", exception);
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
        catch (Exception exception)
        {
            this.RecordDataRecovery("load", key, "Document load failed; treating it as missing.", exception);
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
        catch (Exception exception)
        {
            this.RecordDataRecovery("load", key, "Optional document load failed; treating it as missing.", exception);
            return default;
        }
    }

    private static bool IsRestorableActiveSession(ActiveTimerSessionDocument? session, DateTime wallClockNow)
    {
        return session != null
            && ActiveTimerSessionSnapshot.FromDocument(session, wallClockNow, TimeSpan.Zero) != null;
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
        this.ApplyWakeAlarm();
    }

    private void WindowGeometryChanged(object? sender, EventArgs e)
    {
        _ = this.QueueSessionSave();
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
        window.WindowGeometryChanged -= this.WindowGeometryChanged;
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
        this.ApplyWakeAlarm();

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
            target.Window.RequestAttention();
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
                target.Window.RequestAttention();
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
                owner.Window.RequestAttention();

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

                registration.Window.RequestAttention();
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
                window.ViewModel.CreateActiveSessionDocument(window.Window.CurrentWindowGeometry)))
            .ToArray();
        return new ActiveTimerSessionsDocument(sessions: sessions);
    }

    private async Task SaveSessionsAfterAsync(Task previousSave, ActiveTimerSessionsDocument document)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", ActiveSessionsKey, "Previous active sessions save failed before a queued save.", exception);
        }

        try
        {
            await this.settingsStore.SaveAsync(ActiveSessionsKey, document).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", ActiveSessionsKey, "Active sessions save failed.", exception);
        }
    }

    private void ApplyDesktopProgress()
    {
        DesktopProgressRequest request = this.GetAggregateDesktopProgressRequest();
        _ = this.desktopProgressController.ApplyAsync(request);
    }

    private void ApplyWakeAlarm()
    {
        WakeAlarmTimerSnapshot[] timers = this.windows
            .Where(window => !this.closingWindows.Contains(window.Window))
            .Select(window => new WakeAlarmTimerSnapshot(window.ViewModel.State, window.ViewModel.EndTime))
            .ToArray();
        _ = this.wakeAlarmController.ApplyAsync(timers, this.wakeFromSuspendEnabled);
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
                ApplicationStrings.ApplicationTitle,
                false,
                ApplicationStrings.CommandPause,
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
        catch (Exception exception)
        {
            this.RecordBestEffort("status-icon", "update", this.statusIconService.GetType().Name, "Status icon update failed.", exception);
        }
    }

    private async Task ShutdownAfterFinalSessionSaveAsync(Task sessionSave)
    {
        try
        {
            await sessionSave.ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.RecordDataRecovery("save", ActiveSessionsKey, "Final session save failed before shutdown.", exception);
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

    private static IWindowAttentionService CreateWindowAttentionService(IWindowAttentionTarget target)
    {
        return new WindowAttentionController(target);
    }

    private static DefaultCoordinatorServices CreateDefaultServices()
    {
        IDiagnosticSink diagnosticSink = DiagnosticSinkFactory.CreateDefault();
        var environmentReader = new ProcessDesktopEnvironmentReader();
        var sessionBusProbe = new EnvironmentSessionBusProbe(environmentReader);
        return new DefaultCoordinatorServices(
            new JsonFileSettingsStore(new XdgSettingsPathService(), diagnosticSink),
            new NotifySendNotificationService(diagnosticSink),
            new LinuxAudioAlertService(SoundAssetsDirectory, diagnosticSink),
            new CoordinatedSessionInhibitor(new SystemdSessionInhibitor(diagnosticSink)),
            LinuxDesktopProgressServiceFactory.CreateDefault(diagnosticSink),
            CreateStatusIconService(environmentReader, sessionBusProbe, diagnosticSink),
            new LinuxExternalUriLauncher(diagnosticSink),
            diagnosticSink);
    }

    private static IStatusIconService CreateStatusIconService(
        IDesktopEnvironmentReader environmentReader,
        ISessionBusProbe sessionBusProbe,
        IDiagnosticSink diagnosticSink)
    {
        if (!new LinuxStatusIconCapability(environmentReader, sessionBusProbe).IsSupported())
        {
            return UnsupportedStatusIconService.Instance;
        }

        try
        {
            using Stream iconStream = AssetLoader.Open(StatusIconResourceUri);
            return new AvaloniaStatusIconService(new WindowIcon(iconStream));
        }
        catch (Exception exception)
        {
            diagnosticSink.TryRecord(new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticFailureClass.StartupConfiguration,
                "status-icon",
                "create",
                "avalonia-status-icon",
                "Status icon backend could not be initialized.",
                exception));
            return UnsupportedStatusIconService.Instance;
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

    private void RecordStartupConfiguration(
        string category,
        string operation,
        string backend,
        string message,
        Exception exception)
    {
        this.RecordDiagnostic(
            DiagnosticFailureClass.StartupConfiguration,
            category,
            operation,
            backend,
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

    private readonly record struct LoadDocumentResult<T>(bool Found, T? Value);

    private sealed record WindowRegistration(MainWindow Window, MainWindowViewModel ViewModel);

    private sealed record DefaultCoordinatorServices(
        ISettingsStore SettingsStore,
        INotificationService NotificationService,
        IAudioAlertService AudioAlertService,
        CoordinatedSessionInhibitor SessionInhibitor,
        IDesktopProgressService DesktopProgressService,
        IStatusIconService StatusIconService,
        IExternalUriLauncher ExternalUriLauncher,
        IDiagnosticSink DiagnosticSink);
}
