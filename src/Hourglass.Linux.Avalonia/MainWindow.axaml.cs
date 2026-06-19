using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hourglass.Linux.Services;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindow : Window
{
    private static readonly string NormalBeepPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds",
        "BeepNormal.wav");

    private const double RefreshIntervalMilliseconds = 250;

    private readonly DispatcherTimer refreshTimer;
    private readonly MainWindowViewModel viewModel;
    private bool focusWithinContent;
    private bool pointerWithinContent = true;

    public MainWindow()
        : this(new MainWindowViewModel(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            new NotifySendNotificationService(),
            new SystemdSessionInhibitor(),
            new JsonFileSettingsStore(new XdgSettingsPathService()),
            new LinuxAudioAlertService(NormalBeepPath)))
    {
    }

    internal MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.DataContext = this.viewModel;

        this.refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RefreshIntervalMilliseconds)
        };
        this.refreshTimer.Tick += (_, _) => this.viewModel.Tick();
        this.refreshTimer.Start();

        this.Closed += (_, _) => this.refreshTimer.Stop();
        this.Opened += async (_, _) => await this.viewModel.LoadSettingsAsync();
        this.viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.State))
            {
                this.UpdatePresentationClasses();
            }
        };
        this.UpdatePresentationClasses();
    }

    private void InnerGridPointerEntered(object? sender, PointerEventArgs e)
    {
        this.pointerWithinContent = true;
        this.UpdatePresentationClasses();
    }

    private void InnerGridPointerExited(object? sender, PointerEventArgs e)
    {
        this.pointerWithinContent = false;
        this.UpdatePresentationClasses();
    }

    private void InnerGridFocusChanged(object? sender, RoutedEventArgs e)
    {
        this.focusWithinContent = this.InnerGrid.IsKeyboardFocusWithin;
        this.UpdatePresentationClasses();
    }

    private async void ExitMenuItemClick(object? sender, RoutedEventArgs e)
    {
        await this.viewModel.PendingSettingsSave;
        this.Close();
    }

    private void UpdatePresentationClasses()
    {
        this.RootGrid.Classes.Set("timer-active", this.viewModel.State is TimerState.Running or TimerState.Expired);
        this.RootGrid.Classes.Set("content-active", this.pointerWithinContent || this.focusWithinContent);
    }
}
