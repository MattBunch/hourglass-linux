using Avalonia.Controls;
using Avalonia.Threading;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindow : Window
{
    private const double RefreshIntervalMilliseconds = 250;

    private readonly DispatcherTimer refreshTimer;
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        this.viewModel = new MainWindowViewModel();
        this.DataContext = this.viewModel;

        this.refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RefreshIntervalMilliseconds)
        };
        this.refreshTimer.Tick += (_, _) => this.viewModel.Tick();
        this.refreshTimer.Start();

        this.Closed += (_, _) => this.refreshTimer.Stop();
    }
}
