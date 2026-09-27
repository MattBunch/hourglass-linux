using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Hourglass.Application;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

internal sealed class TimerWindowCoordinator : IAsyncDisposable
{
    private const string ActiveSessionsKey = "active-sessions";

    private static readonly string SoundAssetsDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds");

    private static readonly Uri StatusIconResourceUri = new("avares://hourglass-linux/Assets/hourglass.png");

    private readonly DesktopProgressController desktopProgressController;
    private readonly HourglassRuntime runtime;
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly IAudioAlertService audioAlertService;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly StartupDiagnostics startupDiagnostics;
    private readonly INotificationService notificationService;
    private readonly ApplicationInfoProvider applicationInfoProvider;
    private readonly IExternalUriLauncher externalUriLauncher;
    private readonly ISettingsStore settingsStore;
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
    private WindowRegistration? mostRecentWindow;

    public TimerWindowCoordinator(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(lifetime, StartupDiagnostics.Disabled)
    {
    }

    internal TimerWindowCoordinator(
        IClassicDesktopStyleApplicationLifetime lifetime,
        StartupDiagnostics startupDiagnostics)
        : this(lifetime, CreateDefaultServices(startupDiagnostics), startupDiagnostics)
    {
    }

    private TimerWindowCoordinator(
        IClassicDesktopStyleApplicationLifetime lifetime,
        DefaultCoordinatorServices services,
        StartupDiagnostics startupDiagnostics)
        : this(
            lifetime,
            services.SettingsStore,
            services.NotificationService,
            services.AudioAlertService,
            services.SessionInhibitor,
            services.SystemPowerService,
            services.WakeAlarmService,
            services.DesktopProgressService,
            services.StatusIconService,
            services.ApplicationInfoProvider,
            services.ExternalUriLauncher,
            createWindowAttentionService: target => new WindowAttentionController(target, services.DiagnosticSink),
            diagnosticSink: services.DiagnosticSink,
            startupDiagnostics: startupDiagnostics)
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
        IDiagnosticSink? diagnosticSink = null,
        StartupDiagnostics? startupDiagnostics = null)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
        this.runtime = new HourglassRuntime(this.diagnosticSink, services: new SessionRuntimeServices(notificationService, audioAlertService, sessionInhibitor, systemPowerService,
            ApplicationStrings.ApplicationTitle, ApplicationStrings.StatusTimerComplete, ApplicationStrings.SessionInhibitionReason), settingsStore: settingsStore);
        this.startupDiagnostics = startupDiagnostics ?? StartupDiagnostics.Disabled;
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.audioAlertService = audioAlertService ?? throw new ArgumentNullException(nameof(audioAlertService));
        this.sessionInhibitor = sessionInhibitor ?? throw new ArgumentNullException(nameof(sessionInhibitor));
        this.systemPowerService = systemPowerService ?? throw new ArgumentNullException(nameof(systemPowerService));
        this.runtime.ConfigureWakeAlarms(wakeAlarmService ?? throw new ArgumentNullException(nameof(wakeAlarmService)));
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
        this.startupDiagnostics.Record(StartupStage.CoordinatorStarting);
        ApplicationResult<RuntimeStartupSnapshot> result = await this.runtime.InitializeSessionsAsync(
            initialRequest?.Kind == SingleInstanceLaunchRequestKind.StartTimer ? initialRequest.TimerInput : null,
            initialRequest?.TimerTitle, cancellationToken).ConfigureAwait(true);
        if (result is not ApplicationResult<RuntimeStartupSnapshot>.Success started)
        {
            throw new InvalidOperationException(nameof(StartAsync));
        }
        this.startupDiagnostics.Record(StartupStage.SettingsLoaded);
        this.showInNotificationArea = started.Value.Data.Settings.ShowInNotificationArea && this.statusIconService.IsSupported;
        foreach (RestoredSession session in started.Value.Sessions)
        {
            this.CreateWindow(session.SessionId, session.Session.ToDocument(), restoredExpiry: session.ExpiredWhileClosed);
        }
        if (initialRequest?.Kind == SingleInstanceLaunchRequestKind.Activate) { this.ActivateMostRelevantWindow(); }
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
        try
        {
            await this.pendingSessionSave.ConfigureAwait(false);
            await this.desktopProgressController.ClearAsync().ConfigureAwait(false);
            await this.statusIconService.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await this.runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private MainWindow? CreateWindow(
        string? sessionId = null,
        ActiveTimerSessionDocument? session = null,
        SavedTimerDefinition? savedTimer = null,
        SingleInstanceLaunchRequest? launchRequest = null,
        bool restoredExpiry = false)
    {
        var viewModel = new MainWindowViewModel(
            new SystemMonotonicClock(),
            () => DateTime.Now,
            this.notificationService,
            this.sessionInhibitor,
            this.settingsStore,
            this.audioAlertService,
            this.systemPowerService,
            this.statusIconService.IsSupported,
            this.statusIconService.CanRecoverHiddenWindow,
            sessionId,
            persistActiveSessionDirectly: false,
            restoreActiveSessionOnLoad: false,
            uiDispatcher: AvaloniaUiDispatcher.Instance,
            diagnosticSink: this.diagnosticSink,
            runtime: this.runtime);
        this.startupDiagnostics.Record(StartupStage.MainWindowConstructionStarting);
        var window = new MainWindow(
            viewModel,
            UnsupportedDesktopProgressService.Instance,
            UnsupportedStatusIconService.Instance,
            this.applicationInfoProvider,
            this.externalUriLauncher,
            this.createWindowAttentionService,
            loadSettingsOnOpened: false,
            prepareCoordinatorClose: this.PrepareWindowCloseAsync,
            requestApplicationExit: this.CloseAllWindowsAsync,
            startupDiagnostics: this.startupDiagnostics);
        this.startupDiagnostics.Record(StartupStage.MainWindowConstructed);
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
        _ = this.LoadWindowAsync(registration, session, savedTimer, launchRequest, restoredExpiry);
        window.Show();
        this.startupDiagnostics.Record(StartupStage.MainWindowShowCalled);
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
        return window;
    }

    private async Task LoadWindowAsync(
        WindowRegistration registration,
        ActiveTimerSessionDocument? session,
        SavedTimerDefinition? savedTimer,
        SingleInstanceLaunchRequest? launchRequest,
        bool restoredExpiry)
    {
        await registration.ViewModel.LoadSettingsAsync().ConfigureAwait(true);

        if (!this.windows.Contains(registration))
        {
            return;
        }

        await registration.ViewModel.PendingCommands;
        if (session != null && !registration.ViewModel.RestoreActiveSession(session, restoredExpiry))
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

        await registration.ViewModel.PendingCommands;
        _ = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
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

    private void WindowGeometryChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            WindowRegistration? registration = this.windows.FirstOrDefault(item => item.Window == window);
            if (registration != null && !this.closingWindows.Contains(window))
            {
                registration.ViewModel.UpdateSessionGeometry(window.CurrentWindowGeometry);
            }
        }
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
            this.startupDiagnostics.Record(StartupStage.MainWindowActivated);
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
        this.pendingSessionSave = this.FlushSessionSaveAsync();
        return this.pendingSessionSave;
    }

    private async Task FlushSessionSaveAsync()
    {
        ApplicationResult<bool> result = await this.runtime.FlushPersistenceAsync().ConfigureAwait(false);
        if (result is ApplicationResult<bool>.Failure failed)
        {
            this.RecordDataRecovery("save", "active-sessions", failed.Error.Message, new IOException(failed.Error.Message));
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
        WindowRegistration registration = this.windows.First(item => item.Window == window);
        await registration.ViewModel.PendingCommands.ConfigureAwait(false);
        await this.runtime.RemoveAsync(registration.ViewModel.SessionId).ConfigureAwait(false);
        await this.QueueSessionSave().ConfigureAwait(false);
    }

    private static IWindowAttentionService CreateWindowAttentionService(IWindowAttentionTarget target)
    {
        return new WindowAttentionController(target);
    }

    private static DefaultCoordinatorServices CreateDefaultServices(StartupDiagnostics startupDiagnostics)
    {
        startupDiagnostics.RecordServiceStarting(StartupService.DiagnosticSink);
        IDiagnosticSink diagnosticSink = DiagnosticSinkFactory.CreateDefault();
        startupDiagnostics.RecordServiceCompleted(StartupService.DiagnosticSink);

        startupDiagnostics.RecordServiceStarting(StartupService.DesktopEnvironmentReader);
        var environmentReader = new ProcessDesktopEnvironmentReader();
        startupDiagnostics.RecordServiceCompleted(StartupService.DesktopEnvironmentReader);

        startupDiagnostics.RecordServiceStarting(StartupService.SessionBusProbe);
        var sessionBusProbe = new EnvironmentSessionBusProbe(environmentReader);
        startupDiagnostics.RecordServiceCompleted(StartupService.SessionBusProbe);

        startupDiagnostics.RecordServiceStarting(StartupService.XdgSettingsPathService);
        var settingsPathService = new XdgSettingsPathService();
        startupDiagnostics.RecordServiceCompleted(StartupService.XdgSettingsPathService);

        startupDiagnostics.RecordServiceStarting(StartupService.SettingsStore);
        var settingsStore = new JsonFileSettingsStore(settingsPathService, diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.SettingsStore);

        startupDiagnostics.RecordServiceStarting(StartupService.NotificationService);
        var notificationService = new NotifySendNotificationService(diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.NotificationService);

        startupDiagnostics.RecordServiceStarting(StartupService.AudioAlertService);
        var audioAlertService = new LinuxAudioAlertService(SoundAssetsDirectory, diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.AudioAlertService);

        startupDiagnostics.RecordServiceStarting(StartupService.SystemdSessionInhibitor);
        var systemdSessionInhibitor = new SystemdSessionInhibitor(diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.SystemdSessionInhibitor);

        startupDiagnostics.RecordServiceStarting(StartupService.SessionInhibitor);
        var sessionInhibitor = new CoordinatedSessionInhibitor(systemdSessionInhibitor);
        startupDiagnostics.RecordServiceCompleted(StartupService.SessionInhibitor);

        startupDiagnostics.RecordServiceStarting(StartupService.DesktopProgressService);
        IDesktopProgressService desktopProgressService = LinuxDesktopProgressServiceFactory.CreateDefault(diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.DesktopProgressService);

        startupDiagnostics.RecordServiceStarting(StartupService.StatusIconService);
        IStatusIconService statusIconService = CreateStatusIconService(
            environmentReader,
            sessionBusProbe,
            diagnosticSink,
            startupDiagnostics);
        startupDiagnostics.RecordServiceCompleted(StartupService.StatusIconService);

        startupDiagnostics.RecordServiceStarting(StartupService.ExternalUriLauncher);
        var externalUriLauncher = new LinuxExternalUriLauncher(diagnosticSink);
        startupDiagnostics.RecordServiceCompleted(StartupService.ExternalUriLauncher);

        startupDiagnostics.RecordServiceStarting(StartupService.SystemPowerService);
        ISystemPowerService systemPowerService = UnsupportedSystemPowerService.Instance;
        startupDiagnostics.RecordServiceCompleted(StartupService.SystemPowerService);

        startupDiagnostics.RecordServiceStarting(StartupService.WakeAlarmService);
        var wakeAlarmService = new RtcWakeAlarmService();
        startupDiagnostics.RecordServiceCompleted(StartupService.WakeAlarmService);

        startupDiagnostics.RecordServiceStarting(StartupService.ApplicationInfoProvider);
        var applicationInfoProvider = new ApplicationInfoProvider();
        startupDiagnostics.RecordServiceCompleted(StartupService.ApplicationInfoProvider);

        return new DefaultCoordinatorServices(
            settingsStore,
            notificationService,
            audioAlertService,
            sessionInhibitor,
            systemPowerService,
            wakeAlarmService,
            desktopProgressService,
            statusIconService,
            applicationInfoProvider,
            externalUriLauncher,
            diagnosticSink);
    }

    private static IStatusIconService CreateStatusIconService(
        IDesktopEnvironmentReader environmentReader,
        ISessionBusProbe sessionBusProbe,
        IDiagnosticSink diagnosticSink,
        StartupDiagnostics startupDiagnostics)
    {
        startupDiagnostics.RecordServiceStarting(StartupService.StatusIconCapability);
        bool isSupported = new LinuxStatusIconCapability(environmentReader, sessionBusProbe).IsSupported();
        startupDiagnostics.RecordServiceCompleted(StartupService.StatusIconCapability);
        if (!isSupported)
        {
            return UnsupportedStatusIconService.Instance;
        }

        try
        {
            startupDiagnostics.RecordServiceStarting(StartupService.StatusIconAsset);
            using Stream iconStream = AssetLoader.Open(StatusIconResourceUri);
            startupDiagnostics.RecordServiceCompleted(StartupService.StatusIconAsset);
            startupDiagnostics.RecordServiceStarting(StartupService.AvaloniaStatusIconService);
            var statusIconService = new AvaloniaStatusIconService(new WindowIcon(iconStream));
            startupDiagnostics.RecordServiceCompleted(StartupService.AvaloniaStatusIconService);
            return statusIconService;
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


    private sealed record WindowRegistration(MainWindow Window, MainWindowViewModel ViewModel);

    private sealed record DefaultCoordinatorServices(
        ISettingsStore SettingsStore,
        INotificationService NotificationService,
        IAudioAlertService AudioAlertService,
        CoordinatedSessionInhibitor SessionInhibitor,
        ISystemPowerService SystemPowerService,
        IWakeAlarmService WakeAlarmService,
        IDesktopProgressService DesktopProgressService,
        IStatusIconService StatusIconService,
        ApplicationInfoProvider ApplicationInfoProvider,
        IExternalUriLauncher ExternalUriLauncher,
        IDiagnosticSink DiagnosticSink);
}
