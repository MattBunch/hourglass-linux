using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
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

    private static readonly Uri StatusIconResourceUri = new("avares://hourglass-linux/Assets/hourglass.png");

    private const double RefreshIntervalMilliseconds = 250;
    private const double ExpiryFlashDurationMilliseconds = 420;
    private const double ValidationFeedbackDurationMilliseconds = 650;

    private readonly WindowCloseCoordinator closeCoordinator;
    private readonly DispatcherTimer completionCloseTimer;
    private readonly DesktopProgressController desktopProgressController;
    private readonly DispatcherTimer expiryFlashTimer;
    private readonly DispatcherTimer refreshTimer;
    private readonly IStatusIconService statusIconService;
    private readonly DispatcherTimer validationFeedbackTimer;
    private readonly WindowFullScreenController fullScreenController;
    private readonly Func<MainWindow, Task> prepareCoordinatorClose;
    private readonly bool loadSettingsOnOpened;
    private readonly MainWindowViewModel viewModel;
    private readonly WindowAttentionController? windowAttentionController;
    private int expiryFlashGeneration;
    private bool focusWithinContent;
    private bool isClosed;
    private bool isClosePreparing;
    private bool pointerWithinContent = true;
    private int validationFeedbackGeneration;

    public MainWindow()
        : this(CreateDefaultServices())
    {
    }

    private MainWindow(DefaultMainWindowServices services)
        : this(
            new MainWindowViewModel(
                new CountdownEngine(new SystemMonotonicClock()),
                () => DateTime.Now,
                new NotifySendNotificationService(),
                new SystemdSessionInhibitor(),
                new JsonFileSettingsStore(new XdgSettingsPathService()),
                new LinuxAudioAlertService(NormalBeepPath),
                new UnsupportedSystemPowerService(),
                services.StatusIconService.IsSupported,
                services.StatusIconService.CanRecoverHiddenWindow),
            services.DesktopProgressService,
            services.StatusIconService)
    {
    }

    internal MainWindow(MainWindowViewModel viewModel)
        : this(viewModel, new UnsupportedDesktopProgressService(), UnsupportedStatusIconService.Instance)
    {
    }

    internal MainWindow(MainWindowViewModel viewModel, IDesktopProgressService desktopProgressService)
        : this(viewModel, desktopProgressService, UnsupportedStatusIconService.Instance)
    {
    }

    internal MainWindow(
        MainWindowViewModel viewModel,
        IDesktopProgressService desktopProgressService,
        IStatusIconService statusIconService,
        bool loadSettingsOnOpened = true,
        Func<MainWindow, Task>? prepareCoordinatorClose = null)
    {
        InitializeComponent();

        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)));
        this.statusIconService = statusIconService ?? throw new ArgumentNullException(nameof(statusIconService));
        this.loadSettingsOnOpened = loadSettingsOnOpened;
        this.prepareCoordinatorClose = prepareCoordinatorClose ?? (_ => Task.CompletedTask);
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
        this.viewModel.HideToNotificationAreaRequested += this.HideToNotificationAreaRequested;
        this.statusIconService.ActionRequested += this.StatusIconActionRequested;
        this.UpdatePresentationClasses();
        this.RebuildRecentInputsMenu();
        this.RebuildSavedTimersMenu();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
    }

    private static DefaultMainWindowServices CreateDefaultServices()
    {
        var environmentReader = new ProcessDesktopEnvironmentReader();
        var sessionBusProbe = new EnvironmentSessionBusProbe(environmentReader);
        IStatusIconService statusIconService = new LinuxStatusIconCapability(
            environmentReader,
            sessionBusProbe).IsSupported()
            ? CreateAvaloniaStatusIconService()
            : UnsupportedStatusIconService.Instance;

        return new DefaultMainWindowServices(
            LinuxDesktopProgressServiceFactory.CreateDefault(),
            statusIconService);
    }

    private static IStatusIconService CreateAvaloniaStatusIconService()
    {
        try
        {
            using Stream iconStream = AssetLoader.Open(StatusIconResourceUri);
            return new AvaloniaStatusIconService(new WindowIcon(iconStream));
        }
        catch (Exception)
        {
            return UnsupportedStatusIconService.Instance;
        }
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
        await this.prepareCoordinatorClose(this).ConfigureAwait(false);
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
        if (this.loadSettingsOnOpened)
        {
            await this.viewModel.LoadSettingsAsync();
        }
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
        this.viewModel.HideToNotificationAreaRequested -= this.HideToNotificationAreaRequested;
        this.statusIconService.ActionRequested -= this.StatusIconActionRequested;
        this.refreshTimer.Tick -= this.RefreshTimerTick;
        this.expiryFlashTimer.Tick -= this.ExpiryFlashTimerTick;
        this.completionCloseTimer.Tick -= this.CompletionCloseTimerTick;
        this.validationFeedbackTimer.Tick -= this.ValidationFeedbackTimerTick;
        this.Closing -= this.WindowClosing;
        this.Closed -= this.WindowClosed;
        this.Opened -= this.WindowOpened;
        this.SizeChanged -= this.WindowSizeChanged;
        this.RemoveHandler(KeyDownEvent, this.WindowKeyDown);
        _ = this.statusIconService.DisposeAsync();
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

        if (e.PropertyName is nameof(MainWindowViewModel.StatusIconMenuState))
        {
            this.ApplyStatusIconState();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.RecentInputMenuItems))
        {
            this.RebuildRecentInputsMenu();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.SavedTimerMenuItems)
            or nameof(MainWindowViewModel.CanSaveCurrentTimer))
        {
            this.RebuildSavedTimersMenu();
        }
    }

    private void RebuildRecentInputsMenu()
    {
        this.RecentInputsMenuItem.Items.Clear();

        foreach (RecentInputMenuItem recentInput in this.viewModel.RecentInputMenuItems)
        {
            this.RecentInputsMenuItem.Items.Add(new MenuItem
            {
                Header = recentInput.TimerInput,
                Command = this.viewModel.SelectRecentInputCommand,
                CommandParameter = recentInput.TimerInput
            });
        }

        if (this.viewModel.RecentInputMenuItems.Length > 0)
        {
            this.RecentInputsMenuItem.Items.Add(new Separator());
        }

        this.RecentInputsMenuItem.Items.Add(new MenuItem
        {
            Header = "Clear recent inputs",
            Command = this.viewModel.ClearRecentInputsCommand
        });
    }

    private void RebuildSavedTimersMenu()
    {
        this.SavedTimersMenuItem.Items.Clear();
        this.SavedTimersMenuItem.Items.Add(new MenuItem
        {
            Header = "Save current timer",
            Command = this.viewModel.SaveCurrentTimerCommand
        });
        this.SavedTimersMenuItem.Items.Add(new MenuItem
        {
            Header = "Open all saved timers",
            Command = this.viewModel.OpenAllSavedTimersCommand
        });

        if (this.viewModel.SavedTimerMenuItems.Length > 0)
        {
            this.SavedTimersMenuItem.Items.Add(new Separator());
        }

        foreach (SavedTimerMenuItem savedTimer in this.viewModel.SavedTimerMenuItems)
        {
            var savedTimerMenuItem = new MenuItem
            {
                Header = savedTimer.Header
            };
            savedTimerMenuItem.Items.Add(new MenuItem
            {
                Header = "Open",
                Command = this.viewModel.OpenSavedTimerCommand,
                CommandParameter = savedTimer.Id
            });
            savedTimerMenuItem.Items.Add(new MenuItem
            {
                Header = "Remove",
                Command = this.viewModel.RemoveSavedTimerCommand,
                CommandParameter = savedTimer.Id
            });
            this.SavedTimersMenuItem.Items.Add(savedTimerMenuItem);
        }

        if (this.viewModel.SavedTimerMenuItems.Length > 0)
        {
            this.SavedTimersMenuItem.Items.Add(new Separator());
        }

        this.SavedTimersMenuItem.Items.Add(new MenuItem
        {
            Header = "Clear saved timers",
            Command = this.viewModel.ClearSavedTimersCommand
        });
    }

    private void ApplyDesktopProgress()
    {
        if (this.isClosePreparing || this.isClosed)
        {
            return;
        }

        _ = this.desktopProgressController.ApplyAsync(this.viewModel.DesktopProgressRequest);
    }

    private void ApplyStatusIconState()
    {
        if (this.isClosed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(this.ApplyStatusIconState);
            return;
        }

        _ = this.ApplyStatusIconStateAsync();
    }

    private async Task ApplyStatusIconStateAsync()
    {
        try
        {
            await this.statusIconService.UpdateAsync(this.viewModel.StatusIconMenuState);
        }
        catch (Exception)
        {
        }
    }

    private void HideToNotificationAreaRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(this.HideToNotificationArea);
    }

    private void HideToNotificationArea()
    {
        if (this.isClosed || !this.viewModel.CanHideToNotificationArea)
        {
            return;
        }

        this.Hide();
    }

    private void StatusIconActionRequested(object? sender, StatusIconActionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.HandleStatusIconAction(e.Action));
    }

    private void HandleStatusIconAction(StatusIconAction action)
    {
        if (this.isClosed)
        {
            return;
        }

        switch (action)
        {
            case StatusIconAction.NewTimer:
                ExecuteCommand(this.viewModel.NewTimerCommand);
                break;
            case StatusIconAction.ShowWindow:
                this.windowAttentionController?.RequestAttention();
                break;
            case StatusIconAction.HideWindow:
                this.HideToNotificationArea();
                break;
            case StatusIconAction.PauseResume:
                ExecuteCommand(this.viewModel.PauseResumeCommand);
                break;
            case StatusIconAction.Stop:
                ExecuteCommand(this.viewModel.ResetCommand);
                break;
            case StatusIconAction.Restart:
                ExecuteCommand(this.viewModel.RestartCommand);
                break;
            case StatusIconAction.Exit:
                this.windowAttentionController?.RequestAttention();
                this.Close();
                break;
        }
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

    private sealed record DefaultMainWindowServices(
        IDesktopProgressService DesktopProgressService,
        IStatusIconService StatusIconService);
}
