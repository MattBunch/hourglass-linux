using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

public sealed partial class App : Application
{
    private TimerWindowCoordinator? coordinator;

    internal static SingleInstanceLaunchRequest? InitialLaunchRequest { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            this.coordinator = new TimerWindowCoordinator(desktop);
            desktop.Exit += this.DesktopExit;
            SingleInstanceLaunchRequest? initialRequest = InitialLaunchRequest;
            InitialLaunchRequest = null;
            SingleInstanceLaunchRequestDispatcher.Shared.Register(this.coordinator.HandleLaunchRequestAsync);
            _ = this.coordinator.StartAsync(initialRequest);
        }

        base.OnFrameworkInitializationCompleted();
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
