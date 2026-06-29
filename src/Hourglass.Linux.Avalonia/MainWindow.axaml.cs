using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindow : Window, IWindowAttentionTarget, IFullScreenWindowTarget
{
    private static readonly string NormalBeepPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds",
        "BeepNormal.wav");

    private const double RefreshIntervalMilliseconds = 250;
    private const double ExpiryFlashDurationMilliseconds = 420;
    private const double ValidationFeedbackDurationMilliseconds = 650;

    private readonly WindowCloseCoordinator closeCoordinator;
    private readonly DispatcherTimer completionCloseTimer;
    private readonly DesktopProgressController desktopProgressController;
    private readonly DispatcherTimer expiryFlashTimer;
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer validationFeedbackTimer;
    private readonly WindowFullScreenController fullScreenController;
    private readonly MainWindowViewModel viewModel;
    private readonly WindowAttentionController? windowAttentionController;
    private int expiryFlashGeneration;
    private bool focusWithinContent;
    private bool isClosed;
    private bool isClosePreparing;
    private bool pointerWithinContent = true;
    private int validationFeedbackGeneration;

    public MainWindow()
        : this(new MainWindowViewModel(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            new NotifySendNotificationService(),
            new SystemdSessionInhibitor(),
            new JsonFileSettingsStore(new XdgSettingsPathService()),
            new LinuxAudioAlertService(NormalBeepPath),
            new UnsupportedSystemPowerService()),
            LinuxDesktopProgressServiceFactory.CreateDefault())
    {
    }

    internal MainWindow(MainWindowViewModel viewModel)
        : this(viewModel, new UnsupportedDesktopProgressService())
    {
    }

    internal MainWindow(MainWindowViewModel viewModel, IDesktopProgressService desktopProgressService)
    {
        InitializeComponent();

        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)));
        this.DataContext = this.viewModel;
        this.windowAttentionController = new WindowAttentionController(this);
        this.fullScreenController = new WindowFullScreenController(this);
        this.closeCoordinator = new WindowCloseCoordinator(
            this.RequestCloseApprovalAsync,
            this.PrepareCloseAsync,
            () => Dispatcher.UIThread.Post(this.RequestFinalClose),
            this.CleanupAfterClose);

        this.refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RefreshIntervalMilliseconds)
        };
        this.refreshTimer.Tick += this.RefreshTimerTick;
        this.refreshTimer.Start();

        this.expiryFlashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ExpiryFlashDurationMilliseconds)
        };
        this.expiryFlashTimer.Tick += this.ExpiryFlashTimerTick;

        this.completionCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ExpiryFlashDurationMilliseconds)
        };
        this.completionCloseTimer.Tick += this.CompletionCloseTimerTick;

        this.validationFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ValidationFeedbackDurationMilliseconds)
        };
        this.validationFeedbackTimer.Tick += this.ValidationFeedbackTimerTick;

        this.Closing += this.WindowClosing;
        this.Closed += this.WindowClosed;
        this.Opened += this.WindowOpened;
        this.SizeChanged += this.WindowSizeChanged;
        this.AddHandler(KeyDownEvent, this.WindowKeyDown, RoutingStrategies.Tunnel);
        this.viewModel.PropertyChanged += this.ViewModelPropertyChanged;
        this.viewModel.WindowAttentionRequested += this.WindowAttentionRequested;
        this.viewModel.ExpiryVisualFeedbackRequested += this.ExpiryVisualFeedbackRequested;
        this.viewModel.ValidationFeedbackRequested += this.ValidationFeedbackRequested;
        this.viewModel.CloseRequested += this.CloseRequested;
        this.UpdatePresentationClasses();
        this.ApplyDesktopProgress();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            this.windowAttentionController?.RecordWindowState(this.WindowState);
            this.fullScreenController?.RecordWindowState(this.WindowState);

            if (this.FullScreenMenuItem != null)
            {
                this.FullScreenMenuItem.IsChecked = this.fullScreenController?.IsFullScreen == true;
            }
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
        ApplyCommandScale(this.RestartButton, scale);
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
            if (this.isClosed)
            {
                return;
            }

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
            if (this.isClosed)
            {
                return;
            }

            textBoxToFocus.Focus();
            textBoxToFocus.SelectAll();
        });
        return true;
    }

    private void ExitMenuItemClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void FullScreenMenuItemClick(object? sender, RoutedEventArgs e)
    {
        this.ToggleFullScreen();
    }

    private void ToggleFullScreen()
    {
        this.fullScreenController.Toggle();
        this.FullScreenMenuItem.IsChecked = this.fullScreenController.IsFullScreen;
    }

    private async Task<bool> RequestCloseApprovalAsync()
    {
        if (!this.viewModel.ShouldPromptOnExit)
        {
            return true;
        }

        var dialog = new ExitConfirmationWindow();
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task PrepareCloseAsync()
    {
        this.isClosePreparing = true;
        this.refreshTimer.Stop();
        await this.viewModel.PendingSettingsSave.ConfigureAwait(false);
        await this.desktopProgressController.ClearAsync().ConfigureAwait(false);
    }

    private void UpdatePresentationClasses()
    {
        this.RootGrid.Classes.Set("timer-active", this.viewModel.State == TimerState.Running);
        this.RootGrid.Classes.Set("timer-expired", this.viewModel.HasCompletionEmphasis);
        this.RootGrid.Classes.Set("timer-locked", this.viewModel.IsTimerModificationLocked);
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

    private void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = this.closeCoordinator.RequestClose();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        this.closeCoordinator.CompleteClose();
    }

    private void CleanupAfterClose()
    {
        this.isClosed = true;
        this.expiryFlashGeneration++;
        this.validationFeedbackGeneration++;
        this.refreshTimer.Stop();
        this.expiryFlashTimer.Stop();
        this.completionCloseTimer.Stop();
        this.validationFeedbackTimer.Stop();
        this.RootGrid.Classes.Set("timer-expiry-flash", false);
        this.TimerInputTextBox.Classes.Set("validation-feedback", false);
        this.viewModel.PropertyChanged -= this.ViewModelPropertyChanged;
        this.viewModel.WindowAttentionRequested -= this.WindowAttentionRequested;
        this.viewModel.ExpiryVisualFeedbackRequested -= this.ExpiryVisualFeedbackRequested;
        this.viewModel.ValidationFeedbackRequested -= this.ValidationFeedbackRequested;
        this.viewModel.CloseRequested -= this.CloseRequested;
        this.refreshTimer.Tick -= this.RefreshTimerTick;
        this.expiryFlashTimer.Tick -= this.ExpiryFlashTimerTick;
        this.completionCloseTimer.Tick -= this.CompletionCloseTimerTick;
        this.validationFeedbackTimer.Tick -= this.ValidationFeedbackTimerTick;
        this.Closing -= this.WindowClosing;
        this.Closed -= this.WindowClosed;
        this.Opened -= this.WindowOpened;
        this.SizeChanged -= this.WindowSizeChanged;
        this.RemoveHandler(KeyDownEvent, this.WindowKeyDown);
        this.viewModel.Dispose();
    }

    private void RequestFinalClose()
    {
        if (!this.isClosed)
        {
            this.Close();
        }
    }

    private void RefreshTimerTick(object? sender, EventArgs e)
    {
        this.viewModel.Tick();
    }

    private void WindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        this.UpdateResponsiveLayout();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool editableTextFocused = this.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false };
        WindowShortcutAction action = WindowShortcutRouter.Resolve(e.Key, e.KeyModifiers, editableTextFocused);

        e.Handled = action switch
        {
            WindowShortcutAction.PauseResume => ExecuteCommand(this.viewModel.PauseResumeCommand),
            WindowShortcutAction.Stop => ExecuteCommand(this.viewModel.ResetCommand),
            WindowShortcutAction.Restart => ExecuteCommand(this.viewModel.RestartCommand),
            WindowShortcutAction.Escape => this.viewModel.TryHandleEscape() || this.fullScreenController.TryExit(),
            WindowShortcutAction.ToggleFullScreen => this.ToggleFullScreenAndReportHandled(),
            _ => false
        };
    }

    private static bool ExecuteCommand(RelayCommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private bool ToggleFullScreenAndReportHandled()
    {
        this.ToggleFullScreen();
        return true;
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.State)
            or nameof(MainWindowViewModel.HasCompletionEmphasis)
            or nameof(MainWindowViewModel.HasValidationError)
            or nameof(MainWindowViewModel.IsTimerModificationLocked))
        {
            this.UpdatePresentationClasses();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.DesktopProgressRequest))
        {
            this.ApplyDesktopProgress();
        }
    }

    private void ApplyDesktopProgress()
    {
        if (this.isClosePreparing || this.isClosed)
        {
            return;
        }

        _ = this.desktopProgressController.ApplyAsync(this.viewModel.DesktopProgressRequest);
    }

    private void WindowAttentionRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!this.isClosed)
            {
                this.windowAttentionController?.RequestAttention();
            }
        });
    }

    private void ExpiryVisualFeedbackRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!this.isClosed)
            {
                this.RestartExpiryFlash();
            }
        });
    }

    private void ValidationFeedbackRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.isClosed)
            {
                return;
            }

            this.TimerInputTextBox.Focus();
            this.TimerInputTextBox.SelectAll();
            this.RestartValidationFeedback();
        });
    }

    private void CloseRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.isClosed)
            {
                return;
            }

            this.completionCloseTimer.Stop();
            this.completionCloseTimer.Start();
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
            if (this.isClosed || generation != this.expiryFlashGeneration)
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
            if (this.isClosed || generation != this.validationFeedbackGeneration)
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

    private void CompletionCloseTimerTick(object? sender, EventArgs e)
    {
        this.completionCloseTimer.Stop();
        this.Close();
    }

    private static bool ShouldAnimateVisualFeedback()
    {
        // Avalonia 12 does not expose a reliable cross-desktop reduced-motion preference.
        // Keep feedback short and single-pass until a dependable platform signal is available.
        return true;
    }
}
