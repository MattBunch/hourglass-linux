using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using System.Text.Json;

namespace Hourglass.Linux.Avalonia;

public sealed partial class MainWindow : Window, IWindowAttentionTarget, IFullScreenWindowTarget, IWindowGeometryTarget
{
    private static readonly string SoundAssetsDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Sounds");

    private static readonly JsonSerializerOptions ThemeJsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true
    };

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
    private readonly WindowGeometryController windowGeometryController;
    private readonly Func<Task> requestApplicationExit;
    private readonly Func<MainWindow, Task> prepareCoordinatorClose;
    private readonly bool loadSettingsOnOpened;
    private readonly MainWindowViewModel viewModel;
    private readonly WindowAttentionController? windowAttentionController;
    private int expiryFlashGeneration;
    private bool closeApprovalPreapproved;
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
            CreateDefaultViewModel(services),
            services.DesktopProgressService,
            services.StatusIconService)
    {
    }

    private static MainWindowViewModel CreateDefaultViewModel(DefaultMainWindowServices services)
    {
        var settingsStore = new JsonFileSettingsStore(new XdgSettingsPathService());
        return new MainWindowViewModel(
            new CountdownEngine(new SystemMonotonicClock()),
            () => DateTime.Now,
            new NotifySendNotificationService(),
            new SystemdSessionInhibitor(),
            settingsStore,
            new DirectAppSettingsStore(settingsStore),
            new DirectSavedTimersStore(settingsStore),
            new LinuxAudioAlertService(SoundAssetsDirectory),
            new UnsupportedSystemPowerService(),
            services.StatusIconService.IsSupported,
            services.StatusIconService.CanRecoverHiddenWindow,
            uiDispatcher: AvaloniaUiDispatcher.Instance);
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
        Func<MainWindow, Task>? prepareCoordinatorClose = null,
        Func<Task>? requestApplicationExit = null)
    {
        InitializeComponent();

        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.desktopProgressController = new DesktopProgressController(
            desktopProgressService ?? throw new ArgumentNullException(nameof(desktopProgressService)));
        this.statusIconService = statusIconService ?? throw new ArgumentNullException(nameof(statusIconService));
        this.loadSettingsOnOpened = loadSettingsOnOpened;
        this.prepareCoordinatorClose = prepareCoordinatorClose ?? (_ => Task.CompletedTask);
        this.requestApplicationExit = requestApplicationExit ?? this.RequestLocalExitAsync;
        this.DataContext = this.viewModel;
        this.windowAttentionController = new WindowAttentionController(this);
        this.fullScreenController = new WindowFullScreenController(this);
        this.windowGeometryController = new WindowGeometryController(
            this,
            this.NotifyWindowGeometryChanged,
            this.GetWorkAreas);
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
        this.PositionChanged += this.WindowPositionChanged;
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
        this.ApplyThemePreference();
        this.RebuildThemeMenu();
        this.RebuildRecentInputsMenu();
        this.RebuildSavedTimersMenu();
        this.ApplyDesktopProgress();
        this.ApplyStatusIconState();
    }

    internal bool RequiresExitConfirmation => this.viewModel.ShouldPromptOnExit;

    internal event EventHandler? WindowGeometryChanged;

    internal WindowGeometrySnapshot? CurrentWindowGeometry => this.windowGeometryController.CurrentGeometry;

    internal void ApplyWindowGeometry(WindowGeometrySnapshot? geometry)
    {
        this.windowGeometryController.Apply(geometry);
        this.UpdateResponsiveLayout();
    }

    internal WindowGeometrySnapshot CreateCascadedWindowGeometry(WindowGeometrySnapshot previousGeometry)
    {
        ArgumentNullException.ThrowIfNull(previousGeometry);

        var currentGeometry = new WindowGeometrySnapshot(
            this.Position.X,
            this.Position.Y,
            this.Width,
            this.Height);
        return WindowGeometryValidator.Cascade(currentGeometry, previousGeometry, this.GetWorkAreas());
    }

    internal void CloseWithPreapprovedExit()
    {
        this.closeApprovalPreapproved = true;
        this.Close();
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
            this.windowGeometryController?.RecordChange();

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
        _ = this.requestApplicationExit();
    }

    private void FullScreenMenuItemClick(object? sender, RoutedEventArgs e)
    {
        this.ToggleFullScreen();
    }

    private Task RequestLocalExitAsync()
    {
        this.Close();
        return Task.CompletedTask;
    }

    private void ToggleFullScreen()
    {
        this.fullScreenController.Toggle();
        this.FullScreenMenuItem.IsChecked = this.fullScreenController.IsFullScreen;
    }

    private async Task<bool> RequestCloseApprovalAsync()
    {
        if (this.closeApprovalPreapproved)
        {
            this.closeApprovalPreapproved = false;
            return true;
        }

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
        await this.PrepareCoordinatorCloseOnUiThreadAsync().ConfigureAwait(false);
    }

    private Task PrepareCoordinatorCloseOnUiThreadAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return this.prepareCoordinatorClose(this);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await this.prepareCoordinatorClose(this).ConfigureAwait(true);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        return completion.Task;
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
        this.PositionChanged -= this.WindowPositionChanged;
        this.SizeChanged -= this.WindowSizeChanged;
        this.RemoveHandler(KeyDownEvent, this.WindowKeyDown);
        _ = this.statusIconService.DisposeAsync();
        this.windowGeometryController.Dispose();
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
        this.windowGeometryController.RecordChange();
    }

    private void WindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        this.windowGeometryController.RecordChange();
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

        if (e.PropertyName is nameof(MainWindowViewModel.ThemePreference)
            or nameof(MainWindowViewModel.CustomThemeId)
            or nameof(MainWindowViewModel.CurrentCustomTheme))
        {
            this.ApplyThemePreference();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CustomThemeMenuItems)
            or nameof(MainWindowViewModel.CanExportCustomTheme)
            or nameof(MainWindowViewModel.CanModifyCustomThemes))
        {
            this.RebuildThemeMenu();
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

    private IReadOnlyList<WindowWorkArea> GetWorkAreas()
    {
        return this.Screens.All
            .Select(screen =>
            {
                PixelRect area = screen.WorkingArea;
                return new WindowWorkArea(area.X, area.Y, area.Width, area.Height, screen.Scaling);
            })
            .ToArray();
    }

    private void NotifyWindowGeometryChanged()
    {
        Dispatcher.UIThread.Post(() => this.WindowGeometryChanged?.Invoke(this, EventArgs.Empty));
    }

    internal static ThemeVariant ToThemeVariant(LinuxThemePreference themePreference)
    {
        return themePreference switch
        {
            LinuxThemePreference.Light => ThemeVariant.Light,
            LinuxThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void ApplyThemePreference()
    {
        CustomThemeDefinition? customTheme = this.viewModel.CurrentCustomTheme;
        this.RequestedThemeVariant = customTheme == null
            ? ToThemeVariant(this.viewModel.ThemePreference)
            : ToThemeVariant(customTheme.BaseThemePreference);

        if (customTheme == null)
        {
            this.SetDefaultThemeResources();
            return;
        }

        this.RootGrid.Classes.Set("custom-theme", true);
        CustomThemeColors colors = customTheme.Colors;
        this.SetBrushResource("TimerWindowBackgroundBrush", colors.Background);
        this.SetBrushResource("TimerPrimaryTextBrush", colors.PrimaryText);
        this.SetBrushResource("TimerSecondaryTextBrush", colors.SecondaryText);
        this.SetBrushResource("TimerCommandTextBrush", colors.CommandText);
        this.SetBrushResource("TimerAccentBrush", colors.Accent);
        this.SetBrushResource("TimerProgressFillBrush", colors.ProgressFill);
        this.SetBrushResource("TimerValidationFlashBrush", colors.ValidationFlash);
        this.SetBrushResource("TimerCompletionBorderBrush", colors.CompletionBorder);
        this.SetBrushResource("TimerLockedBorderBrush", colors.LockedBorder);
    }

    private void SetDefaultThemeResources()
    {
        this.RootGrid.Classes.Set("custom-theme", false);
        this.SetBrushResource("TimerWindowBackgroundBrush", "Transparent");
        this.SetBrushResource("TimerPrimaryTextBrush", CustomThemeColors.DefaultPrimaryText);
        this.SetBrushResource("TimerSecondaryTextBrush", CustomThemeColors.DefaultSecondaryText);
        this.SetBrushResource("TimerCommandTextBrush", CustomThemeColors.DefaultCommandText);
        this.SetBrushResource("TimerAccentBrush", CustomThemeColors.DefaultAccent);
        this.SetBrushResource("TimerProgressFillBrush", CustomThemeColors.DefaultProgressFill);
        this.SetBrushResource("TimerValidationFlashBrush", CustomThemeColors.DefaultValidationFlash);
        this.SetBrushResource("TimerCompletionBorderBrush", CustomThemeColors.DefaultCompletionBorder);
        this.SetBrushResource("TimerLockedBorderBrush", CustomThemeColors.DefaultLockedBorder);
    }

    private void SetBrushResource(string key, string color)
    {
        this.Resources[key] = StringComparer.Ordinal.Equals(color, "Transparent")
            ? Brushes.Transparent
            : new SolidColorBrush(Color.Parse(color));
    }

    private void RebuildThemeMenu()
    {
        this.ThemeMenuItem.Items.Clear();
        this.ThemeMenuItem.Items.Add(new MenuItem
        {
            Header = "System",
            ToggleType = MenuItemToggleType.Radio,
            GroupName = "ThemePreference",
            IsChecked = this.viewModel.IsSystemThemeSelected,
            Command = this.viewModel.SelectThemePreferenceCommand,
            CommandParameter = nameof(LinuxThemePreference.System)
        });
        this.ThemeMenuItem.Items.Add(new MenuItem
        {
            Header = "Light",
            ToggleType = MenuItemToggleType.Radio,
            GroupName = "ThemePreference",
            IsChecked = this.viewModel.IsLightThemeSelected,
            Command = this.viewModel.SelectThemePreferenceCommand,
            CommandParameter = nameof(LinuxThemePreference.Light)
        });
        this.ThemeMenuItem.Items.Add(new MenuItem
        {
            Header = "Dark",
            ToggleType = MenuItemToggleType.Radio,
            GroupName = "ThemePreference",
            IsChecked = this.viewModel.IsDarkThemeSelected,
            Command = this.viewModel.SelectThemePreferenceCommand,
            CommandParameter = nameof(LinuxThemePreference.Dark)
        });

        if (this.viewModel.CustomThemeMenuItems.Length > 0)
        {
            this.ThemeMenuItem.Items.Add(new Separator());
        }

        foreach (CustomThemeMenuItem theme in this.viewModel.CustomThemeMenuItems)
        {
            var themeItem = new MenuItem
            {
                Header = theme.Name
            };
            themeItem.Items.Add(new MenuItem
            {
                Header = "Use this theme",
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "ThemePreference",
                IsChecked = theme.IsSelected,
                Command = this.viewModel.SelectCustomThemeCommand,
                CommandParameter = theme.Id
            });
            themeItem.Items.Add(new MenuItem
            {
                Header = "Edit",
                Command = new RelayCommand(
                    () => _ = this.EditCustomThemeAsync(theme.Id),
                    () => this.viewModel.CanModifyCustomThemes)
            });
            themeItem.Items.Add(new MenuItem
            {
                Header = "Duplicate",
                Command = this.viewModel.DuplicateCustomThemeCommand,
                CommandParameter = theme.Id
            });
            themeItem.Items.Add(new MenuItem
            {
                Header = "Export",
                Command = new RelayCommand(() => _ = this.ExportCustomThemeAsync(theme.Id))
            });
            themeItem.Items.Add(new MenuItem
            {
                Header = "Delete",
                Command = new RelayCommand(
                    () => _ = this.DeleteCustomThemeAsync(theme.Id),
                    () => this.viewModel.CanModifyCustomThemes)
            });
            this.ThemeMenuItem.Items.Add(themeItem);
        }

        this.ThemeMenuItem.Items.Add(new Separator());
        this.ThemeMenuItem.Items.Add(new MenuItem
        {
            Header = "New custom theme",
            Command = new RelayCommand(
                () => _ = this.CreateCustomThemeAsync(),
                () => this.viewModel.CanModifyCustomThemes)
        });
        this.ThemeMenuItem.Items.Add(new MenuItem
        {
            Header = "Import custom theme",
            Command = new RelayCommand(
                () => _ = this.ImportCustomThemeAsync(),
                () => this.viewModel.CanModifyCustomThemes)
        });
    }

    private async Task CreateCustomThemeAsync()
    {
        if (!this.viewModel.CanModifyCustomThemes)
        {
            return;
        }

        var dialog = new CustomThemeEditorWindow();
        CustomThemeDefinition? theme = await dialog.ShowDialog<CustomThemeDefinition?>(this).ConfigureAwait(true);
        if (theme != null && this.viewModel.CanModifyCustomThemes)
        {
            this.viewModel.SaveCustomTheme(theme, select: true);
        }
    }

    private async Task EditCustomThemeAsync(string themeId)
    {
        if (!this.viewModel.CanModifyCustomThemes)
        {
            return;
        }

        CustomThemeDefinition? theme = this.viewModel.FindCustomTheme(themeId);
        if (theme == null)
        {
            return;
        }

        var dialog = new CustomThemeEditorWindow(theme);
        CustomThemeDefinition? editedTheme = await dialog.ShowDialog<CustomThemeDefinition?>(this).ConfigureAwait(true);
        if (editedTheme != null && this.viewModel.CanModifyCustomThemes)
        {
            this.viewModel.SaveCustomTheme(editedTheme, select: StringComparer.Ordinal.Equals(this.viewModel.CustomThemeId, editedTheme.Id));
        }
    }

    private async Task DeleteCustomThemeAsync(string themeId)
    {
        if (!this.viewModel.CanModifyCustomThemes)
        {
            return;
        }

        CustomThemeDefinition? theme = this.viewModel.FindCustomTheme(themeId);
        if (theme == null)
        {
            return;
        }

        var dialog = new CustomThemeDeleteWindow(theme.Name);
        bool delete = await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
        if (delete && this.viewModel.CanModifyCustomThemes)
        {
            this.viewModel.DeleteCustomThemeCommand.Execute(theme.Id);
        }
    }

    private async Task ImportCustomThemeAsync()
    {
        if (!this.viewModel.CanModifyCustomThemes)
        {
            return;
        }

        TopLevel? topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Hourglass theme")
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"]
                }
            ],
            Title = "Import custom theme"
        }).ConfigureAwait(true);
        IStorageFile? file = files.FirstOrDefault();
        if (file == null)
        {
            return;
        }

        CustomThemeDefinition? theme;
        try
        {
            await using Stream stream = await file.OpenReadAsync().ConfigureAwait(true);
            theme = await JsonSerializer.DeserializeAsync<CustomThemeDefinition>(
                stream,
                ThemeJsonOptions).ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }
        catch (JsonException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        if (theme is { IsValid: true } && this.viewModel.CanModifyCustomThemes)
        {
            this.viewModel.SaveCustomTheme(theme, select: true);
        }
    }

    private async Task ExportCustomThemeAsync(string themeId)
    {
        CustomThemeDefinition? theme = this.viewModel.FindCustomTheme(themeId);
        TopLevel? topLevel = GetTopLevel(this);
        if (theme == null || topLevel == null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("Hourglass theme")
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"]
                }
            ],
            SuggestedFileName = $"{SanitizeFileName(theme.Name)}.json",
            Title = "Export custom theme"
        }).ConfigureAwait(true);
        if (file == null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await JsonSerializer.SerializeAsync(stream, theme, ThemeJsonOptions).ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string SanitizeFileName(string name)
    {
        string sanitized = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        return string.IsNullOrWhiteSpace(sanitized) ? "hourglass-theme" : sanitized;
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
