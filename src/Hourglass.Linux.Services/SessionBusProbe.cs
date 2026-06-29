namespace Hourglass.Linux.Services;

public interface ISessionBusProbe
{
    bool IsSessionBusAvailable();
}

public sealed class EnvironmentSessionBusProbe(IDesktopEnvironmentReader environmentReader) : ISessionBusProbe
{
    public const string SessionBusAddressVariable = "DBUS_SESSION_BUS_ADDRESS";

    private readonly IDesktopEnvironmentReader environmentReader =
        environmentReader ?? throw new ArgumentNullException(nameof(environmentReader));

    public bool IsSessionBusAvailable()
    {
        return !string.IsNullOrWhiteSpace(
            this.environmentReader.GetEnvironmentVariable(SessionBusAddressVariable));
    }
}
