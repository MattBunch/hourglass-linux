using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

public sealed partial class App : Application
{
    private TimerWindowCoordinator? coordinator;
    private readonly StartupDiagnostics startupDiagnostics;

    internal static SingleInstanceLaunchRequest? InitialLaunchRequest { get; set; }

    public App()
        : this(StartupDiagnostics.Disabled)
    {
    }

    internal App(StartupDiagnostics startupDiagnostics)
    {
        this.startupDiagnostics = startupDiagnostics ?? throw new ArgumentNullException(nameof(startupDiagnostics));
    }

    public override void Initialize()
    {
        this.startupDiagnostics.Record(StartupStage.AppInitialize);
        AvaloniaXamlLoader.Load(this);
        this.startupDiagnostics.Record(StartupStage.AppXamlLoaded);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        this.startupDiagnostics.Record(StartupStage.FrameworkInitialization);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            this.startupDiagnostics.Record(StartupStage.ClassicDesktopLifetimeConfigured);
            this.coordinator = new TimerWindowCoordinator(desktop, this.startupDiagnostics);
            this.startupDiagnostics.Record(StartupStage.CoordinatorCreated);
            desktop.Exit += this.DesktopExit;
            SingleInstanceLaunchRequest? initialRequest = InitialLaunchRequest;
            InitialLaunchRequest = null;
            _ = this.StartCoordinatorAsync(initialRequest);
            this.startupDiagnostics.Record(StartupStage.CoordinatorStartScheduled);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartCoordinatorAsync(SingleInstanceLaunchRequest? initialRequest)
    {
        if (this.coordinator == null)
        {
            return;
        }

        try
        {
            await this.coordinator.StartAsync(initialRequest).ConfigureAwait(true);
            SingleInstanceLaunchRequestDispatcher.Shared.Register(this.coordinator.HandleLaunchRequestAsync);
        }
        catch (Exception exception)
        {
            this.startupDiagnostics.RecordException(StartupStage.CoordinatorStartFailed, exception);
            throw;
        }
    }

    private async void DesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= this.DesktopExit;
        }

        if (this.coordinator != null)
        {
            SingleInstanceLaunchRequestDispatcher.Shared.Unregister(this.coordinator.HandleLaunchRequestAsync);
            await this.coordinator.DisposeAsync();
            this.coordinator = null;
        }
    }
}
