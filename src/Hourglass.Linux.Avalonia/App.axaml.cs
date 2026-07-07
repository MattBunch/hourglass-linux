using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Hourglass.Linux.Avalonia;

public sealed partial class App : Application
{
    private TimerWindowCoordinator? coordinator;

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
            _ = this.coordinator.StartAsync();
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
            await this.coordinator.DisposeAsync();
            this.coordinator = null;
        }
    }
}
