namespace Hourglass.Cli;

using Hourglass.Application;
using Hourglass.Linux.Services;
using Hourglass.Platform;

public enum CliRuntimePurpose { Query, Foreground }

public interface ICliRuntimeFactory
{
    Task<ApplicationResult<CliRuntimeLease>> OpenAsync(CliRuntimePurpose purpose, CancellationToken cancellationToken);
}

public sealed class CliRuntimeLease(IHourglassClient client, Func<ValueTask> dispose) : IAsyncDisposable
{
    public IHourglassClient Client { get; } = client;
    public ValueTask DisposeAsync() => dispose();
}

public sealed class LocalRuntimeFactory : ICliRuntimeFactory
{
    private readonly Func<ISingleInstanceService> authority;
    private readonly Func<HourglassRuntime> createRuntime;
    private readonly ISettingsPathService settingsPaths;
    private readonly ISettingsStore settingsProbe;

    public LocalRuntimeFactory() : this(() => new LinuxFileLockSingleInstanceService(), CreateRuntime, new XdgSettingsPathService()) { }

    public LocalRuntimeFactory(Func<ISingleInstanceService> authority, Func<HourglassRuntime> createRuntime,
        ISettingsPathService? settingsPaths = null, ISettingsStore? settingsProbe = null)
    {
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.createRuntime = createRuntime ?? throw new ArgumentNullException(nameof(createRuntime));
        this.settingsPaths = settingsPaths ?? new XdgSettingsPathService();
        this.settingsProbe = settingsProbe ?? new JsonFileSettingsStore(this.settingsPaths);
    }

    public async Task<ApplicationResult<CliRuntimeLease>> OpenAsync(CliRuntimePurpose purpose, CancellationToken cancellationToken)
    {
        ISingleInstanceService? ownership = null;
        HourglassRuntime? runtime = null;
        try
        {
            ownership = this.authority();
            if (!await ownership.TryAcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                ownership.Dispose();
                return Failed(ApplicationErrorCode.RuntimeUnavailable, "Another runtime is active. Cross-process control is not available in this interim CLI.");
            }
            if (purpose == CliRuntimePurpose.Foreground && await this.HasUnparsedRecoveryDocumentAsync(cancellationToken).ConfigureAwait(false))
            {
                ownership.Dispose();
                return Failed(ApplicationErrorCode.RuntimeUnavailable, "An unreadable recovery document is present; restore or dismiss it through the GUI.");
            }
            runtime = this.createRuntime();
            if (purpose == CliRuntimePurpose.Foreground)
            {
                ApplicationResult<ApplicationDataSnapshot> readiness = await runtime.PrepareForegroundRuntimeAsync(cancellationToken).ConfigureAwait(false);
                if (readiness is ApplicationResult<ApplicationDataSnapshot>.Failure failed)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    ownership.Dispose();
                    return new ApplicationResult<CliRuntimeLease>.Failure(failed.Error);
                }
                runtime.StartScheduler();
            }
            HourglassRuntime ownedRuntime = runtime;
            ISingleInstanceService ownedAuthority = ownership;
            return new ApplicationResult<CliRuntimeLease>.Success(new(runtime, async () =>
            {
                try { await ownedRuntime.DisposeAsync().ConfigureAwait(false); }
                finally { ownedAuthority.Dispose(); }
            }));
        }
        catch (Exception exception)
        {
            if (runtime != null) { await runtime.DisposeAsync().ConfigureAwait(false); }
            ownership?.Dispose();
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) { throw; }
            return Failed(exception is IOException or UnauthorizedAccessException ? ApplicationErrorCode.PersistenceFailure
                : ApplicationErrorCode.RuntimeUnavailable, exception.Message);
        }
    }

    private async Task<bool> HasUnparsedRecoveryDocumentAsync(CancellationToken cancellationToken)
    {
        string directory = this.settingsPaths.GetSettingsDirectory();
        if (File.Exists(Path.Combine(directory, "active-sessions.json")))
        {
            return await this.settingsProbe.LoadAsync<Hourglass.Settings.ActiveTimerSessionsDocument>("active-sessions", cancellationToken).ConfigureAwait(false) == null;
        }
        return File.Exists(Path.Combine(directory, "active-session.json"))
            && await this.settingsProbe.LoadAsync<Hourglass.Settings.ActiveTimerSessionDocument>("active-session", cancellationToken).ConfigureAwait(false) == null;
    }

    private static ApplicationResult<CliRuntimeLease> Failed(ApplicationErrorCode code, string message) => new ApplicationResult<CliRuntimeLease>.Failure(new(code, message));

    private static HourglassRuntime CreateRuntime()
    {
        IDiagnosticSink diagnostics = NoOpDiagnosticSink.Instance;
        var services = new SessionRuntimeServices(new NotifySendNotificationService(diagnostics),
            new LinuxAudioAlertService(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"), diagnostics),
            new SystemdSessionInhibitor(diagnostics), UnsupportedSystemPowerService.Instance,
            "Hourglass", "Timer completed.", "Timer running");
        var runtime = new HourglassRuntime(diagnostics, services: services, settingsStore: new JsonFileSettingsStore(new XdgSettingsPathService(), diagnostics));
        runtime.ConfigureWakeAlarms(new RtcWakeAlarmService());
        return runtime;
    }
}
