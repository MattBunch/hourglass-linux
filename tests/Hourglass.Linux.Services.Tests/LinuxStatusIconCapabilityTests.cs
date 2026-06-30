namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Xunit;

public sealed class LinuxStatusIconCapabilityTests
{
    [Fact]
    public void RecognizedDesktopWithSessionBusIsSupported()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "KDE"
            },
            sessionBusAvailable: true);

        Assert.True(capability.IsSupported());
    }

    [Fact]
    public void DesktopMatchingIsCaseInsensitive()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "x-CiNnAmOn"
            },
            sessionBusAvailable: true);

        Assert.True(capability.IsSupported());
    }

    [Fact]
    public void ColonSeparatedCurrentDesktopValuesAreParsed()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME:kde"
            },
            sessionBusAvailable: true);

        Assert.True(capability.IsSupported());
    }

    [Fact]
    public void UnknownDesktopIsUnsupported()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME"
            },
            sessionBusAvailable: true);

        Assert.False(capability.IsSupported());
    }

    [Fact]
    public void MissingSessionBusIsUnsupported()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "KDE"
            },
            sessionBusAvailable: false);

        Assert.False(capability.IsSupported());
    }

    [Fact]
    public void ForcedNoneIsUnsupported()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "KDE",
                [LinuxStatusIconCapability.BackendOverrideVariable] = "none"
            },
            sessionBusAvailable: true);

        Assert.False(capability.IsSupported());
    }

    [Fact]
    public void ForcedAvaloniaUsesSessionBusCapability()
    {
        var supported = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME",
                [LinuxStatusIconCapability.BackendOverrideVariable] = "avalonia"
            },
            sessionBusAvailable: true);
        var unsupported = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "KDE",
                [LinuxStatusIconCapability.BackendOverrideVariable] = "avalonia"
            },
            sessionBusAvailable: false);

        Assert.True(supported.IsSupported());
        Assert.False(unsupported.IsSupported());
    }

    [Fact]
    public void MissingOrEmptyDesktopVariablesDoNotThrow()
    {
        var capability = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "",
                [LinuxDesktopProgressServiceFactory.XdgSessionDesktopVariable] = null,
                [LinuxDesktopProgressServiceFactory.DesktopSessionVariable] = " "
            },
            sessionBusAvailable: true);

        Assert.False(capability.IsSupported());
    }

    [Fact]
    public void SessionDesktopAndDesktopSessionAreConsidered()
    {
        var sessionDesktop = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgSessionDesktopVariable] = "xfce"
            },
            sessionBusAvailable: true);
        var desktopSession = CreateCapability(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.DesktopSessionVariable] = "plasma"
            },
            sessionBusAvailable: true);

        Assert.True(sessionDesktop.IsSupported());
        Assert.True(desktopSession.IsSupported());
    }

    private static LinuxStatusIconCapability CreateCapability(
        IReadOnlyDictionary<string, string?> environment,
        bool sessionBusAvailable)
    {
        return new LinuxStatusIconCapability(
            new FakeDesktopEnvironmentReader(environment),
            new FakeSessionBusProbe(sessionBusAvailable));
    }

    private sealed class FakeDesktopEnvironmentReader(IReadOnlyDictionary<string, string?> values)
        : IDesktopEnvironmentReader
    {
        public string? GetEnvironmentVariable(string name)
        {
            return values.TryGetValue(name, out string? value) ? value : null;
        }
    }

    private sealed class FakeSessionBusProbe(bool available) : ISessionBusProbe
    {
        public bool IsSessionBusAvailable()
        {
            return available;
        }
    }
}
