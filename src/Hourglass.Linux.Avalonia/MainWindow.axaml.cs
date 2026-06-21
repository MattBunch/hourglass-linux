using Avalonia;
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
        this.Opened += async (_, _) =>
        {
            this.UpdateResponsiveLayout();
            await this.viewModel.LoadSettingsAsync();
        };
        this.SizeChanged += (_, _) => this.UpdateResponsiveLayout();
        this.viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.State))
            {
                this.UpdatePresentationClasses();
            }
        };
        this.UpdatePresentationClasses();
    }

    private void UpdateResponsiveLayout()
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(
            this.Bounds.Width,
            this.Bounds.Height);

        this.InnerGrid.Margin = new Thickness(scale.InnerMargin);
        this.ControlsPanel.Margin = new Thickness(scale.ContentHorizontalMargin, 0);
        this.ControlsPanel.Spacing = scale.ContentSpacing;
        this.PrimaryTextGrid.MinHeight = scale.PrimaryRowMinimumHeight;

        this.TimerTitleTextBox.MaxFontSize = scale.SecondaryMaximumFontSize;
        this.TimerInputTextBox.MaxFontSize = scale.PrimaryMaximumFontSize;
        this.RemainingTimeTextBox.MaxFontSize = scale.PrimaryMaximumFontSize;
        this.CompletionTextBox.MaxFontSize = scale.PrimaryMaximumFontSize;

        ApplyCommandScale(this.StartButton, scale);
        ApplyCommandScale(this.PauseButton, scale);
        ApplyCommandScale(this.ResumeButton, scale);
        ApplyCommandScale(this.StopButton, scale);
        ApplyCommandScale(this.CancelButton, scale);
    }

    private static void ApplyCommandScale(Button button, ResponsiveInterfaceScale scale)
    {
        button.FontSize = scale.CommandFontSize;
        button.Padding = new Thickness(scale.CommandHorizontalPadding, 0);
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

    private void TimerTitleTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        this.TryEnterInputMode(this.TimerTitleTextBox);
    }

    private void CompletionTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        this.TryEnterInputMode(this.TimerInputTextBox);
    }

    private void RemainingTimeTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        this.TryEnterTimerInputMode();
    }

    private void RemainingTimeTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.TryEnterTimerInputMode())
        {
            e.Handled = true;
        }
    }

    private bool TryEnterTimerInputMode()
    {
        if (!this.viewModel.TryEnterTimerInputMode())
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            this.TimerInputTextBox.Focus();
            this.TimerInputTextBox.SelectAll();
        });
        return true;
    }

    private void CompletionTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.TryEnterInputMode(this.TimerInputTextBox))
        {
            e.Handled = true;
        }
    }

    private bool TryEnterInputMode(TextBox textBoxToFocus)
    {
        if (!this.viewModel.TryEnterInputModeFromExpired())
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            textBoxToFocus.Focus();
            textBoxToFocus.SelectAll();
        });
        return true;
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
