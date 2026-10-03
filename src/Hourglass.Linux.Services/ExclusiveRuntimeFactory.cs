namespace Hourglass.Linux.Services;

using Hourglass.Application;
using Hourglass.Platform;

public enum ExclusiveRuntimePurpose { Query, Sessions, Control, Tui }

public interface IExclusiveRuntimeFactory
{
    Task<ApplicationResult<ExclusiveRuntimeLease>> OpenAsync(ExclusiveRuntimePurpose purpose, CancellationToken cancellationToken);
}

public sealed class ExclusiveRuntimeLease(IHourglassClient client, Func<ValueTask> dispose) : IAsyncDisposable
{
    public IHourglassClient Client { get; } = client;
    public ValueTask DisposeAsync() => dispose();
}

public sealed class ExclusiveRuntimeFactory : IExclusiveRuntimeFactory
{
    private readonly Func<ISingleInstanceService> authority;
    private readonly Func<HourglassRuntime> createRuntime;
    private readonly ISettingsPathService settingsPaths;
    private readonly ISettingsStore settingsProbe;
    private readonly string? controlPath;
    private readonly HostBootstrap? host;

    public ExclusiveRuntimeFactory() : this(() => new LinuxFileLockSingleInstanceService(), CreateRuntime, new XdgSettingsPathService(), controlPath: RuntimeControlTransport.DefaultPath, host: new HostBootstrap()) { }

    public ExclusiveRuntimeFactory(Func<ISingleInstanceService> authority, Func<HourglassRuntime> createRuntime,
        ISettingsPathService? settingsPaths = null, ISettingsStore? settingsProbe = null, string? controlPath = null, HostBootstrap? host = null)
    {
        this.controlPath = controlPath;
        this.host = host;
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.createRuntime = createRuntime ?? throw new ArgumentNullException(nameof(createRuntime));
        this.settingsPaths = settingsPaths ?? new XdgSettingsPathService();
        this.settingsProbe = settingsProbe ?? new JsonFileSettingsStore(this.settingsPaths);
    }

    public async Task<ApplicationResult<ExclusiveRuntimeLease>> OpenAsync(ExclusiveRuntimePurpose purpose, CancellationToken cancellationToken)
    {
        ISingleInstanceService? ownership = null;
        HourglassRuntime? runtime = null;
        RuntimeControlServer? control = null;
        try
        {
            ownership = this.authority();
            if (!await ownership.TryAcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                ownership.Dispose();
                if (this.controlPath == null) { return Failed(ApplicationErrorCode.RuntimeUnavailable, "Another runtime is active."); }
                return await this.ConnectAsync(purpose, cancellationToken).ConfigureAwait(false);
            }
            if (this.host != null && purpose != ExclusiveRuntimePurpose.Query)
            {
                ownership.Dispose(); ownership = null;
                if (await HasUnreadableRecoveryAsync(this.settingsPaths, this.settingsProbe, cancellationToken).ConfigureAwait(false)) { return Failed(ApplicationErrorCode.PersistenceFailure, "Unreadable recovery document; restore or dismiss it through the GUI."); }
                ApplicationResult<RemoteHourglassClient> connected = await this.host.StartAsync(Kind(purpose), cancellationToken).ConfigureAwait(false);
                return connected is ApplicationResult<RemoteHourglassClient>.Success ready
                    ? new ApplicationResult<ExclusiveRuntimeLease>.Success(new(ready.Value, ready.Value.DisposeAsync))
                    : new ApplicationResult<ExclusiveRuntimeLease>.Failure(((ApplicationResult<RemoteHourglassClient>.Failure)connected).Error);
            }
            if (purpose == ExclusiveRuntimePurpose.Sessions && await HasUnreadableRecoveryAsync(this.settingsPaths, this.settingsProbe, cancellationToken).ConfigureAwait(false))
            {
                ownership.Dispose();
                return Failed(ApplicationErrorCode.RuntimeUnavailable, "An unreadable recovery document is present; restore or dismiss it through the GUI.");
            }
            runtime = this.createRuntime();
            if (purpose == ExclusiveRuntimePurpose.Sessions)
            {
                ApplicationResult<ApplicationDataSnapshot> readiness = await runtime.PrepareForegroundRuntimeAsync(cancellationToken).ConfigureAwait(false);
                if (readiness is ApplicationResult<ApplicationDataSnapshot>.Failure failed)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    ownership.Dispose();
                    return new ApplicationResult<ExclusiveRuntimeLease>.Failure(failed.Error);
                }
                runtime.StartScheduler();
                if (this.controlPath != null)
                {
                    control = new RuntimeControlServer(runtime, this.controlPath);
                    control.Start();
                    await ownership.StartRequestListenerAsync(GuiLaunchBridge.ForwardAsync, cancellationToken).ConfigureAwait(false);
                }
            }
            HourglassRuntime ownedRuntime = runtime;
            ISingleInstanceService ownedAuthority = ownership;
            RuntimeControlServer? ownedControl = control;
            return new ApplicationResult<ExclusiveRuntimeLease>.Success(new(runtime, async () =>
            {
                try
                {
                    if (ownedControl != null) { await ownedControl.DisposeAsync().ConfigureAwait(false); }
                    await ownedRuntime.FlushPersistenceAsync().ConfigureAwait(false);
                    await ownedRuntime.DisposeAsync().ConfigureAwait(false);
                }
                finally { ownedAuthority.Dispose(); }
            }));
        }
        catch (Exception exception)
        {
            if (control != null) { await control.DisposeAsync().ConfigureAwait(false); }
            if (runtime != null) { await runtime.DisposeAsync().ConfigureAwait(false); }
            ownership?.Dispose();
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) { throw; }
            return Failed(exception is IOException or UnauthorizedAccessException ? ApplicationErrorCode.PersistenceFailure
                : ApplicationErrorCode.RuntimeUnavailable, exception.Message);
        }
    }

    private async Task<ApplicationResult<ExclusiveRuntimeLease>> ConnectAsync(ExclusiveRuntimePurpose purpose, CancellationToken cancellationToken)
    {
        ApplicationResult<RemoteHourglassClient> result = await RemoteHourglassClient.ConnectWhenReadyAsync(this.controlPath, cancellationToken, Kind(purpose)).ConfigureAwait(false);
        return result is ApplicationResult<RemoteHourglassClient>.Success connected
            ? new ApplicationResult<ExclusiveRuntimeLease>.Success(new(connected.Value, connected.Value.DisposeAsync))
            : new ApplicationResult<ExclusiveRuntimeLease>.Failure(((ApplicationResult<RemoteHourglassClient>.Failure)result).Error);
    }

    public static async Task<bool> HasUnreadableRecoveryAsync(ISettingsPathService settingsPaths, ISettingsStore settingsProbe, CancellationToken cancellationToken)
    {
        string directory = settingsPaths.GetSettingsDirectory();
        if (File.Exists(Path.Combine(directory, "active-sessions.json")))
        {
            return await settingsProbe.LoadAsync<Hourglass.Settings.ActiveTimerSessionsDocument>("active-sessions", cancellationToken).ConfigureAwait(false) == null;
        }
        return File.Exists(Path.Combine(directory, "active-session.json"))
            && await settingsProbe.LoadAsync<Hourglass.Settings.ActiveTimerSessionDocument>("active-session", cancellationToken).ConfigureAwait(false) == null;
    }

    private static RuntimeClientKind Kind(ExclusiveRuntimePurpose purpose) => purpose switch { ExclusiveRuntimePurpose.Tui => RuntimeClientKind.Tui, ExclusiveRuntimePurpose.Sessions => RuntimeClientKind.Foreground, _ => RuntimeClientKind.Control };

    private static ApplicationResult<ExclusiveRuntimeLease> Failed(ApplicationErrorCode code, string message) => new ApplicationResult<ExclusiveRuntimeLease>.Failure(new(code, message));

    public static HourglassRuntime CreateRuntime()
    {
        IDiagnosticSink diagnostics = new DiagnosticJournal();
        var services = new SessionRuntimeServices(new NotifySendNotificationService(diagnostics),
            new LinuxAudioAlertService(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"), diagnostics),
            new SystemdSessionInhibitor(diagnostics), UnsupportedSystemPowerService.Instance,
            "Hourglass", "Timer completed.", "Timer running");
        var runtime = new HourglassRuntime(diagnostics, services: services, settingsStore: new JsonFileSettingsStore(new XdgSettingsPathService(), diagnostics));
        runtime.ConfigureWakeAlarms(new RtcWakeAlarmService());
        return runtime;
    }
}
