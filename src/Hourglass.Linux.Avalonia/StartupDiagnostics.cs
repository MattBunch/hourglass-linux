namespace Hourglass.Linux.Avalonia;

internal sealed class StartupDiagnostics
{
    private const string DiagnosticsEnvironmentVariable = "HOURGLASS_STARTUP_DIAGNOSTICS";
    private const string EnabledValue = "1";
    private const string DisplayEnvironmentVariable = "DISPLAY";
    private const string WaylandDisplayEnvironmentVariable = "WAYLAND_DISPLAY";
    private const string SessionTypeEnvironmentVariable = "XDG_SESSION_TYPE";

    internal static StartupDiagnostics Disabled { get; } = new(_ => null, TextWriter.Null);

    private readonly Func<string, string?> environmentVariableReader;
    private readonly bool isEnabled;
    private readonly TextWriter writer;

    internal StartupDiagnostics(Func<string, string?> environmentVariableReader, TextWriter writer)
    {
        this.environmentVariableReader = environmentVariableReader ?? throw new ArgumentNullException(nameof(environmentVariableReader));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isEnabled = string.Equals(
            this.environmentVariableReader(DiagnosticsEnvironmentVariable),
            EnabledValue,
            StringComparison.Ordinal);
    }

    internal static StartupDiagnostics CreateDefault(TextWriter writer)
    {
        return new StartupDiagnostics(Environment.GetEnvironmentVariable, writer);
    }

    internal void Record(StartupStage stage)
    {
        this.Write(FormattableString.Invariant($"[hourglass-startup] stage={stage}"));
    }

    internal void RecordDisplayContext()
    {
        this.Write(FormattableString.Invariant(
            $"[hourglass-startup] context pid={Environment.ProcessId} display={this.Read(DisplayEnvironmentVariable)} wayland-display={this.Read(WaylandDisplayEnvironmentVariable)} session-type={this.Read(SessionTypeEnvironmentVariable)}"));
    }

    internal void RecordException(StartupStage stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        this.Write(FormattableString.Invariant(
            $"[hourglass-startup] stage={stage} exception={exception.GetType().FullName} message={exception.Message}"));
    }

    internal void RecordServiceStarting(StartupService service)
    {
        this.Write(FormattableString.Invariant($"[hourglass-startup] service={service} state=Starting"));
    }

    internal void RecordServiceCompleted(StartupService service)
    {
        this.Write(FormattableString.Invariant($"[hourglass-startup] service={service} state=Completed"));
    }

    private string Read(string name)
    {
        return this.environmentVariableReader(name) ?? "<unset>";
    }

    private void Write(string line)
    {
        if (!this.isEnabled)
        {
            return;
        }

        try
        {
            this.writer.WriteLine(line);
            this.writer.Flush();
        }
        catch (Exception)
        {
            // Startup diagnostics must not change application behavior.
        }
    }
}

internal enum StartupStage
{
    ProcessEntry,
    CommandLineParsed,
    SingleInstanceAcquired,
    SingleInstanceForwarded,
    RequestListenerStarted,
    AppBuilderCreated,
    PlatformDetectionConfigured,
    DesktopLifetimeStarting,
    DesktopLifetimeFailed,
    AppInitialize,
    AppXamlLoaded,
    FrameworkInitialization,
    ClassicDesktopLifetimeConfigured,
    CoordinatorConstructionStarting,
    CoordinatorConstructionFailed,
    CoordinatorCreated,
    CoordinatorStartScheduled,
    CoordinatorStarting,
    SettingsLoaded,
    MainWindowConstructionStarting,
    MainWindowConstructed,
    MainWindowShowCalled,
    MainWindowOpened,
    MainWindowActivated,
    CoordinatorStartFailed,
    UnhandledException,
    UnobservedTaskException
}

internal enum StartupService
{
    DiagnosticSink,
    DesktopEnvironmentReader,
    SessionBusProbe,
    XdgSettingsPathService,
    SettingsStore,
    NotificationService,
    AudioAlertService,
    SystemdSessionInhibitor,
    SessionInhibitor,
    DesktopProgressService,
    StatusIconService,
    StatusIconCapability,
    StatusIconAsset,
    AvaloniaStatusIconService,
    ExternalUriLauncher,
    SystemPowerService,
    WakeAlarmService,
    ApplicationInfoProvider
}
