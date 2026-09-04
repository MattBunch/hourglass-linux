namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Hourglass.Platform;
using Xunit;

public sealed class LinuxDesktopProgressServiceFactoryTests
{
    [Fact]
    public void DeferredFactoryConstructionDoesNotInitializeUnitySender()
    {
        var senderFactory = new FakeUnityLauncherEntrySenderFactory(available: true);
        var factory = new LinuxDesktopProgressServiceFactory(
            new FakeDesktopEnvironmentReader(
                new Dictionary<string, string?>
                {
                    [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "ubuntu"
                }),
            senderFactory);

        IDesktopProgressService service = factory.CreateDeferred();

        Assert.IsType<DeferredDesktopProgressService>(service);
        Assert.Equal(0, senderFactory.CreateCalls);
    }

    [Fact]
    public void UnityCompatibleDesktopWithAvailableSenderSelectsUnityBackend()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "Unity"
            },
            senderAvailable: true);

        Assert.IsType<UnityLauncherDesktopProgressService>(service);
        Assert.True(service.IsSupported);
    }

    [Fact]
    public void DesktopMatchingIsCaseInsensitive()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "uBuNtU"
            },
            senderAvailable: true);

        Assert.IsType<UnityLauncherDesktopProgressService>(service);
    }

    [Fact]
    public void ColonSeparatedCurrentDesktopValuesAreParsed()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "ubuntu:GNOME"
            },
            senderAvailable: true);

        Assert.IsType<UnityLauncherDesktopProgressService>(service);
    }

    [Fact]
    public void UnknownDesktopSelectsUnsupportedBackend()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME"
            },
            senderAvailable: true);

        Assert.IsType<UnsupportedDesktopProgressService>(service);
        Assert.False(service.IsSupported);
    }

    [Fact]
    public void MissingSessionBusSelectsUnsupportedBackend()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "ubuntu:GNOME"
            },
            senderAvailable: false);

        Assert.IsType<UnsupportedDesktopProgressService>(service);
    }

    [Fact]
    public void ForcedNoneSelectsUnsupportedBackend()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "ubuntu:GNOME",
                [LinuxDesktopProgressServiceFactory.BackendOverrideVariable] = "none"
            },
            senderAvailable: true);

        Assert.IsType<UnsupportedDesktopProgressService>(service);
    }

    [Fact]
    public void ForcedUnitySelectsUnityBackendWhenInitializationSucceeds()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME",
                [LinuxDesktopProgressServiceFactory.BackendOverrideVariable] = "unity"
            },
            senderAvailable: true);

        Assert.IsType<UnityLauncherDesktopProgressService>(service);
    }

    [Fact]
    public void ForcedUnityFallsBackWhenInitializationFails()
    {
        var diagnostics = new RecordingDiagnosticSink();
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "GNOME",
                [LinuxDesktopProgressServiceFactory.BackendOverrideVariable] = "unity"
            },
            senderAvailable: false,
            diagnostics);

        Assert.IsType<UnsupportedDesktopProgressService>(service);
        DiagnosticEvent diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticFailureClass.StartupConfiguration, diagnostic.FailureClass);
        Assert.Equal("desktop-progress", diagnostic.Category);
        Assert.Equal("initialize", diagnostic.Operation);
        Assert.Equal("unity", diagnostic.Backend);
    }

    [Fact]
    public void MissingOrEmptyDesktopVariablesDoNotThrow()
    {
        IDesktopProgressService service = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgCurrentDesktopVariable] = "",
                [LinuxDesktopProgressServiceFactory.XdgSessionDesktopVariable] = null,
                [LinuxDesktopProgressServiceFactory.DesktopSessionVariable] = " "
            },
            senderAvailable: true);

        Assert.IsType<UnsupportedDesktopProgressService>(service);
    }

    [Fact]
    public void SessionDesktopAndDesktopSessionAreConsidered()
    {
        IDesktopProgressService sessionDesktopService = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.XdgSessionDesktopVariable] = "ubuntu-xorg"
            },
            senderAvailable: true);
        IDesktopProgressService desktopSessionService = CreateService(
            new Dictionary<string, string?>
            {
                [LinuxDesktopProgressServiceFactory.DesktopSessionVariable] = "ubuntu-wayland"
            },
            senderAvailable: true);

        Assert.IsType<UnityLauncherDesktopProgressService>(sessionDesktopService);
        Assert.IsType<UnityLauncherDesktopProgressService>(desktopSessionService);
    }

    [Fact]
    public void EnvironmentSessionBusProbeReadsInjectableEnvironment()
    {
        var unavailable = new EnvironmentSessionBusProbe(
            new FakeDesktopEnvironmentReader(new Dictionary<string, string?>()));
        var available = new EnvironmentSessionBusProbe(
            new FakeDesktopEnvironmentReader(
                new Dictionary<string, string?>
                {
                    [EnvironmentSessionBusProbe.SessionBusAddressVariable] = "unix:path=/run/user/1000/bus"
                }));

        Assert.False(unavailable.IsSessionBusAvailable());
        Assert.True(available.IsSessionBusAvailable());
    }

    private static IDesktopProgressService CreateService(
        IReadOnlyDictionary<string, string?> environment,
        bool senderAvailable,
        IDiagnosticSink? diagnosticSink = null)
    {
        var factory = new LinuxDesktopProgressServiceFactory(
            new FakeDesktopEnvironmentReader(environment),
            new FakeUnityLauncherEntrySenderFactory(senderAvailable),
            diagnosticSink);

        return factory.Create();
    }

    private sealed class FakeDesktopEnvironmentReader(IReadOnlyDictionary<string, string?> values)
        : IDesktopEnvironmentReader
    {
        public string? GetEnvironmentVariable(string name)
        {
            return values.TryGetValue(name, out string? value) ? value : null;
        }
    }

    private sealed class FakeUnityLauncherEntrySenderFactory(bool available)
        : IUnityLauncherEntrySenderFactory
    {
        public int CreateCalls { get; private set; }

        public bool TryCreate(out IUnityLauncherEntrySender sender)
        {
            this.CreateCalls++;
            sender = new FakeUnityLauncherEntrySender();
            return available;
        }
    }

    private sealed class FakeUnityLauncherEntrySender : IUnityLauncherEntrySender
    {
        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
