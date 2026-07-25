namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class LinuxDesktopProgressServiceFactory
{
    public const string BackendOverrideVariable = "HOURGLASS_DESKTOP_PROGRESS_BACKEND";
    public const string DesktopSessionVariable = "DESKTOP_SESSION";
    public const string XdgCurrentDesktopVariable = "XDG_CURRENT_DESKTOP";
    public const string XdgSessionDesktopVariable = "XDG_SESSION_DESKTOP";

    private readonly IDesktopEnvironmentReader environmentReader;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly IUnityLauncherEntrySenderFactory unitySenderFactory;

    public LinuxDesktopProgressServiceFactory(
        IDesktopEnvironmentReader environmentReader,
        IUnityLauncherEntrySenderFactory unitySenderFactory,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.environmentReader = environmentReader ?? throw new ArgumentNullException(nameof(environmentReader));
        this.unitySenderFactory = unitySenderFactory ?? throw new ArgumentNullException(nameof(unitySenderFactory));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public static IDesktopProgressService CreateDefault(IDiagnosticSink? diagnosticSink = null)
    {
        var environmentReader = new ProcessDesktopEnvironmentReader();

        return new LinuxDesktopProgressServiceFactory(
            environmentReader,
            new DbusUnityLauncherEntrySenderFactory(new EnvironmentSessionBusProbe(environmentReader)),
            diagnosticSink).Create();
    }

    public IDesktopProgressService Create()
    {
        DesktopProgressEnvironment environment = DesktopProgressEnvironment.Read(this.environmentReader);
        DesktopProgressBackendOverride backendOverride = ParseBackendOverride(environment.BackendOverride);

        return backendOverride switch
        {
            DesktopProgressBackendOverride.None => UnsupportedDesktopProgressService.Instance,
            DesktopProgressBackendOverride.Unity => this.CreateUnityOrUnsupported(),
            _ when environment.IsUnityLauncherEntryCompatible => this.CreateUnityOrUnsupported(),
            _ => UnsupportedDesktopProgressService.Instance
        };
    }

    private IDesktopProgressService CreateUnityOrUnsupported()
    {
        this.diagnosticSink.ResetDuplicateSuppression("desktop-progress", "initialize", "unity");
        if (this.unitySenderFactory.TryCreate(out IUnityLauncherEntrySender sender))
        {
            return new UnityLauncherDesktopProgressService(sender, this.diagnosticSink);
        }

        this.diagnosticSink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.StartupConfiguration,
            "desktop-progress",
            "initialize",
            "unity",
            "Unity launcher desktop progress backend could not be initialized."));
        return UnsupportedDesktopProgressService.Instance;
    }

    private static DesktopProgressBackendOverride ParseBackendOverride(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "auto"
            : value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "none" => DesktopProgressBackendOverride.None,
            "unity" => DesktopProgressBackendOverride.Unity,
            _ => DesktopProgressBackendOverride.Auto
        };
    }

    private enum DesktopProgressBackendOverride
    {
        Auto,
        Unity,
        None
    }

    private sealed record DesktopProgressEnvironment(
        string? XdgCurrentDesktop,
        string? XdgSessionDesktop,
        string? DesktopSession,
        string? BackendOverride)
    {
        private static readonly string[] UnityCompatibleDesktopIds =
        [
            "unity",
            "ubuntu",
            "ubuntu-wayland",
            "ubuntu-xorg"
        ];

        public bool IsUnityLauncherEntryCompatible =>
            this.DesktopIdentifiers.Any(identifier => UnityCompatibleDesktopIds.Contains(identifier));

        private IEnumerable<string> DesktopIdentifiers
        {
            get
            {
                foreach (string value in this.ReadValues())
                {
                    foreach (string identifier in SplitDesktopIdentifiers(value))
                    {
                        yield return identifier;
                    }
                }
            }
        }

        public static DesktopProgressEnvironment Read(IDesktopEnvironmentReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return new DesktopProgressEnvironment(
                reader.GetEnvironmentVariable(XdgCurrentDesktopVariable),
                reader.GetEnvironmentVariable(XdgSessionDesktopVariable),
                reader.GetEnvironmentVariable(DesktopSessionVariable),
                reader.GetEnvironmentVariable(BackendOverrideVariable));
        }

        private IEnumerable<string> ReadValues()
        {
            if (!string.IsNullOrWhiteSpace(this.XdgCurrentDesktop))
            {
                yield return this.XdgCurrentDesktop;
            }

            if (!string.IsNullOrWhiteSpace(this.XdgSessionDesktop))
            {
                yield return this.XdgSessionDesktop;
            }

            if (!string.IsNullOrWhiteSpace(this.DesktopSession))
            {
                yield return this.DesktopSession;
            }
        }

        private static IEnumerable<string> SplitDesktopIdentifiers(string value)
        {
            return value
                .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(identifier => identifier.ToLowerInvariant());
        }
    }
}
