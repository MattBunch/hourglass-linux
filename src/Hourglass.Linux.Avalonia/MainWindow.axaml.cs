using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hourglass.Linux.Services;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindow : Window, IWindowAttentionTarget
{
    private static readonly string NormalBeepPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds",
        "BeepNormal.wav");

    private const double RefreshIntervalMilliseconds = 250;
    private const double ExpiryFlashDurationMilliseconds = 420;
    private const double ValidationFeedbackDurationMilliseconds = 650;

    private readonly DispatcherTimer expiryFlashTimer;
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer validationFeedbackTimer;
    private readonly MainWindowViewModel viewModel;
    private readonly WindowAttentionController? windowAttentionController;
    private int expiryFlashGeneration;
    private bool focusWithinContent;
    private bool pointerWithinContent = true;
    private int validationFeedbackGeneration;

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
        this.windowAttentionController = new WindowAttentionController(this);

        this.refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RefreshIntervalMilliseconds)
        };
        this.refreshTimer.Tick += (_, _) => this.viewModel.Tick();
        this.refreshTimer.Start();

        this.expiryFlashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ExpiryFlashDurationMilliseconds)
        };
        this.expiryFlashTimer.Tick += this.ExpiryFlashTimerTick;

        this.validationFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ValidationFeedbackDurationMilliseconds)
        };
        this.validationFeedbackTimer.Tick += this.ValidationFeedbackTimerTick;

        this.Closed += this.WindowClosed;
        this.Opened += this.WindowOpened;
        this.SizeChanged += (_, _) => this.UpdateResponsiveLayout();
        this.viewModel.PropertyChanged += this.ViewModelPropertyChanged;
        this.viewModel.WindowAttentionRequested += this.WindowAttentionRequested;
        this.viewModel.ExpiryVisualFeedbackRequested += this.ExpiryVisualFeedbackRequested;
        this.viewModel.ValidationFeedbackRequested += this.ValidationFeedbackRequested;
        this.UpdatePresentationClasses();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            this.windowAttentionController?.RecordWindowState(this.WindowState);
        }
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
        this.RootGrid.Classes.Set("timer-active", this.viewModel.State == TimerState.Running);
        this.RootGrid.Classes.Set("timer-expired", this.viewModel.HasCompletionEmphasis);
        this.RootGrid.Classes.Set("content-active", this.pointerWithinContent || this.focusWithinContent);
        this.TimerInputTextBox.Classes.Set("validation-error", this.viewModel.HasValidationError);

        if (!this.viewModel.HasCompletionEmphasis)
        {
            this.expiryFlashGeneration++;
            this.expiryFlashTimer.Stop();
            this.RootGrid.Classes.Set("timer-expiry-flash", false);
        }

        if (!this.viewModel.HasValidationError)
        {
            this.validationFeedbackGeneration++;
            this.validationFeedbackTimer.Stop();
            this.TimerInputTextBox.Classes.Set("validation-feedback", false);
        }
    }

    private async void WindowOpened(object? sender, EventArgs e)
    {
        this.UpdateResponsiveLayout();
        await this.viewModel.LoadSettingsAsync();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        this.refreshTimer.Stop();
        this.expiryFlashTimer.Stop();
        this.validationFeedbackTimer.Stop();
        this.RootGrid.Classes.Set("timer-expiry-flash", false);
        this.TimerInputTextBox.Classes.Set("validation-feedback", false);
        this.viewModel.PropertyChanged -= this.ViewModelPropertyChanged;
        this.viewModel.WindowAttentionRequested -= this.WindowAttentionRequested;
        this.viewModel.ExpiryVisualFeedbackRequested -= this.ExpiryVisualFeedbackRequested;
        this.viewModel.ValidationFeedbackRequested -= this.ValidationFeedbackRequested;
        this.viewModel.Dispose();
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.State)
            or nameof(MainWindowViewModel.HasCompletionEmphasis)
            or nameof(MainWindowViewModel.HasValidationError))
        {
            this.UpdatePresentationClasses();
        }
    }

    private void WindowAttentionRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.windowAttentionController?.RequestAttention());
    }

    private void ExpiryVisualFeedbackRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(this.RestartExpiryFlash);
    }

    private void ValidationFeedbackRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.TimerInputTextBox.Focus();
            this.TimerInputTextBox.SelectAll();
            this.RestartValidationFeedback();
        });
    }

    private void RestartExpiryFlash()
    {
        this.expiryFlashGeneration++;
        int generation = this.expiryFlashGeneration;
        this.expiryFlashTimer.Stop();
        this.RootGrid.Classes.Set("timer-expiry-flash", false);

        if (!ShouldAnimateVisualFeedback())
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (generation != this.expiryFlashGeneration)
            {
                return;
            }

            this.RootGrid.Classes.Set("timer-expiry-flash", true);
            this.expiryFlashTimer.Start();
        }, DispatcherPriority.Render);
    }

    private void RestartValidationFeedback()
    {
        this.validationFeedbackGeneration++;
        int generation = this.validationFeedbackGeneration;
        this.validationFeedbackTimer.Stop();
        this.TimerInputTextBox.Classes.Set("validation-feedback", false);

        if (!ShouldAnimateVisualFeedback())
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (generation != this.validationFeedbackGeneration)
            {
                return;
            }

            this.TimerInputTextBox.Classes.Set("validation-feedback", true);
            this.validationFeedbackTimer.Start();
        }, DispatcherPriority.Render);
    }

    private void ExpiryFlashTimerTick(object? sender, EventArgs e)
    {
        this.expiryFlashTimer.Stop();
        this.RootGrid.Classes.Set("timer-expiry-flash", false);
    }

    private void ValidationFeedbackTimerTick(object? sender, EventArgs e)
    {
        this.validationFeedbackTimer.Stop();
        this.TimerInputTextBox.Classes.Set("validation-feedback", false);
    }

    private static bool ShouldAnimateVisualFeedback()
    {
        // Avalonia 12 does not expose a reliable cross-desktop reduced-motion preference.
        // Keep feedback short and single-pass until a dependable platform signal is available.
        return true;
    }
}
