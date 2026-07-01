namespace Hourglass.Linux.Services;

public sealed class LinuxStatusIconCapability
{
    public const string BackendOverrideVariable = "HOURGLASS_STATUS_ICON_BACKEND";

    private readonly IDesktopEnvironmentReader environmentReader;
    private readonly ISessionBusProbe sessionBusProbe;

    public LinuxStatusIconCapability(
        IDesktopEnvironmentReader environmentReader,
        ISessionBusProbe sessionBusProbe)
    {
        this.environmentReader = environmentReader ?? throw new ArgumentNullException(nameof(environmentReader));
        this.sessionBusProbe = sessionBusProbe ?? throw new ArgumentNullException(nameof(sessionBusProbe));
    }

    public bool IsSupported()
    {
        StatusIconBackendOverride backendOverride = ParseBackendOverride(
            this.environmentReader.GetEnvironmentVariable(BackendOverrideVariable));

        return backendOverride switch
        {
            StatusIconBackendOverride.None => false,
            StatusIconBackendOverride.Avalonia => this.sessionBusProbe.IsSessionBusAvailable(),
            _ => this.sessionBusProbe.IsSessionBusAvailable() && this.IsRecognizedStatusIconDesktop()
        };
    }

    private bool IsRecognizedStatusIconDesktop()
    {
        return this.DesktopIdentifiers().Any(static identifier => identifier is
            "kde"
            or "plasma"
            or "lxqt"
            or "unity"
            or "ubuntu"
            or "ubuntu-wayland"
            or "ubuntu-xorg"
            or "xfce"
            or "x-cinnamon"
            or "cinnamon");
    }

    private IEnumerable<string> DesktopIdentifiers()
    {
        foreach (string variable in new[]
        {
            LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable,
            LinuxDesktopProgressServiceFactory.XdgSessionDesktopVariable,
            LinuxDesktopProgressServiceFactory.DesktopSessionVariable
        })
        {
            string? value = this.environmentReader.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (string identifier in value.Split(
                ':',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return identifier.ToLowerInvariant();
            }
        }
    }

    private static StatusIconBackendOverride ParseBackendOverride(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "auto"
            : value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "none" => StatusIconBackendOverride.None,
            "avalonia" => StatusIconBackendOverride.Avalonia,
            _ => StatusIconBackendOverride.Auto
        };
    }

    private enum StatusIconBackendOverride
    {
        Auto,
        Avalonia,
        None
    }
}
