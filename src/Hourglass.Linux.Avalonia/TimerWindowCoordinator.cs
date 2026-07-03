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

    private static readonly string NormalBeepPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds",
        "BeepNormal.wav");

    private static readonly Uri StatusIconResourceUri = new("avares://hourglass-linux/Assets/hourglass.png");

    private readonly DesktopProgressController desktopProgressController;
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly IAudioAlertService audioAlertService;
    private readonly INotificationService notificationService;
    private readonly ISettingsStore settingsStore;
    private readonly IStatusIconService statusIconService;
    private readonly ISystemPowerService systemPowerService;
    private readonly CoordinatedSessionInhibitor sessionInhibitor;
    private readonly HashSet<MainWindow> closingWindows = [];
    private readonly List<WindowRegistration> windows = [];
    private Task pendingSessionSave = Task.CompletedTask;
    private bool isShuttingDown;
    private WindowRegistration? mostRecentWindow;

    public TimerWindowCoordinator(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(
            lifetime,
            new JsonFileSettingsStore(new XdgSettingsPathService()),
            new NotifySendNotificationService(),
            new LinuxAudioAlertService(NormalBeepPath),
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
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)));
        this.statusIconService = statusIconService ?? throw new ArgumentNullException(nameof(statusIconService));
        this.statusIconService.ActionRequested += this.StatusIconActionRequested;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        LinuxAppSettings settings = await this.LoadDocumentAsync(SettingsKey, LinuxAppSettings.Default, cancellationToken)
            .ConfigureAwait(true);
        ActiveTimerSessionsDocument activeSessions = await this.LoadActiveSessionsAsync(cancellationToken)
            .ConfigureAwait(true);

        bool restoredAny = false;
        if (settings.RestoreActiveSessionOnStartup)
        {
            foreach (ActiveTimerSessionDefinition session in activeSessions.Sessions)
            {
                if (session.Session != null && this.CreateWindow(session.SessionId, session.Session) != null)
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

        if (!restoredAny)
        {
            this.CreateWindow();
        }
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
        SavedTimerDefinition? savedTimer = null)
    {
        var viewModel = new MainWindowViewModel(
            new CountdownEngine(new SystemMonotonicClock()),
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
            restoreActiveSessionOnLoad: false);
        var window = new MainWindow(
            viewModel,
            new UnsupportedDesktopProgressService(),
            UnsupportedStatusIconService.Instance,
            loadSettingsOnOpened: false,
            prepareCoordinatorClose: this.PrepareWindowCloseAsync);
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

        _ = this.LoadWindowAsync(registration, session, savedTimer);
        window.Show();
        _ = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
        return window;
    }

    private async Task LoadWindowAsync(
        WindowRegistration registration,
        ActiveTimerSessionDocument? session,
        SavedTimerDefinition? savedTimer)
    {
        await registration.ViewModel.LoadSettingsAsync().ConfigureAwait(true);

        if (session != null && !registration.ViewModel.RestoreActiveSession(session))
        {
            registration.Window.Close();
            return;
        }

        if (savedTimer != null)
        {
            registration.ViewModel.ApplySavedTimer(savedTimer);
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

    private void ViewModelNewTimerRequested(object? sender, EventArgs e)
    {
        this.CreateWindow();
    }

    private async void ViewModelOpenAllSavedTimersRequested(object? sender, EventArgs e)
    {
        SavedTimersDocument savedTimers = await this.LoadDocumentAsync(
            SavedTimersKey,
            SavedTimersDocument.Empty,
            CancellationToken.None).ConfigureAwait(true);

        foreach (SavedTimerDefinition savedTimer in savedTimers.Timers)
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
        if (sender is MainWindowViewModel viewModel)
        {
            WindowRegistration? registration = this.windows.FirstOrDefault(item => item.ViewModel == viewModel);
            if (registration != null)
            {
                this.mostRecentWindow = registration;
            }
        }

        if (e.PropertyName is nameof(MainWindowViewModel.DesktopProgressRequest))
        {
            this.ApplyDesktopProgress();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.StatusIconMenuState)
            or nameof(MainWindowViewModel.WindowTitle))
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

        _ = this.QueueSessionSave();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();

        if (this.windows.Count == 0 && !this.isShuttingDown)
        {
            this.isShuttingDown = true;
            this.lifetime.Shutdown();
        }
    }

    private void StatusIconActionRequested(object? sender, StatusIconActionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.HandleStatusIconAction(e.Action));
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
                this.CloseAllWindows();
                break;
        }
    }

    private void CloseAllWindows()
    {
        foreach (WindowRegistration registration in this.windows.ToArray())
        {
            new WindowAttentionController(registration.Window).RequestAttention();
            registration.Window.Close();
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
        StatusIconMenuState state = target?.ViewModel.StatusIconMenuState
            ?? new StatusIconMenuState(
                "Hourglass",
                false,
                "Pause",
                false,
                false,
                false,
                false,
                true);

        try
        {
            await this.statusIconService.UpdateAsync(state).ConfigureAwait(true);
        }
        catch (Exception)
        {
        }
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
