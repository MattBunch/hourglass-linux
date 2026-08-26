namespace Hourglass.Linux.Avalonia.Tests;

using System.Globalization;
using System.Xml.Linq;
using Hourglass.Linux.Avalonia;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class MainWindowViewModelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTimerTitleUsesApplicationWindowTitle(string? timerTitle)
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerTitle = timerTitle;

        Assert.Equal(timerTitle ?? string.Empty, viewModel.TimerTitle);
        Assert.Equal("Hourglass", viewModel.WindowTitle);
    }

    [Fact]
    public void DefaultTimerInputUsesParserResourceInsteadOfUiPlaceholder()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement timerInput = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "TimerInputTextBox");

        Assert.Equal(TimerStart.Default.ToString(), TimerViewState.DefaultTimerInput);
        Assert.Equal(TimerStart.Default.ToString(), viewModel.TimerInput);
        Assert.Equal("{x:Static local:ApplicationStrings.TimerInputDefault}", timerInput.Attribute("PlaceholderText")?.Value);
    }

    [Fact]
    public void ChangingTimerTitleImmediatelyUpdatesWindowTitleAndRaisesNotifications()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TimerTitle = "Tea";

        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal("Tea", viewModel.WindowTitle);
        Assert.Equal(
            [
                nameof(viewModel.TimerTitle),
                nameof(viewModel.WindowTitle),
                nameof(viewModel.StatusIconMenuState)
            ],
            changedProperties);
    }

    [Fact]
    public void LaunchTimerRequestAppliesTitleAndStartsTimer()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.ApplyLaunchTimerRequest("5 minutes", "Tea");

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("5 minutes", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal("00:05:00", viewModel.RemainingTime);
    }

    [Fact]
    public void WhitespaceTimerTitlePreservesInputWithoutRepublishingFallbackWindowTitle()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TimerTitle = "   ";

        Assert.Equal("   ", viewModel.TimerTitle);
        Assert.Equal("Hourglass", viewModel.WindowTitle);
        Assert.Equal([nameof(viewModel.TimerTitle)], changedProperties);
    }

    [Fact]
    public void TimerTickDoesNotRepublishUnchangedWindowTitle()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        var changedProperties = new List<string?>();
        viewModel.TimerTitle = "Tea";
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.Tick();

        Assert.DoesNotContain(nameof(viewModel.WindowTitle), changedProperties);
    }

    [Fact]
    public async Task LockedTimerTickDoesNotRepublishCustomThemeModificationState()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(
            clock,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default with { LockInterface = true }
            });
        var changedProperties = new List<string?>();

        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "2 minutes";
        viewModel.StartCommand.Execute(null);
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.True(viewModel.IsTimerModificationLocked);
        Assert.False(viewModel.CanModifyCustomThemes);
        Assert.DoesNotContain(nameof(viewModel.CanModifyCustomThemes), changedProperties);
    }

    [Fact]
    public async Task TimeLeftWindowTitleModeUpdatesOnTimerTicks()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(
            clock,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default with { WindowTitleMode = WindowTitleMode.TimeLeft }
            });
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "2 minutes";
        viewModel.StartCommand.Execute(null);
        changedProperties.Clear();

        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        Assert.Equal("00:01:30", viewModel.WindowTitle);
        Assert.Contains(nameof(viewModel.WindowTitle), changedProperties);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeft)]
    [InlineData(WindowTitleMode.TimeElapsed)]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed)]
    public async Task StoppedTimeBasedWindowTitleModesUseTimerTitleInsteadOfZero(WindowTitleMode mode)
    {
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default with { WindowTitleMode = mode }
            });

        await viewModel.LoadSettingsAsync();
        viewModel.TimerTitle = "Tea";

        Assert.Equal("Tea", viewModel.WindowTitle);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeft)]
    [InlineData(WindowTitleMode.TimeElapsed)]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed)]
    public async Task StoppedTimeBasedWindowTitleModesUseApplicationTitleWhenTimerTitleIsBlank(WindowTitleMode mode)
    {
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default with { WindowTitleMode = mode }
            });

        await viewModel.LoadSettingsAsync();

        Assert.Equal("Hourglass", viewModel.WindowTitle);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle, "00:01:30")]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle, "00:00:30")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft, "00:01:30")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed, "00:00:30")]
    public async Task RunningCombinedWindowTitleModesOmitBlankTimerTitle(WindowTitleMode mode, string expected)
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(
            clock,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default with { WindowTitleMode = mode }
            });

        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "2 minutes";
        viewModel.StartCommand.Execute(null);

        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        Assert.Equal(expected, viewModel.WindowTitle);
    }

    [Fact]
    public async Task SelectThemePreferenceChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.SelectThemePreferenceCommand.Execute(nameof(LinuxThemePreference.Dark));
        await viewModel.PendingSettingsSave;

        Assert.Equal(LinuxThemePreference.Dark, viewModel.ThemePreference);
        Assert.True(viewModel.IsDarkThemeSelected);
        Assert.False(viewModel.IsSystemThemeSelected);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(LinuxThemePreference.Dark, settingsStore.SavedSettings.ThemePreference);
        Assert.Contains(nameof(viewModel.ThemePreference), changedProperties);
        Assert.Contains(nameof(viewModel.IsDarkThemeSelected), changedProperties);
    }

    [Fact]
    public async Task CustomThemesLoadSelectAndSaveSeparatelyFromSettings()
    {
        var theme = new CustomThemeDefinition("theme-1", "Evening", LinuxThemePreference.Dark);
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [theme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        await viewModel.LoadSettingsAsync();

        CustomThemeMenuItem menuItem = Assert.Single(viewModel.CustomThemeMenuItems);
        Assert.Equal("Evening", menuItem.Name);

        viewModel.SelectCustomThemeCommand.Execute(theme.Id);
        await viewModel.PendingSettingsSave;

        Assert.Equal(LinuxThemePreference.Custom, viewModel.ThemePreference);
        Assert.Equal(theme.Id, viewModel.CustomThemeId);
        Assert.Equal(theme, viewModel.CurrentCustomTheme);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(LinuxThemePreference.Custom, settingsStore.SavedSettings.ThemePreference);
        Assert.Equal(theme.Id, settingsStore.SavedSettings.CustomThemeId);
    }

    [Fact]
    public async Task SavingCustomThemePersistsThemeDocumentAndSelectsTheme()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        var theme = new CustomThemeDefinition("theme-1", "Evening");

        viewModel.SaveCustomTheme(theme, select: true);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedCustomThemes);
        Assert.Equal(theme, Assert.Single(settingsStore.SavedCustomThemes.Themes));
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(LinuxThemePreference.Custom, settingsStore.SavedSettings.ThemePreference);
        Assert.Equal(theme.Id, settingsStore.SavedSettings.CustomThemeId);
    }

    [Fact]
    public async Task SavingCustomThemePreservesThemesAddedByAnotherWindow()
    {
        var previousTheme = new CustomThemeDefinition("theme-1", "Evening");
        var localTheme = new CustomThemeDefinition("theme-2", "Morning");
        var externalTheme = new CustomThemeDefinition("theme-3", "Noon");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [previousTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LatestCustomThemes = new CustomThemesDocument(themes: [previousTheme, externalTheme]);
        viewModel.SaveCustomTheme(localTheme, select: false);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedCustomThemes);
        Assert.Equal(
            ["theme-1", "theme-2", "theme-3"],
            settingsStore.SavedCustomThemes.Themes.Select(theme => theme.Id).Order());
    }

    [Fact]
    public async Task SavingCustomThemePreservesExistingThemeEditedByAnotherWindow()
    {
        var previousTheme = new CustomThemeDefinition("theme-1", "Evening");
        var externalEdit = new CustomThemeDefinition("theme-1", "Evening updated elsewhere");
        var localTheme = new CustomThemeDefinition("theme-2", "Morning");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [previousTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LatestCustomThemes = new CustomThemesDocument(themes: [externalEdit]);
        viewModel.SaveCustomTheme(localTheme, select: false);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedCustomThemes);
        Assert.Equal(
            ["Evening updated elsewhere", "Morning"],
            settingsStore.SavedCustomThemes.Themes.Select(theme => theme.Name).Order());
    }

    [Fact]
    public async Task SavingCustomThemeDeletePreservesThemesAddedByAnotherWindow()
    {
        var deletedTheme = new CustomThemeDefinition("theme-1", "Evening");
        var externalTheme = new CustomThemeDefinition("theme-2", "Noon");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [deletedTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LatestCustomThemes = new CustomThemesDocument(themes: [deletedTheme, externalTheme]);
        viewModel.DeleteCustomThemeCommand.Execute(deletedTheme.Id);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedCustomThemes);
        CustomThemeDefinition savedTheme = Assert.Single(settingsStore.SavedCustomThemes.Themes);
        Assert.Equal(externalTheme, savedTheme);
    }

    [Fact]
    public async Task SavingCustomThemeEditWinsForSameThemeAndPreservesOtherLatestThemes()
    {
        var originalTheme = new CustomThemeDefinition("theme-1", "Evening");
        var editedTheme = new CustomThemeDefinition("theme-1", "Evening updated");
        var externalTheme = new CustomThemeDefinition("theme-2", "Noon");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [originalTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LatestCustomThemes = new CustomThemesDocument(themes: [originalTheme, externalTheme]);
        viewModel.SaveCustomTheme(editedTheme, select: false);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedCustomThemes);
        Assert.Equal(
            ["Evening updated", "Noon"],
            settingsStore.SavedCustomThemes.Themes.Select(theme => theme.Name).Order());
    }

    [Fact]
    public async Task LockInterfaceBlocksCustomThemeMutation()
    {
        var existingTheme = new CustomThemeDefinition("theme-1", "Evening");
        var addedTheme = new CustomThemeDefinition("theme-2", "Morning");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedCustomThemes = new CustomThemesDocument(themes: [existingTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.ToggleLockInterfaceCommand.Execute(null);
        viewModel.TimerInput = "2 minutes";
        viewModel.StartCommand.Execute(null);

        Assert.True(viewModel.IsTimerModificationLocked);
        Assert.False(viewModel.CanModifyCustomThemes);
        Assert.False(viewModel.DuplicateCustomThemeCommand.CanExecute(existingTheme.Id));
        Assert.False(viewModel.DeleteCustomThemeCommand.CanExecute(existingTheme.Id));

        viewModel.SaveCustomTheme(addedTheme, select: true);
        viewModel.DeleteCustomThemeCommand.Execute(existingTheme.Id);
        await viewModel.PendingSettingsSave;

        Assert.Null(settingsStore.SavedCustomThemes);
        Assert.Null(viewModel.FindCustomTheme(addedTheme.Id));
        Assert.Equal(existingTheme, viewModel.FindCustomTheme(existingTheme.Id));
    }

    [Fact]
    public async Task MissingCustomThemeSelectionFallsBackToSystem()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default with
            {
                ThemePreference = LinuxThemePreference.Custom,
                CustomThemeId = "missing"
            }
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal(LinuxThemePreference.System, viewModel.ThemePreference);
        Assert.Null(viewModel.CustomThemeId);
        Assert.Null(viewModel.CurrentCustomTheme);
    }

    [Fact]
    public async Task SelectWindowTitleModeChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        viewModel.TimerTitle = "Tea";
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.SelectWindowTitleModeCommand.Execute(nameof(WindowTitleMode.TimerTitlePlusTimeLeft));
        await viewModel.PendingSettingsSave;

        Assert.Equal(WindowTitleMode.TimerTitlePlusTimeLeft, viewModel.WindowTitleMode);
        Assert.True(viewModel.IsTimerTitlePlusTimeLeftModeSelected);
        Assert.Equal("Tea", viewModel.WindowTitle);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(WindowTitleMode.TimerTitlePlusTimeLeft, settingsStore.SavedSettings.WindowTitleMode);
        Assert.Contains(nameof(viewModel.WindowTitleMode), changedProperties);
        Assert.DoesNotContain(nameof(viewModel.WindowTitle), changedProperties);
    }

    [Fact]
    public void ChangingTimerTitleWhileRunningDoesNotAlterCountdown()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();
        string remainingTime = viewModel.RemainingTime;

        viewModel.TimerTitle = "Coffee";

        Assert.Equal("Coffee", viewModel.WindowTitle);
        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal(remainingTime, viewModel.RemainingTime);
    }

    [Fact]
    public void ChangingTimerTitleWhilePausedDoesNotAlterCountdown()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();
        viewModel.PauseResumeCommand.Execute(null);
        string remainingTime = viewModel.RemainingTime;

        viewModel.TimerTitle = "Paused tea";

        Assert.Equal("Paused tea", viewModel.WindowTitle);
        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal(remainingTime, viewModel.RemainingTime);
    }

    [Fact]
    public void ExpiredAndResetTimerRetainTimerTitle()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.TimerTitle = "Eggs";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.True(viewModel.HasCompletionEmphasis);
        Assert.Equal("Eggs", viewModel.TimerTitle);
        Assert.Equal("Eggs", viewModel.WindowTitle);

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.False(viewModel.HasCompletionEmphasis);
        Assert.Equal("Eggs", viewModel.TimerTitle);
        Assert.Equal("Eggs", viewModel.WindowTitle);
    }

    [Fact]
    public void EnteringInputModeWhileExpiredResetsCompletedPresentationWithoutStopCommand()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "5 minutes";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromMinutes(6));
        viewModel.Tick();

        bool transitioned = viewModel.TryEnterInputModeFromExpired();

        Assert.True(transitioned);
        Assert.Equal("5 minutes", viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.False(viewModel.HasCompletionEmphasis);
        Assert.Equal(0, viewModel.ProgressPercent);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.False(viewModel.IsCompletionTextVisible);
        Assert.True(viewModel.IsStartVisible);
        Assert.False(viewModel.IsPauseVisible);
        Assert.False(viewModel.IsResumeVisible);
        Assert.False(viewModel.IsStopVisible);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void EnteringInputModePreservesExpressionUntilNativeTextEditingReplacesSelection()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.True(viewModel.TryEnterInputModeFromExpired());
        Assert.Equal("1 second", viewModel.TimerInput);

        viewModel.TimerInput = "2";
        viewModel.TimerInput += " min";

        Assert.Equal("2 min", viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("m")]
    [InlineData("1pm")]
    public void NativeTimerEditingPreservesFirstTextAfterExpiredModeSwitch(string text)
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.True(viewModel.TryEnterInputModeFromExpired());
        viewModel.TimerInput = text;

        Assert.Equal(text, viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);
    }

    [Fact]
    public void EnteringInputModeWhileRunningKeepsCountdownActiveAndShowsEditCommands()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        Assert.True(viewModel.TryEnterTimerInputMode());
        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("2 min", viewModel.TimerInput);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.False(viewModel.IsRemainingTimeVisible);
        Assert.True(viewModel.IsStartVisible);
        Assert.True(viewModel.IsCancelVisible);
        Assert.False(viewModel.IsPauseVisible);
        Assert.False(viewModel.IsStopVisible);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.CancelEditCommand.CanExecute(null));

        clock.Advance(TimeSpan.FromSeconds(15));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:01:15", viewModel.RemainingTime);
        Assert.True(viewModel.IsTimerInputVisible);
    }

    [Fact]
    public void ExpiryWhileEditingForcesCompletedStatusAndPublishesOneShotFeedback()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        int attentionCount = 0;
        int flashCount = 0;
        viewModel.WindowAttentionRequested += (_, _) => attentionCount++;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => flashCount++;
        viewModel.TimerInput = "1 second";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);

        Assert.True(viewModel.TryEnterTimerInputMode());
        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.False(viewModel.IsRemainingTimeVisible);

        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.True(viewModel.IsCompletionTextVisible);
        Assert.False(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.HasCompletionEmphasis);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("1 second", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal(1, flashCount);
        Assert.Equal(1, attentionCount);

        viewModel.Tick();

        Assert.Equal(1, flashCount);
        Assert.Equal(1, attentionCount);
    }

    [Fact]
    public async Task ExpiryWhileEditingCompletesWithoutAttentionWhenPopupIsDisabled()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(popUpWhenExpired: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            sessionInhibitor: sessionInhibitor,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);
        int attentionCount = 0;
        int flashCount = 0;
        viewModel.WindowAttentionRequested += (_, _) => attentionCount++;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => flashCount++;
        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "1 second";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());

        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.True(viewModel.IsCompletionTextVisible);
        Assert.False(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.HasCompletionEmphasis);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("1 second", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal(1, flashCount);
        Assert.Equal(0, attentionCount);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);

        viewModel.Tick();

        Assert.Equal(1, flashCount);
        Assert.Equal(0, attentionCount);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void EnteringInputModeWhilePausedKeepsCountdownPaused()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        viewModel.PauseResumeCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());
        clock.Advance(TimeSpan.FromSeconds(15));
        viewModel.Tick();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal("2 min", viewModel.TimerInput);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.True(viewModel.IsCancelVisible);
    }

    [Fact]
    public void CancellingActiveTimerEditRestoresOriginalExpressionAndLiveDisplay()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());
        viewModel.TimerInput = "45 seconds";

        viewModel.CancelEditCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("2 min", viewModel.TimerInput);
        Assert.False(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.IsRemainingTimeVisible);
        Assert.False(viewModel.IsCancelVisible);
        Assert.False(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void StartingEditedInputReplacesActiveTimer()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();
        Assert.True(viewModel.TryEnterTimerInputMode());
        viewModel.TimerInput = "5 minutes";

        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("5 minutes", viewModel.TimerInput);
        Assert.Equal("00:05:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.IsRemainingTimeVisible);
        Assert.False(viewModel.IsCancelVisible);
    }

    [Fact]
    public void InvalidEditedInputKeepsOriginalTimerRunningAndEditorOpen()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());
        viewModel.TimerInput = "not a timer";

        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(10));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("not a timer", viewModel.TimerInput);
        Assert.Equal("00:01:50", viewModel.RemainingTime);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.IsCancelVisible);
    }

    [Fact]
    public void EditingTitleWhileExpiredEntersInputModeAndPreservesFirstEdit()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.TimerTitle = "Eggs";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        viewModel.TimerTitle = "Eggs!";

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Eggs!", viewModel.TimerTitle);
        Assert.Equal("1 second", viewModel.TimerInput);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.False(viewModel.IsCompletionTextVisible);
    }

    [Fact]
    public void TimerTitleAndTimerInputRemainIndependent()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        string initialTimerInput = viewModel.TimerInput;

        viewModel.TimerTitle = "Laundry";

        Assert.Equal(initialTimerInput, viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);

        viewModel.TimerInput = "10 minutes";

        Assert.Equal("Laundry", viewModel.TimerTitle);
        Assert.Equal("Laundry", viewModel.WindowTitle);
        Assert.Equal(TimerState.Stopped, viewModel.State);
    }

    [Fact]
    public void TimerTitleControlUsesImmediateBindingAndDrivesWindowTitle()
    {
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        XElement titleInput = Assert.Single(
            document.Descendants(controls + "ResponsiveTextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerTitleTextBox");
        XElement timerInput = Assert.Single(
            document.Descendants(controls + "ResponsiveTextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerInputTextBox");
        XElement timerDisplay = Assert.IsType<XElement>(timerInput.Parent);

        Assert.Equal("{Binding WindowTitle}", window.Attribute("Title")?.Value);
        Assert.Equal(
            "{Binding TimerTitle, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            titleInput.Attribute("Text")?.Value);
        Assert.Equal("Click to enter title", ResolveResourceReference(titleInput.Attribute("PlaceholderText")?.Value));
        Assert.Equal("titleInput", titleInput.Attribute("Classes")?.Value);
        Assert.Equal("TimerTitleTextBoxGotFocus", titleInput.Attribute("GotFocus")?.Value);
        Assert.Null(titleInput.Attribute("IsVisible"));
        Assert.Same(titleInput.Parent, timerDisplay.Parent);
        Assert.Contains(timerDisplay, titleInput.ElementsAfterSelf());
    }

    [Fact]
    public void CompletedDisplayUsesWindowsStyleFocusableTimerField()
    {
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        XElement completionInput = Assert.Single(
            document.Descendants(controls + "ResponsiveTextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "CompletionTextBox");

        Assert.Null(window.Attribute("TextInput"));
        Assert.Equal("{Binding StatusText, Mode=OneWay}", completionInput.Attribute("Text")?.Value);
        Assert.Equal("True", completionInput.Attribute("IsReadOnly")?.Value);
        Assert.Equal("{Binding IsCompletionTextVisible}", completionInput.Attribute("IsVisible")?.Value);
        Assert.Equal("Arrow", completionInput.Attribute("Cursor")?.Value);
        Assert.Equal("CompletionTextBoxGotFocus", completionInput.Attribute("GotFocus")?.Value);
        Assert.Equal("CompletionTextBoxPointerPressed", completionInput.Attribute("PointerPressed")?.Value);
    }

    [Fact]
    public void RunningDisplayUsesWindowsStyleFocusableTimerField()
    {
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement remainingTime = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "RemainingTimeTextBox");

        Assert.Equal("timerInput", remainingTime.Attribute("Classes")?.Value);
        Assert.Equal("{Binding RemainingTime, Mode=OneWay}", remainingTime.Attribute("Text")?.Value);
        Assert.Equal("True", remainingTime.Attribute("IsReadOnly")?.Value);
        Assert.Equal("Arrow", remainingTime.Attribute("Cursor")?.Value);
        Assert.Equal("RemainingTimeTextBoxGotFocus", remainingTime.Attribute("GotFocus")?.Value);
        Assert.Equal("RemainingTimeTextBoxPointerPressed", remainingTime.Attribute("PointerPressed")?.Value);
    }

    [Fact]
    public void ResponsiveTextBoxesRetainFramelessNativeTextBoxStyles()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        string[] selectors = document
            .Descendants(avalonia + "Style")
            .Select(style => style.Attribute("Selector")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Contains("TextBox.timerInput", selectors);
        Assert.Contains("TextBox.titleInput", selectors);
        Assert.Contains("TextBox.timerInput:focus", selectors);
        Assert.Contains("TextBox.titleInput:focus", selectors);
        Assert.DoesNotContain("local|ResponsiveTextBox.timerInput", selectors);
        Assert.DoesNotContain("local|ResponsiveTextBox.titleInput", selectors);
    }

    [Fact]
    public void MainWindowStylesKeepBuiltInThemeForegroundsAndScopeCustomThemeBrushes()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement styles = Assert.IsType<XElement>(document.Root?.Element(avalonia + "Window.Styles"));
        Dictionary<string, XElement> stylesBySelector = styles
            .Elements(avalonia + "Style")
            .Where(element => element.Attribute("Selector") != null)
            .ToDictionary(element => element.Attribute("Selector")!.Value, StringComparer.Ordinal);

        AssertStyleSetter(stylesBySelector["TextBox.timerInput"], "Foreground", "{DynamicResource TextControlForeground}");
        AssertStyleSetter(stylesBySelector["TextBox.timerInput"], "CaretBrush", "{DynamicResource TextControlForeground}");
        AssertStyleSetter(stylesBySelector["TextBox.titleInput"], "Foreground", "{DynamicResource TextControlForeground}");
        AssertStyleSetter(stylesBySelector["TextBox.titleInput"], "CaretBrush", "{DynamicResource TextControlForeground}");
        AssertStyleSetter(stylesBySelector["Button.textCommand"], "Foreground", "{DynamicResource TextControlForeground}");
        AssertStyleSetter(stylesBySelector["Button.textCommand:pointerover"], "Foreground", "{DynamicResource AccentFillColorDefaultBrush}");
        AssertStyleSetter(stylesBySelector["Grid.custom-theme Grid.progressTrack"], "Background", "{DynamicResource TimerWindowBackgroundBrush}");
        AssertStyleSetter(stylesBySelector["Grid.custom-theme TextBox.timerInput"], "Foreground", "{DynamicResource TimerPrimaryTextBrush}");
        AssertStyleSetter(stylesBySelector["Grid.custom-theme TextBox.titleInput"], "Foreground", "{DynamicResource TimerSecondaryTextBrush}");
        AssertStyleSetter(stylesBySelector["Grid.custom-theme Button.textCommand"], "Foreground", "{DynamicResource TimerCommandTextBrush}");
        AssertStyleSetter(stylesBySelector["Grid.custom-theme Button.textCommand:pointerover"], "Foreground", "{DynamicResource TimerAccentBrush}");
    }

    [Fact]
    public void CustomThemeDialogCommandsHonorInterfaceLock()
    {
        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));

        Assert.Contains("nameof(MainWindowViewModel.CanModifyCustomThemes)", codeBehind);
        Assert.Contains("() => this.viewModel.CanModifyCustomThemes", codeBehind);
        Assert.Contains("Header = ApplicationStrings.ThemeCommandUse", codeBehind);
        Assert.Contains("ToggleType = MenuItemToggleType.Radio", codeBehind);
        Assert.Contains("if (!this.viewModel.CanModifyCustomThemes)", codeBehind);
        Assert.Contains("theme != null && this.viewModel.CanModifyCustomThemes", codeBehind);
        Assert.Contains("editedTheme != null && this.viewModel.CanModifyCustomThemes", codeBehind);
        Assert.Contains("delete && this.viewModel.CanModifyCustomThemes", codeBehind);
        Assert.Contains("theme is { IsValid: true } && this.viewModel.CanModifyCustomThemes", codeBehind);
    }

    [Fact]
    public void CustomThemeImportAndExportHandleStorageFailures()
    {
        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        int importStart = codeBehind.IndexOf("private async Task ImportCustomThemeAsync", StringComparison.Ordinal);
        int exportStart = codeBehind.IndexOf("private async Task ExportCustomThemeAsync", StringComparison.Ordinal);
        int nextMethodStart = codeBehind.IndexOf("private static string SanitizeFileName", exportStart, StringComparison.Ordinal);
        string importMethod = codeBehind[importStart..exportStart];
        string exportMethod = codeBehind[exportStart..nextMethodStart];

        Assert.Contains("try", importMethod);
        Assert.Contains("file.OpenReadAsync()", importMethod);
        Assert.Contains("catch (UnauthorizedAccessException)", importMethod);
        Assert.Contains("catch (IOException)", importMethod);
        Assert.Contains("catch (JsonException)", importMethod);
        Assert.Contains("catch (NotSupportedException)", importMethod);
        Assert.Contains("try", exportMethod);
        Assert.Contains("file.OpenWriteAsync()", exportMethod);
        Assert.Contains("JsonSerializer.SerializeAsync", exportMethod);
        Assert.Contains("catch (UnauthorizedAccessException)", exportMethod);
        Assert.Contains("catch (IOException)", exportMethod);
        Assert.Contains("catch (JsonException)", exportMethod);
        Assert.Contains("catch (NotSupportedException)", exportMethod);
    }

    [Fact]
    public void PrimaryAndTitleTextUseResponsiveControlsWithSafeLimits()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));

        XElement title = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "TimerTitleTextBox");
        XElement timerInput = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "TimerInputTextBox");
        XElement remainingTime = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "RemainingTimeTextBox");
        XElement completion = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "CompletionTextBox");

        Assert.Equal("8", title.Attribute("MinFontSize")?.Value);
        Assert.Equal("12", title.Attribute("MaxFontSize")?.Value);
        foreach (XElement primaryText in new[] { timerInput, remainingTime, completion })
        {
            Assert.Equal("8", primaryText.Attribute("MinFontSize")?.Value);
            Assert.Equal("18", primaryText.Attribute("MaxFontSize")?.Value);
        }

        Assert.DoesNotContain(remainingTime.Ancestors(), element => element.Name == avalonia + "Viewbox");
    }

    [Fact]
    public void StartWithValidInputRunsTimer()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:02:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsInputEnabled);
        Assert.True(viewModel.IsRunning);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.True(viewModel.ResetCommand.CanExecute(null));
        Assert.Equal(1, sessionInhibitor.AcquireCount);
        Assert.Equal(0, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void StartWithAbsoluteTimeInputRunsTimer()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 6, 8, 12, 30, 0);
        var viewModel = CreateViewModel(clock, () => now);

        viewModel.TimerInput = "1pm";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:30:00", viewModel.RemainingTime);
        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.False(viewModel.IsRestartVisible);
    }

    [Fact]
    public void StartWithInvalidInputKeepsTimerStopped()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerInput = "not a timer";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Enter a valid current timer.", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
    }

    [Fact]
    public void InvalidInputSetsDurableErrorAndReplaysFeedbackWithoutSideEffects()
    {
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            notificationService: notificationService,
            sessionInhibitor: sessionInhibitor,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);
        int feedbackCount = 0;
        viewModel.ValidationFeedbackRequested += (_, _) => feedbackCount++;

        viewModel.TimerInput = "not a timer";
        viewModel.StartCommand.Execute(null);
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("not a timer", viewModel.TimerInput);
        Assert.Equal("Enter a valid current timer.", viewModel.StatusText);
        Assert.True(viewModel.HasValidationError);
        Assert.Equal("Enter a valid current timer.", viewModel.TimerInputHelpText);
        Assert.Equal(2, feedbackCount);
        Assert.Equal(0, notificationService.CallCount);
        Assert.Equal(0, audioAlertService.CallCount);
        Assert.Equal(0, sessionInhibitor.AcquireCount);
        Assert.Null(settingsStore.SavedSettings);
    }

    [Fact]
    public void EditingOrValidStartingClearsValidationError()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerInput = "invalid";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.HasValidationError);
        Assert.Equal("Enter a valid current timer.", viewModel.TimerInputHelpText);

        viewModel.TimerInput = "still invalid";
        Assert.False(viewModel.HasValidationError);
        Assert.Equal("Enter a duration or time, then press Enter to start.", viewModel.TimerInputHelpText);

        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.HasValidationError);
        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.False(viewModel.HasValidationError);
    }

    [Fact]
    public void StartCommandDoesNotRestartRunningTimer()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "2 min";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        viewModel.TimerInput = "5 minutes";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.Equal(1, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void RestartCommandRestartsDurationAndReacquiresSessionInhibition()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);
        viewModel.TimerInput = "2 minutes";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        viewModel.Tick();

        viewModel.RestartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:02:00", viewModel.RemainingTime);
        Assert.Equal("2 minutes", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.True(viewModel.IsRemainingTimeVisible);
        Assert.False(viewModel.HasCompletionEmphasis);
        Assert.Equal(2, sessionInhibitor.AcquireCount);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void RestartAfterExpiryCreatesOneNewExpiryCycle()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);
        int attentionCount = 0;
        int flashCount = 0;
        viewModel.WindowAttentionRequested += (_, _) => attentionCount++;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => flashCount++;
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        viewModel.RestartCommand.Execute(null);
        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.False(viewModel.HasCompletionEmphasis);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(2, attentionCount);
        Assert.Equal(2, flashCount);
        Assert.Equal(2, notificationService.CallCount);
        Assert.Equal(2, audioAlertService.CallCount);
    }

    [Fact]
    public void TimerExpressionEditingTemporarilyDisablesRestartAndEscapeRestoresIt()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());
        viewModel.TimerInput = "2 minutes";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.TryEnterTimerInputMode());

        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.TryHandleEscape());

        Assert.False(viewModel.IsTimerInputVisible);
        Assert.True(viewModel.RestartCommand.CanExecute(null));
    }

    [Fact]
    public void EscapeDismissesExpiredPresentationAfterEditCancellationPriority()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.True(viewModel.TryHandleEscape());

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.False(viewModel.HasCompletionEmphasis);
        Assert.False(viewModel.TryHandleEscape());
    }

    [Fact]
    public void RelayCommandDoesNotExecuteWhenCanExecuteIsFalse()
    {
        int callCount = 0;
        var command = new RelayCommand(() => callCount++, () => false);

        command.Execute(null);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void TimerInputEnterKeyBindingsUseStartCommand()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement timerInput = Assert.Single(
            document.Descendants(controls + "ResponsiveTextBox"),
            element => element.Attribute(xaml + "Name")?.Value == "TimerInputTextBox");
        string[] gestures = timerInput
            .Element(controls + "ResponsiveTextBox.KeyBindings")?
            .Elements(avalonia + "KeyBinding")
            .Where(element => element.Attribute("Command")?.Value == "{Binding StartCommand}")
            .Select(element => element.Attribute("Gesture")?.Value)
            .OfType<string>()
            .Order()
            .ToArray() ?? [];

        Assert.Equal(["Enter", "Return"], gestures);

        Assert.DoesNotContain(
            timerInput.Element(controls + "ResponsiveTextBox.KeyBindings")?.Elements(avalonia + "KeyBinding") ?? [],
            element => element.Attribute("Gesture")?.Value == "Escape");
    }

    [Fact]
    public void MainWindowPrimaryTimerSurfaceHasAccessibleNamesAndKeyboardPaths()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace controls = "clr-namespace:Hourglass.Linux.Avalonia";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));

        AssertAutomationName(
            FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "TimerTitleTextBox"),
            "Timer title");
        XElement timerInput = FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "TimerInputTextBox");
        AssertAutomationHelpText(timerInput, "{Binding TimerInputHelpText, Mode=OneWay}");
        Assert.Null(timerInput.Attribute("AutomationProperties.LiveSetting"));

        XElement validationStatus = FindNamedElement(document, avalonia + "TextBlock", xaml, "ValidationStatusText");
        Assert.Equal("{Binding StatusText, Mode=OneWay}", validationStatus.Attribute("Text")?.Value);
        Assert.Equal("{Binding HasValidationError}", validationStatus.Attribute("IsVisible")?.Value);
        AssertAutomationNameBindsToText(validationStatus);
        AssertAutomationLiveSetting(validationStatus, "Polite");
        AssertAutomationName(
            FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "RemainingTimeTextBox"),
            "Remaining time");
        AssertAutomationName(
            FindNamedElement(document, controls + "ResponsiveTextBox", xaml, "CompletionTextBox"),
            "Timer status");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "StartButton"), "Start timer");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "PauseButton"), "Pause timer");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "ResumeButton"), "Resume timer");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "StopButton"), "Stop timer");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "RestartButton"), "Restart timer");
        AssertAutomationName(FindNamedElement(document, avalonia + "Button", xaml, "CancelButton"), "Cancel timer edit");

        string shortcuts = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/WindowShortcutRouter.cs"));
        Assert.Contains("Key.Space when !isEditableTextFocused => WindowShortcutAction.PauseResume", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Key.P => WindowShortcutAction.PauseResume", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Key.S => WindowShortcutAction.Stop", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Key.R => WindowShortcutAction.Restart", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Key.Escape => WindowShortcutAction.Escape", shortcuts, StringComparison.Ordinal);
        Assert.Contains("WindowShortcutAction.ToggleFullScreen", shortcuts, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenuUsesExistingTimerCommandsAndPersistentOptionBindings()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement contextMenu = Assert.Single(
            rootGrid.Element(avalonia + "Grid.ContextMenu")?.Elements(avalonia + "ContextMenu") ?? []);
        Dictionary<string, XElement> menuItems = contextMenu
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Header")!.Value), StringComparer.Ordinal);

        Assert.Equal("{Binding AlwaysOnTop}", window.Attribute("Topmost")?.Value);
        Assert.Equal("Transparent", rootGrid.Attribute("Background")?.Value);
        Assert.Equal("{Binding StartCommand}", menuItems["Start"].Attribute("Command")?.Value);
        Assert.Equal("{Binding PauseResumeCommand}", menuItems["{Binding PauseResumeText}"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ResetCommand}", menuItems["Stop"].Attribute("Command")?.Value);
        Assert.Equal("{Binding RestartCommand}", menuItems["Restart"].Attribute("Command")?.Value);
        Assert.Equal("RecentInputsMenuItem", menuItems["Recent inputs"].Attribute(xaml + "Name")?.Value);
        Assert.Equal("SavedTimersMenuItem", menuItems["Saved timers"].Attribute(xaml + "Name")?.Value);
        Assert.Equal("CheckBox", menuItems["Notifications"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding NotificationsEnabled, Mode=OneWay}", menuItems["Notifications"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleNotificationsCommand}", menuItems["Notifications"].Attribute("Command")?.Value);
        Dictionary<string, XElement> soundItems = menuItems["Sound"]
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Header")!.Value), StringComparer.Ordinal);
        Assert.Equal("Radio", soundItems["None"].Attribute("ToggleType")?.Value);
        Assert.Equal("AudioAlertSound", soundItems["None"].Attribute("GroupName")?.Value);
        Assert.Equal("{Binding IsNoSoundSelected, Mode=OneWay}", soundItems["None"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding SelectAudioAlertSoundCommand}", soundItems["None"].Attribute("Command")?.Value);
        Assert.Equal("none", soundItems["None"].Attribute("CommandParameter")?.Value);
        Assert.Equal("Radio", soundItems["Loud beep"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding IsLoudBeepSoundSelected, Mode=OneWay}", soundItems["Loud beep"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding SelectAudioAlertSoundCommand}", soundItems["Loud beep"].Attribute("Command")?.Value);
        Assert.Equal("resource:Loud beep", soundItems["Loud beep"].Attribute("CommandParameter")?.Value);
        Assert.Equal("Radio", soundItems["Normal beep"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding IsNormalBeepSoundSelected, Mode=OneWay}", soundItems["Normal beep"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding SelectAudioAlertSoundCommand}", soundItems["Normal beep"].Attribute("Command")?.Value);
        Assert.Equal("resource:Normal beep", soundItems["Normal beep"].Attribute("CommandParameter")?.Value);
        Assert.Equal("Radio", soundItems["Quiet beep"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding IsQuietBeepSoundSelected, Mode=OneWay}", soundItems["Quiet beep"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding SelectAudioAlertSoundCommand}", soundItems["Quiet beep"].Attribute("Command")?.Value);
        Assert.Equal("resource:Quiet beep", soundItems["Quiet beep"].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding PreviewAudioAlertSoundCommand}", soundItems["Preview selected sound"].Attribute("Command")?.Value);
        Assert.Equal("{Binding StopAudioAlertPreviewCommand}", soundItems["Stop preview"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Pop up when expired"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding PopUpWhenExpired, Mode=OneWay}", menuItems["Pop up when expired"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding TogglePopUpWhenExpiredCommand}", menuItems["Pop up when expired"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Always on top"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding AlwaysOnTop, Mode=OneWay}", menuItems["Always on top"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleAlwaysOnTopCommand}", menuItems["Always on top"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Show progress in taskbar"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding ShowProgressInTaskbar, Mode=OneWay}", menuItems["Show progress in taskbar"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleShowProgressInTaskbarCommand}", menuItems["Show progress in taskbar"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Show in notification area"].Attribute("ToggleType")?.Value);
        Assert.Equal(
            "{Binding ShowInNotificationArea, Mode=OneWay}",
            menuItems["Show in notification area"].Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding IsStatusIconSupported}",
            menuItems["Show in notification area"].Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding ToggleShowInNotificationAreaCommand}",
            menuItems["Show in notification area"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Prompt on exit"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding PromptOnExit, Mode=OneWay}", menuItems["Prompt on exit"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding TogglePromptOnExitCommand}", menuItems["Prompt on exit"].Attribute("Command")?.Value);
        Assert.Equal("ThemeMenuItem", menuItems["Theme"].Attribute(xaml + "Name")?.Value);
        Assert.Empty(menuItems["Theme"].Elements(avalonia + "MenuItem"));
        Dictionary<string, XElement> titleItems = menuItems["Window title"]
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Header")!.Value), StringComparer.Ordinal);
        Assert.Equal("Radio", titleItems["Application name"].Attribute("ToggleType")?.Value);
        Assert.Equal("WindowTitleMode", titleItems["Application name"].Attribute("GroupName")?.Value);
        Assert.Equal(
            "{Binding IsApplicationNameTitleModeSelected, Mode=OneWay}",
            titleItems["Application name"].Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding SelectWindowTitleModeCommand}",
            titleItems["Application name"].Attribute("Command")?.Value);
        Assert.Equal("ApplicationName", titleItems["Application name"].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding IsTimeLeftTitleModeSelected, Mode=OneWay}", titleItems["Time left"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimeLeft", titleItems["Time left"].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding IsTimeElapsedTitleModeSelected, Mode=OneWay}", titleItems["Time elapsed"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimeElapsed", titleItems["Time elapsed"].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding IsTimerTitleModeSelected, Mode=OneWay}", titleItems["Timer title"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimerTitle", titleItems["Timer title"].Attribute("CommandParameter")?.Value);
        Assert.Equal(
            "{Binding IsTimeLeftPlusTimerTitleModeSelected, Mode=OneWay}",
            titleItems["Time left plus timer title"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimeLeftPlusTimerTitle", titleItems["Time left plus timer title"].Attribute("CommandParameter")?.Value);
        Assert.Equal(
            "{Binding IsTimeElapsedPlusTimerTitleModeSelected, Mode=OneWay}",
            titleItems["Time elapsed plus timer title"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimeElapsedPlusTimerTitle", titleItems["Time elapsed plus timer title"].Attribute("CommandParameter")?.Value);
        Assert.Equal(
            "{Binding IsTimerTitlePlusTimeLeftModeSelected, Mode=OneWay}",
            titleItems["Timer title plus time left"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimerTitlePlusTimeLeft", titleItems["Timer title plus time left"].Attribute("CommandParameter")?.Value);
        Assert.Equal(
            "{Binding IsTimerTitlePlusTimeElapsedModeSelected, Mode=OneWay}",
            titleItems["Timer title plus time elapsed"].Attribute("IsChecked")?.Value);
        Assert.Equal("TimerTitlePlusTimeElapsed", titleItems["Timer title plus time elapsed"].Attribute("CommandParameter")?.Value);
        Dictionary<string, XElement> advancedItems = menuItems["Advanced options"]
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Header")!.Value), StringComparer.Ordinal);
        Assert.Equal("CheckBox", advancedItems["Reverse progress bar"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding ReverseProgressBar, Mode=OneWay}", advancedItems["Reverse progress bar"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleReverseProgressBarCommand}", advancedItems["Reverse progress bar"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Show time elapsed"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding ShowTimeElapsed, Mode=OneWay}", advancedItems["Show time elapsed"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleShowTimeElapsedCommand}", advancedItems["Show time elapsed"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Loop timer"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding LoopTimer, Mode=OneWay}", advancedItems["Loop timer"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleLoopTimerCommand}", advancedItems["Loop timer"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Loop sound"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding LoopSound, Mode=OneWay}", advancedItems["Loop sound"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleLoopSoundCommand}", advancedItems["Loop sound"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Close when expired"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding CloseWhenExpired, Mode=OneWay}", advancedItems["Close when expired"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleCloseWhenExpiredCommand}", advancedItems["Close when expired"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Lock interface"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding LockInterface, Mode=OneWay}", advancedItems["Lock interface"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleLockInterfaceCommand}", advancedItems["Lock interface"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Do not keep computer awake"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding DoNotKeepComputerAwake, Mode=OneWay}", advancedItems["Do not keep computer awake"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleDoNotKeepComputerAwakeCommand}", advancedItems["Do not keep computer awake"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Shut down when expired"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding ShutDownWhenExpired, Mode=OneWay}", advancedItems["Shut down when expired"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding IsShutdownSupported}", advancedItems["Shut down when expired"].Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding ToggleShutDownWhenExpiredCommand}", advancedItems["Shut down when expired"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Restore active session on startup"].Attribute("ToggleType")?.Value);
        Assert.Equal(
            "{Binding RestoreActiveSessionOnStartup, Mode=OneWay}",
            advancedItems["Restore active session on startup"].Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding ToggleRestoreActiveSessionOnStartupCommand}",
            advancedItems["Restore active session on startup"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", advancedItems["Open saved timers on startup"].Attribute("ToggleType")?.Value);
        Assert.Equal(
            "{Binding OpenSavedTimersOnStartup, Mode=OneWay}",
            advancedItems["Open saved timers on startup"].Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding ToggleOpenSavedTimersOnStartupCommand}",
            advancedItems["Open saved timers on startup"].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding CanHideToNotificationArea}",
            menuItems["Hide to notification area"].Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding HideToNotificationAreaCommand}",
            menuItems["Hide to notification area"].Attribute("Command")?.Value);
        Assert.Null(menuItems["About Hourglass"].Attribute("Command"));
        Assert.Equal("AboutMenuItemClick", menuItems["About Hourglass"].Attribute("Click")?.Value);
        Assert.Equal("CheckBox", menuItems["Full screen"].Attribute("ToggleType")?.Value);
        Assert.Equal("FullScreenMenuItemClick", menuItems["Full screen"].Attribute("Click")?.Value);
        Assert.Equal("ExitMenuItemClick", menuItems["Exit"].Attribute("Click")?.Value);
    }

    [Fact]
    public void ContextMenuToggleItemsExposeLabelsAndCheckedState()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement contextMenu = Assert.Single(
            rootGrid.Element(avalonia + "Grid.ContextMenu")?.Elements(avalonia + "ContextMenu") ?? []);
        XElement[] toggleItems = contextMenu
            .Descendants(avalonia + "MenuItem")
            .Where(element => element.Attribute("ToggleType") is not null)
            .ToArray();

        foreach (XElement item in toggleItems)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Attribute("Header")?.Value));
        }

        foreach (XElement item in toggleItems.Where(element => element.Attribute("ToggleType")?.Value == "Radio"))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Attribute("GroupName")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(item.Attribute("IsChecked")?.Value));
        }

        foreach (XElement item in toggleItems.Where(element => element.Attribute("ToggleType")?.Value == "CheckBox"
            && element.Attribute(xaml + "Name")?.Value != "FullScreenMenuItem"))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Attribute("IsChecked")?.Value));
        }

        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        Assert.Contains("this.FullScreenMenuItem.IsChecked = this.fullScreenController?.IsFullScreen == true;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.FullScreenMenuItem.IsChecked = this.fullScreenController.IsFullScreen;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenuPlacesAboutBeforeExit()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement contextMenu = Assert.Single(
            rootGrid.Element(avalonia + "Grid.ContextMenu")?.Elements(avalonia + "ContextMenu") ?? []);
        XElement[] elements = contextMenu.Elements().ToArray();
        string[] headers = elements
            .Where(element => element.Name == avalonia + "MenuItem")
            .Select(element => element.Attribute("Header")?.Value)
            .OfType<string>()
            .Select(ResolveResourceReference)
            .ToArray();

        Assert.True(
            Array.IndexOf(headers, "About Hourglass") < Array.IndexOf(headers, "Exit"),
            "About Hourglass should appear before Exit.");

        int hideIndex = Array.FindIndex(
            elements,
            element => element.Name == avalonia + "MenuItem"
                && ResolveResourceReference(element.Attribute("Header")?.Value) == "Hide to notification area");
        Assert.True(hideIndex >= 0);
        Assert.Equal(avalonia + "Separator", elements[hideIndex + 1].Name);
        Assert.Equal("About Hourglass", ResolveResourceReference(elements[hideIndex + 2].Attribute("Header")?.Value));
        Assert.Equal("Exit", ResolveResourceReference(elements[hideIndex + 3].Attribute("Header")?.Value));
    }

    [Fact]
    public void AboutDialogContainsApplicationMetadataAndActions()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/AboutWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        Dictionary<string, XElement> buttons = document
            .Descendants(avalonia + "Button")
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Content")?.Value), StringComparer.Ordinal);
        string[] namedTextBlocks = document
            .Descendants(avalonia + "TextBlock")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal("About Hourglass", ResolveResourceReference(window.Attribute("Title")?.Value));
        Assert.Equal("/Assets/hourglass.png", window.Attribute("Icon")?.Value);
        Assert.Equal("False", window.Attribute("CanResize")?.Value);
        Assert.Equal("False", window.Attribute("ShowInTaskbar")?.Value);
        Assert.Equal("CenterOwner", window.Attribute("WindowStartupLocation")?.Value);
        Assert.Contains("ProductNameText", namedTextBlocks);
        Assert.Contains("VersionText", namedTextBlocks);
        Assert.Contains("BuildText", namedTextBlocks);
        Assert.Contains("CommitText", namedTextBlocks);
        Assert.Contains("RuntimeText", namedTextBlocks);
        Assert.Contains("PlatformText", namedTextBlocks);
        Assert.Contains("DeveloperText", namedTextBlocks);
        Assert.Contains("LicenseText", namedTextBlocks);
        Assert.Contains("GitHub Repository", buttons.Keys);
        Assert.Contains("Developer Website", buttons.Keys);
        Assert.Contains("Original Hourglass Project", buttons.Keys);
        Assert.Contains("Copy build information", buttons.Keys);
        AssertAutomationName(buttons["GitHub Repository"], "Open GitHub repository");
        AssertAutomationName(buttons["Developer Website"], "Open developer website");
        AssertAutomationName(buttons["Original Hourglass Project"], "Open original Hourglass project");
        AssertAutomationName(buttons["Copy build information"], "Copy build information");
        AssertAutomationName(buttons["Close"], "Close About Hourglass");
        XElement linkStatusText = Assert.Single(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute(xaml + "Name")?.Value == "LinkStatusText");
        AssertAutomationNameBindsToText(linkStatusText);
        AssertAutomationLiveSetting(linkStatusText, "Polite");

        XElement copyStatusText = Assert.Single(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute(xaml + "Name")?.Value == "CopyStatusText");
        AssertAutomationNameBindsToText(copyStatusText);
        AssertAutomationLiveSetting(copyStatusText, "Polite");
        Assert.Equal("True", buttons["Close"].Attribute("IsCancel")?.Value);
    }

    [Fact]
    public void DialogInteractiveControlsHaveAccessibleNames()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        XDocument themeEditor = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/CustomThemeEditorWindow.axaml"));
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "NameBox"), "Theme name");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "ComboBox", xaml, "BaseThemeBox"), "Base theme");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "BackgroundBox"), "Background color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "PrimaryTextBox"), "Primary text color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "SecondaryTextBox"), "Secondary text color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "CommandTextBox"), "Command text color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "AccentBox"), "Accent color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "ProgressFillBox"), "Progress fill color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "ValidationFlashBox"), "Validation flash color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "CompletionBorderBox"), "Completion border color");
        AssertAutomationName(FindNamedElement(themeEditor, avalonia + "TextBox", xaml, "LockedBorderBox"), "Locked border color");
        XElement validationText = FindNamedElement(themeEditor, avalonia + "TextBlock", xaml, "ValidationText");
        AssertAutomationNameBindsToText(validationText);
        AssertAutomationLiveSetting(validationText, "Polite");
        AssertAutomationName(FindButtonByContent(themeEditor, "Cancel"), "Cancel custom theme edit");
        AssertAutomationName(FindButtonByContent(themeEditor, "Save"), "Save custom theme");

        XDocument themeDelete = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/CustomThemeDeleteWindow.axaml"));
        AssertAutomationNameBindsToText(FindNamedElement(themeDelete, avalonia + "TextBlock", xaml, "MessageText"));
        AssertAutomationName(FindButtonByContent(themeDelete, "Cancel"), "Cancel delete custom theme");
        AssertAutomationName(FindButtonByContent(themeDelete, "Delete"), "Delete custom theme");

        XDocument exitConfirmation = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/ExitConfirmationWindow.axaml"));
        AssertAutomationNameBindsToText(Assert.Single(exitConfirmation.Descendants(avalonia + "TextBlock")));
        AssertAutomationName(FindButtonByContent(exitConfirmation, "Cancel"), "Cancel exit");
        AssertAutomationName(FindButtonByContent(exitConfirmation, "Exit"), "Exit Hourglass");
    }

    [Fact]
    public void RuntimeXamlUserFacingTextUsesApplicationStringResources()
    {
        string[] xamlFiles =
        [
            "src/Hourglass.Linux.Avalonia/MainWindow.axaml",
            "src/Hourglass.Linux.Avalonia/AboutWindow.axaml",
            "src/Hourglass.Linux.Avalonia/ExitConfirmationWindow.axaml",
            "src/Hourglass.Linux.Avalonia/CustomThemeEditorWindow.axaml",
            "src/Hourglass.Linux.Avalonia/CustomThemeDeleteWindow.axaml"
        ];
        string[] userFacingAttributes =
        [
            "Title",
            "Header",
            "Content",
            "Text",
            "PlaceholderText",
            "AutomationProperties.Name",
            "AutomationProperties.HelpText"
        ];

        foreach (string xamlFile in xamlFiles)
        {
            XDocument document = XDocument.Load(FindRepositoryFile(xamlFile));
            XElement root = Assert.IsType<XElement>(document.Root);
            foreach (XElement element in root.DescendantsAndSelf())
            {
                foreach (string attributeName in userFacingAttributes)
                {
                    XAttribute? attribute = element.Attribute(attributeName);
                    if (attribute == null || IsAllowedNonResourceXamlValue(attribute.Value))
                    {
                        continue;
                    }

                    Assert.StartsWith("{x:Static local:ApplicationStrings.", attribute.Value, StringComparison.Ordinal);
                    Assert.EndsWith("}", attribute.Value, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void AvaloniaRuntimeCodeKeepsUserFacingStringsBehindApplicationStrings()
    {
        string[] sourceFiles = Directory.GetFiles(
                FindRepositoryDirectory("src/Hourglass.Linux.Avalonia"),
                "*.cs",
            SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("ApplicationStrings.cs", StringComparison.Ordinal)
                && !file.EndsWith("AssemblyInfo.cs", StringComparison.Ordinal)
                && !file.Split(Path.DirectorySeparatorChar).Contains("bin", StringComparer.Ordinal)
                && !file.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.Ordinal))
            .ToArray();

        string[] allowListedFragments =
        [
            "\"active-session\"",
            "\"active-sessions\"",
            "\"app\"",
            "\"application/json\"",
            "\"AudioAlertSound\"",
            "\"avalonia-status-icon\"",
            "\"avalonia-window\"",
            "\"Active audio playback disposal failed.\"",
            "\"Active session load failed; using no active session.\"",
            "\"Active sessions save failed.\"",
            "\"Assets\"",
            "\"Application settings load failed; using defaults.\"",
            "\"avares://hourglass-linux/Assets/hourglass.png\"",
            "\"BuildConfiguration\"",
            "\"clear\"",
            "\"create\"",
            "\"custom-theme\"",
            "\"custom-themes\"",
            "\"Default\"",
            "\"DeveloperWebsite\"",
            "\"desktop-progress\"",
            "\"Desktop progress update failed.\"",
            "\"Document load failed; treating it as missing.\"",
            "\"Document load failed; using fallback.\"",
            "\"Document save failed.\"",
            "\"event\"",
            "\"Event handler failed.\"",
            "\"Final session save failed before shutdown.\"",
            "\"handler\"",
            "\"hourglass\"",
            "\"hourglass-linux\"",
            "\"hourglass-theme\"",
            "\"json\"",
            "\"Latest custom themes load failed before save; using fallback.\"",
            "\"M\"",
            "\"load\"",
            "\"N\"",
            "\"Optional document load failed; treating it as missing.\"",
            "\"OriginalProject\"",
            "\"Previous active sessions save failed before a queued save.\"",
            "\"Previous custom themes save failed before a queued save.\"",
            "\"Previous document save failed before a queued save.\"",
            "\"Previous saved timers save failed before a queued save.\"",
            "\"Previous settings save failed before a queued save.\"",
            "\"release\"",
            "\"RepositoryUrl\"",
            "\"resource:Loud beep\"",
            "\"resource:Normal beep\"",
            "\"resource:Quiet beep\"",
            "\"save\"",
            "\"saved-timers\"",
            "\"Saved timers load failed; using an empty document.\"",
            "\"Saved timers save failed.\"",
            "\"schedule\"",
            "\"settings\"",
            "\"Settings save failed.\"",
            "\"Session inhibition acquire failed.\"",
            "\"Session inhibition release failed.\"",
            "\"Shutdown request failed.\"",
            "\"Sounds\"",
            "\"SourceRevision\"",
            "\"status-icon\"",
            "\"ThemePreference\"",
            "\"Status icon backend could not be initialized.\"",
            "\"Status icon update failed.\"",
            "\"Timer expiry audio failed.\"",
            "\"Timer expiry notification failed.\"",
            "\"Transparent\"",
            "\"unsupported\"",
            "\"Wake alarm release failed.\"",
            "\"Wake alarm scheduling failed.\"",
            "\"wake-alarm\"",
            "\"window-attention\"",
            "\"Window attention request failed.\"",
            "\"WindowTitleMode\"",
            "\"*.json\"",
            "\"libxcb.so.1\"",
            "\"libxcb-dri3.so.0\"",
            "\"libc.so.6\"",
            "\"--title\"",
            "\"-t\"",
            "\"https://github.com/MattBunch/hourglass-linux\"",
            "\"https://mattbunch.dev\"",
            "\"http://chris.dziemborowicz.com/apps/hourglass/\""
        ];

        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            foreach (string line in source.Split(Environment.NewLine))
            {
                string trimmed = line.Trim();
                if (!trimmed.Contains('"', StringComparison.Ordinal)
                    || trimmed.Contains("ApplicationStrings.", StringComparison.Ordinal)
                    || trimmed.Contains("RecordBestEffort", StringComparison.Ordinal)
                    || trimmed.Contains("RecordDataRecovery", StringComparison.Ordinal)
                    || trimmed.Contains("RecordFailure", StringComparison.Ordinal)
                    || trimmed.Contains("RecordUserRequested", StringComparison.Ordinal)
                    || trimmed.Contains(".Classes.Set(", StringComparison.Ordinal)
                    || trimmed.Contains("SetBrushResource(", StringComparison.Ordinal)
                    || trimmed.Contains("SuggestedFileName", StringComparison.Ordinal)
                    || trimmed.Contains("FormattableString.Invariant", StringComparison.Ordinal)
                    || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || allowListedFragments.Any(fragment => trimmed.Contains(fragment, StringComparison.Ordinal)))
                {
                    continue;
                }

                Assert.DoesNotMatch("\"[^\"]*[A-Za-z][^\"]*\"", trimmed);
            }
        }
    }

    [Fact]
    public void AboutDialogIsMainWindowShellBehavior()
    {
        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        string aboutHandler = ExtractMethod(codeBehind, "private async void AboutMenuItemClick");

        Assert.Contains("private AboutWindow? aboutWindow;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.applicationInfoProvider.GetApplicationInfo()", aboutHandler, StringComparison.Ordinal);
        Assert.Contains("await dialog.ShowDialog(this).ConfigureAwait(true);", aboutHandler, StringComparison.Ordinal);
        Assert.Contains("this.aboutWindow.Activate();", aboutHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("viewModel.", aboutHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Command.Execute", aboutHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitConfirmationDialogIsNativeAndKeyboardAccessible()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XDocument document = XDocument.Load(
            FindRepositoryFile("src/Hourglass.Linux.Avalonia/ExitConfirmationWindow.axaml"));
        XElement window = Assert.IsType<XElement>(document.Root);
        Dictionary<string, XElement> buttons = document
            .Descendants(avalonia + "Button")
            .ToDictionary(element => ResolveResourceReference(element.Attribute("Content")?.Value), StringComparer.Ordinal);

        Assert.Equal("CenterOwner", window.Attribute("WindowStartupLocation")?.Value);
        Assert.Equal("True", buttons["Cancel"].Attribute("IsCancel")?.Value);
        Assert.Equal("True", buttons["Exit"].Attribute("IsDefault")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            element => ResolveResourceReference(element.Attribute("Text")?.Value)
                == "One or more timers are still running or paused. Exit Hourglass?");
    }

    [Fact]
    public void ContextMenuTimerCommandAvailabilityMatchesEveryTimerState()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        AssertCommandAvailability(viewModel, canStart: true, canPauseResume: false, canStop: false, canRestart: false);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: true, canStop: true, canRestart: true);

        viewModel.PauseResumeCommand.Execute(null);
        Assert.Equal("Resume", viewModel.PauseResumeText);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: true, canStop: true, canRestart: true);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();
        Assert.Equal(TimerState.Expired, viewModel.State);
        AssertCommandAvailability(viewModel, canStart: false, canPauseResume: false, canStop: true, canRestart: true);
    }

    [Fact]
    public void CompletionAndValidationStylesUseExplicitReplayableClasses()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        string[] selectors = document
            .Descendants(avalonia + "Style")
            .Select(style => style.Attribute("Selector")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Contains("Grid.timer-expired Border#CompletionEmphasisBorder", selectors);
        Assert.Contains("Grid.timer-locked Border#CompletionEmphasisBorder", selectors);
        Assert.Contains("Grid.timer-expiry-flash Border#ExpiryFlashLayer", selectors);
        Assert.Contains("TextBox.timerInput.validation-error", selectors);
        Assert.Contains("TextBox.timerInput.validation-feedback", selectors);

        XElement progressLayer = FindNamedElement(document, avalonia + "Grid", xaml, "ProgressLayer");
        XElement innerGrid = FindNamedElement(document, avalonia + "Grid", xaml, "InnerGrid");
        XElement completionBorder = FindNamedElement(document, avalonia + "Border", xaml, "CompletionEmphasisBorder");
        XElement expiryFlash = FindNamedElement(document, avalonia + "Border", xaml, "ExpiryFlashLayer");
        int progressZIndex = int.Parse(progressLayer.Attribute("ZIndex")?.Value ?? string.Empty, CultureInfo.InvariantCulture);
        int innerGridZIndex = int.Parse(innerGrid.Attribute("ZIndex")?.Value ?? string.Empty, CultureInfo.InvariantCulture);
        int completionZIndex = int.Parse(completionBorder.Attribute("ZIndex")?.Value ?? string.Empty, CultureInfo.InvariantCulture);
        int expiryFlashZIndex = int.Parse(expiryFlash.Attribute("ZIndex")?.Value ?? string.Empty, CultureInfo.InvariantCulture);

        Assert.Equal("False", completionBorder.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("False", expiryFlash.Attribute("IsHitTestVisible")?.Value);
        Assert.True(progressZIndex < innerGridZIndex);
        Assert.True(innerGridZIndex < completionZIndex);
        Assert.True(completionZIndex < expiryFlashZIndex);

        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        Assert.Contains("Classes.Set(\"timer-expired\", this.viewModel.HasCompletionEmphasis)", codeBehind);
        Assert.Contains("Classes.Set(\"timer-locked\", this.viewModel.IsTimerModificationLocked)", codeBehind);
        Assert.Contains("Classes.Set(\"validation-error\", this.viewModel.HasValidationError)", codeBehind);
    }

    [Fact]
    public void WindowClosePathsUseSharedCoordinatorAndIdempotentCleanup()
    {
        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        int exitHandlerStart = codeBehind.IndexOf("private void ExitMenuItemClick", StringComparison.Ordinal);
        int nextMethodStart = codeBehind.IndexOf("private void FullScreenMenuItemClick", exitHandlerStart, StringComparison.Ordinal);
        string exitHandler = codeBehind[exitHandlerStart..nextMethodStart];
        int cleanupStart = codeBehind.IndexOf("private void CleanupAfterClose", StringComparison.Ordinal);
        int cleanupEnd = codeBehind.IndexOf("private void RequestFinalClose", cleanupStart, StringComparison.Ordinal);
        string cleanup = codeBehind[cleanupStart..cleanupEnd];
        int prepareCloseStart = codeBehind.IndexOf("private async Task PrepareCloseAsync", StringComparison.Ordinal);
        int prepareCloseEnd = codeBehind.IndexOf("private void UpdatePresentationClasses", prepareCloseStart, StringComparison.Ordinal);
        string prepareClose = codeBehind[prepareCloseStart..prepareCloseEnd];
        int applyDesktopProgressStart = codeBehind.IndexOf("private void ApplyDesktopProgress", StringComparison.Ordinal);
        int applyDesktopProgressEnd = codeBehind.IndexOf("private void WindowAttentionRequested", applyDesktopProgressStart, StringComparison.Ordinal);
        string applyDesktopProgress = codeBehind[applyDesktopProgressStart..applyDesktopProgressEnd];
        int statusIconExitStart = codeBehind.IndexOf("case StatusIconAction.Exit:", StringComparison.Ordinal);
        int statusIconExitEnd = codeBehind.IndexOf("break;", statusIconExitStart, StringComparison.Ordinal);
        string statusIconExit = codeBehind[statusIconExitStart..statusIconExitEnd];

        Assert.Contains("_ = this.requestApplicationExit();", exitHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingSettingsSave", exitHandler, StringComparison.Ordinal);
        Assert.Contains("this.requestApplicationExit = requestApplicationExit ?? this.RequestLocalExitAsync;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private Task RequestLocalExitAsync()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.RequestAttention();", statusIconExit, StringComparison.Ordinal);
        Assert.Contains("this.Close();", statusIconExit, StringComparison.Ordinal);
        Assert.True(
            statusIconExit.IndexOf("this.RequestAttention();", StringComparison.Ordinal)
            < statusIconExit.IndexOf("this.Close();", StringComparison.Ordinal));
        Assert.Contains("this.Closing += this.WindowClosing;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = this.closeCoordinator.RequestClose();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.closeCoordinator.CompleteClose();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.PrepareCloseAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LinuxDesktopProgressServiceFactory.CreateDefault(diagnosticSink)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.PendingSettingsSave", prepareClose, StringComparison.Ordinal);
        Assert.Contains("this.desktopProgressController.ClearAsync()", prepareClose, StringComparison.Ordinal);
        Assert.Contains("await this.PrepareCoordinatorCloseOnUiThreadAsync().ConfigureAwait(false);", prepareClose, StringComparison.Ordinal);
        Assert.Contains("private Task PrepareCoordinatorCloseOnUiThreadAsync()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.CheckAccess()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(async () =>", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.isClosePreparing = true;", prepareClose, StringComparison.Ordinal);
        Assert.Contains("this.refreshTimer.Stop();", prepareClose, StringComparison.Ordinal);
        Assert.True(
            prepareClose.IndexOf("this.refreshTimer.Stop();", StringComparison.Ordinal)
            < prepareClose.IndexOf("this.desktopProgressController.ClearAsync()", StringComparison.Ordinal));
        Assert.Contains("this.refreshTimer.Stop();", cleanup, StringComparison.Ordinal);
        Assert.Contains("this.expiryFlashTimer.Stop();", cleanup, StringComparison.Ordinal);
        Assert.Contains("this.validationFeedbackTimer.Stop();", cleanup, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.WindowAttentionRequested -= this.WindowAttentionRequested;", cleanup, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.ExpiryVisualFeedbackRequested -= this.ExpiryVisualFeedbackRequested;", cleanup, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.ValidationFeedbackRequested -= this.ValidationFeedbackRequested;", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("this.desktopProgressController.ClearAsync();", cleanup, StringComparison.Ordinal);
        Assert.Contains("nameof(MainWindowViewModel.DesktopProgressRequest)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.ApplyDesktopProgress();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (this.isClosePreparing || this.isClosed)", applyDesktopProgress, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.Dispose();", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusIconFactoryLoadsEmbeddedAvaloniaResource()
    {
        string codeBehind = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        string statusIconService = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/AvaloniaStatusIconService.cs"));
        int applyStatusIconStart = codeBehind.IndexOf("private void ApplyStatusIconState()", StringComparison.Ordinal);
        int applyStatusIconEnd = codeBehind.IndexOf("private void HideToNotificationAreaRequested", applyStatusIconStart, StringComparison.Ordinal);
        string applyStatusIcon = codeBehind[applyStatusIconStart..applyStatusIconEnd];
        int hiddenBeforeIcon = statusIconService.IndexOf("IsVisible = false,", StringComparison.Ordinal);
        int iconAssignment = statusIconService.IndexOf("Icon = icon,", StringComparison.Ordinal);

        Assert.Contains("avares://hourglass-linux/Assets/hourglass.png", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AssetLoader.Open(StatusIconResourceUri)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new AvaloniaStatusIconService(new WindowIcon(iconStream))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("new AvaloniaStatusIconService(Path.Combine", codeBehind, StringComparison.Ordinal);
        Assert.InRange(hiddenBeforeIcon, 0, iconAssignment - 1);
        Assert.Contains("Dispatcher.UIThread.CheckAccess()", applyStatusIcon, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(this.ApplyStatusIconState)", applyStatusIcon, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureAwait(false)", applyStatusIcon, StringComparison.Ordinal);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTime()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        DateTime now = new(2026, 6, 8, 10, 0, 0);
        var viewModel = CreateViewModel(clock, () => now, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal("00:00:07", viewModel.RemainingTime);
        Assert.Equal("Resume", viewModel.PauseResumeText);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);

        now = now.AddSeconds(8);
        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:00:05", viewModel.RemainingTime);
        Assert.Equal("Pause", viewModel.PauseResumeText);
        Assert.Equal(2, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void ResetReturnsTimerToReadyState()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.True(viewModel.IsInputEnabled);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.False(viewModel.ResetCommand.CanExecute(null));
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void ProgressAdvancesFreezesResumesExpiresAndResets()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(100, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(2.5));
        viewModel.Tick();

        Assert.Equal(75, viewModel.ProgressPercent);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(4));
        viewModel.Tick();

        Assert.Equal(75, viewModel.ProgressPercent);

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2.5));
        viewModel.Tick();

        Assert.Equal(50, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, viewModel.ProgressPercent);

        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(0, viewModel.ProgressPercent);

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(0, viewModel.ProgressPercent);
    }

    [Fact]
    public void TickTransitionsExpiredTimerToCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsInputEnabled);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void InhibitionFailureDoesNotPreventTimerStart()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor { ThrowOnAcquire = true };
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal(1, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public void DisposeReleasesActiveInhibition()
    {
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), sessionInhibitor: sessionInhibitor);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);
        viewModel.Dispose();

        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Theory]
    [InlineData(null, "Hourglass")]
    [InlineData("", "Hourglass")]
    [InlineData("   ", "Hourglass")]
    [InlineData("Tea", "Tea")]
    public void TickTransitionsExpiredTimerShowsNotificationOnceWithExpectedTitle(
        string? timerTitle,
        string expectedNotificationTitle)
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var viewModel = CreateViewModel(clock, notificationService: notificationService);

        viewModel.TimerTitle = timerTitle;
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(expectedNotificationTitle, notificationService.Title);
        Assert.Equal("Timer complete", notificationService.Body);
    }

    [Fact]
    public void TickTransitionsExpiredTimerPlaysAudioOnce()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(clock, audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.NormalBeep, audioAlertService.SoundId);
    }

    [Fact]
    public async Task TickTransitionsExpiredTimerPlaysSelectedAudioSound()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default with { AudioAlertSoundId = AudioAlertSoundIds.QuietBeep }
        };
        var viewModel = CreateViewModel(
            clock,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);

        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.QuietBeep, audioAlertService.SoundId);
    }

    [Fact]
    public void ExpiryPublishesAttentionAndVisualFeedbackOncePerTimerCycle()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        int attentionCount = 0;
        int flashCount = 0;
        viewModel.WindowAttentionRequested += (_, _) => attentionCount++;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => flashCount++;

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();
        viewModel.Tick();

        Assert.Equal(1, attentionCount);
        Assert.Equal(1, flashCount);
        Assert.True(viewModel.HasCompletionEmphasis);

        viewModel.ResetCommand.Execute(null);
        Assert.False(viewModel.HasCompletionEmphasis);
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(2, attentionCount);
        Assert.Equal(2, flashCount);
    }

    [Fact]
    public async Task DisabledAttentionLeavesOtherExpiryEffectsIntact()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(popUpWhenExpired: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            sessionInhibitor: sessionInhibitor,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);
        int attentionCount = 0;
        viewModel.WindowAttentionRequested += (_, _) => attentionCount++;

        await viewModel.LoadSettingsAsync();
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(0, attentionCount);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
        Assert.True(viewModel.HasCompletionEmphasis);
    }

    [Fact]
    public void AttentionHandlerFailureDoesNotPreventNotificationOrAudio()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);
        viewModel.WindowAttentionRequested += (_, _) => throw new InvalidOperationException("Attention failed.");

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
    }

    [Fact]
    public void NotificationFailureDoesNotPreventCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService { ThrowOnNotify = true };
        var viewModel = CreateViewModel(clock, notificationService: notificationService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
    }

    [Fact]
    public void AudioFailureDoesNotPreventCompleteDisplayOrNotification()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService { ThrowOnPlay = true };
        var notificationService = new RecordingNotificationService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
    }

    [Fact]
    public void NotificationFailureDoesNotPreventAudio()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService { ThrowOnNotify = true };
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.NormalBeep, audioAlertService.SoundId);
    }

    [Fact]
    public async Task LoadSettingsUsesMostRecentTimerInput()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["15 minutes"], notificationsEnabled: true)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal("15 minutes", viewModel.TimerInput);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task LoadSettingsRestoresAllOptions()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(
                notificationsEnabled: false,
                audioAlertsEnabled: false,
                alwaysOnTop: true,
                popUpWhenExpired: false,
                promptOnExit: false,
                reverseProgressBar: true,
                showTimeElapsed: true,
                loopTimer: true,
                loopSound: true,
                closeWhenExpired: true,
                lockInterface: true,
                doNotKeepComputerAwake: true,
                shutDownWhenExpired: false,
                showProgressInTaskbar: false,
                showInNotificationArea: true)
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            statusIconSupported: true);

        await viewModel.LoadSettingsAsync();

        Assert.False(viewModel.NotificationsEnabled);
        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.AlwaysOnTop);
        Assert.False(viewModel.PopUpWhenExpired);
        Assert.False(viewModel.PromptOnExit);
        Assert.True(viewModel.ReverseProgressBar);
        Assert.True(viewModel.ShowTimeElapsed);
        Assert.True(viewModel.LoopTimer);
        Assert.True(viewModel.LoopSound);
        Assert.True(viewModel.CloseWhenExpired);
        Assert.True(viewModel.LockInterface);
        Assert.True(viewModel.DoNotKeepComputerAwake);
        Assert.False(viewModel.ShutDownWhenExpired);
        Assert.False(viewModel.ShowProgressInTaskbar);
        Assert.True(viewModel.ShowInNotificationArea);
    }

    [Fact]
    public async Task ToggleNotificationsChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.NotificationsEnabled);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.NotificationsEnabled);
    }

    [Fact]
    public async Task ToggleAudioAlertsChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleAudioAlertsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.IsNoSoundSelected);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.None, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task SelectAudioAlertSoundChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.SelectAudioAlertSoundCommand.Execute(AudioAlertSoundIds.QuietBeep);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.IsQuietBeepSoundSelected);
        Assert.False(viewModel.IsNormalBeepSoundSelected);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.QuietBeep, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task SelectUnavailableAudioAlertSoundDoesNotChangeOrSaveSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var audioAlertService = new RecordingAudioAlertService();
        audioAlertService.SetSoundAvailable(AudioAlertSoundIds.QuietBeep, available: false);
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);

        viewModel.SelectAudioAlertSoundCommand.Execute(AudioAlertSoundIds.QuietBeep);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.IsNormalBeepSoundSelected);
        Assert.Null(settingsStore.SavedSettings);
    }

    [Fact]
    public async Task SelectNoSoundDisablesAudioAlertsAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.SelectAudioAlertSoundCommand.Execute(AudioAlertSoundIds.None);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.IsNoSoundSelected);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.None, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task LoadDisabledAudioAlertSettingsShowsNoSoundSelected()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(audioAlertsEnabled: false)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.False(viewModel.AudioAlertsEnabled);
        Assert.True(viewModel.IsNoSoundSelected);
        Assert.False(viewModel.IsNormalBeepSoundSelected);
    }

    [Fact]
    public async Task PreviewAudioAlertSoundPlaysSelectedSound()
    {
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default with { AudioAlertSoundId = AudioAlertSoundIds.LoudBeep }
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);

        await viewModel.LoadSettingsAsync();
        viewModel.PreviewAudioAlertSoundCommand.Execute(null);

        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal(AudioAlertSoundIds.LoudBeep, audioAlertService.SoundId);
    }

    [Fact]
    public async Task AudioPreviewCompletionRaisesPropertyChangedThroughUiDispatcher()
    {
        var audioAlertService = new RecordingAudioAlertService();
        var uiDispatcher = new RecordingUiDispatcher();
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            audioAlertService: audioAlertService,
            uiDispatcher: uiDispatcher);
        int previewNotifications = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainWindowViewModel.IsAudioPreviewActive))
            {
                return;
            }

            Assert.True(uiDispatcher.IsDispatching);
            previewNotifications++;
            if (previewNotifications == 2)
            {
                completed.TrySetResult();
            }
        };

        viewModel.PreviewAudioAlertSoundCommand.Execute(null);

        Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(completed.Task, finished);
        Assert.True(uiDispatcher.PostCount >= 2);
    }

    [Fact]
    public void PreviewUnavailableAudioAlertSoundDoesNotPlay()
    {
        var audioAlertService = new RecordingAudioAlertService();
        audioAlertService.SetSoundAvailable(AudioAlertSoundIds.NormalBeep, available: false);
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            audioAlertService: audioAlertService);

        Assert.False(viewModel.CanPreviewAudioAlertSound);

        viewModel.PreviewAudioAlertSoundCommand.Execute(null);

        Assert.Equal(0, audioAlertService.CallCount);
    }

    [Fact]
    public async Task StopPreviewCancelsActiveAudioAlertPreview()
    {
        var audioAlertService = new RecordingAudioAlertService
        {
            HoldPlaybackUntilCanceled = true
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            audioAlertService: audioAlertService);

        viewModel.PreviewAudioAlertSoundCommand.Execute(null);
        await audioAlertService.PlaybackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsAudioPreviewActive);

        viewModel.StopAudioAlertPreviewCommand.Execute(null);
        await audioAlertService.PlaybackCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.IsAudioPreviewActive);
    }

    [Fact]
    public async Task SelectNoSoundStopsActiveAudioAlertPreview()
    {
        var audioAlertService = new RecordingAudioAlertService
        {
            HoldPlaybackUntilCanceled = true
        };
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);

        viewModel.PreviewAudioAlertSoundCommand.Execute(null);
        await audioAlertService.PlaybackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectAudioAlertSoundCommand.Execute(AudioAlertSoundIds.None);
        await audioAlertService.PlaybackCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.IsAudioPreviewActive);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.None, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task ToggleAlwaysOnTopChangesStateAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleAlwaysOnTopCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.AlwaysOnTop);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.AlwaysOnTop);
    }

    [Fact]
    public async Task ToggleShowProgressInTaskbarRaisesPropertyChangedAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ToggleShowProgressInTaskbarCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.ShowProgressInTaskbar);
        Assert.Contains(nameof(MainWindowViewModel.ShowProgressInTaskbar), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.DesktopProgressRequest), changedProperties);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.ShowProgressInTaskbar);
    }

    [Fact]
    public async Task ToggleShowInNotificationAreaRequiresSupportedBackendAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            statusIconSupported: true);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ToggleShowInNotificationAreaCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.IsStatusIconSupported);
        Assert.True(viewModel.ShowInNotificationArea);
        Assert.False(viewModel.CanHideToNotificationArea);
        Assert.True(viewModel.StatusIconMenuState.IsVisible);
        Assert.False(viewModel.StatusIconMenuState.CanHideWindow);
        Assert.Contains(nameof(MainWindowViewModel.ShowInNotificationArea), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.CanHideToNotificationArea), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.StatusIconMenuState), changedProperties);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.ShowInNotificationArea);
    }

    [Fact]
    public async Task SettingsSaveMergesRecentInputWithLatestPersistedSettings()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(showInNotificationArea: false)
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            statusIconSupported: true);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = new LinuxAppSettings(
            ["20 minutes", "12 minutes"],
            showInNotificationArea: true);

        viewModel.TimerInput = "12 minutes";
        viewModel.StartCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.ShowInNotificationArea);
        Assert.Equal(["12 minutes", "20 minutes"], settingsStore.SavedSettings.RecentTimerInputs);
    }

    [Fact]
    public async Task SettingsSaveMergesChangedOptionOverLatestPersistedSettings()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with
        {
            ReverseProgressBar = true,
            ShowInNotificationArea = true
        };

        viewModel.ToggleNotificationsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.NotificationsEnabled);
        Assert.True(settingsStore.SavedSettings.ReverseProgressBar);
        Assert.True(settingsStore.SavedSettings.ShowInNotificationArea);
    }

    [Fact]
    public async Task SettingsSaveAppliesCustomThemeSelectionAsCoherentOptionGroup()
    {
        var firstTheme = new CustomThemeDefinition("theme-1", "Evening");
        var secondTheme = new CustomThemeDefinition("theme-2", "Morning");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default with
            {
                ThemePreference = LinuxThemePreference.Custom,
                CustomThemeId = firstTheme.Id
            },
            LoadedCustomThemes = new CustomThemesDocument(themes: [firstTheme, secondTheme])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with { ThemePreference = LinuxThemePreference.System };

        viewModel.SelectCustomThemeCommand.Execute(secondTheme.Id);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(LinuxThemePreference.Custom, settingsStore.SavedSettings.ThemePreference);
        Assert.Equal(secondTheme.Id, settingsStore.SavedSettings.CustomThemeId);
    }

    [Fact]
    public async Task SettingsSaveAppliesCloseWhenExpiredAsCoherentOptionGroup()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with { LoopTimer = true };

        viewModel.ToggleCloseWhenExpiredCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.CloseWhenExpired);
        Assert.False(settingsStore.SavedSettings.LoopTimer);
        Assert.False(settingsStore.SavedSettings.LoopSound);
    }

    [Fact]
    public async Task SettingsSaveAppliesLoopSoundAsCoherentOptionGroup()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with { CloseWhenExpired = true };

        viewModel.ToggleLoopSoundCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.LoopSound);
        Assert.False(settingsStore.SavedSettings.CloseWhenExpired);
        Assert.False(settingsStore.SavedSettings.LoopTimer);
    }

    [Fact]
    public async Task SettingsSaveKeepsLatestAudioSelectionWhenLoopOptionChanges()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with
        {
            AudioAlertSoundId = AudioAlertSoundIds.QuietBeep
        };

        viewModel.ToggleLoopSoundCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.LoopSound);
        Assert.True(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.QuietBeep, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task SettingsSaveAppliesAudioSoundAsCoherentOptionGroup()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        settingsStore.LoadedSettings = LinuxAppSettings.Default with
        {
            AudioAlertsEnabled = false,
            AudioAlertSoundId = AudioAlertSoundIds.None
        };

        viewModel.SelectAudioAlertSoundCommand.Execute(AudioAlertSoundIds.QuietBeep);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.AudioAlertsEnabled);
        Assert.Equal(AudioAlertSoundIds.QuietBeep, settingsStore.SavedSettings.AudioAlertSoundId);
    }

    [Fact]
    public async Task CoordinatedAppSettingsStorePreservesIndependentStaleWindowChanges()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var appSettingsStore = new CoordinatedAppSettingsStore(settingsStore);
        LinuxAppSettings previous = LinuxAppSettings.Default;

        await appSettingsStore.SaveChangeAsync(
            previous,
            previous with { NotificationsEnabled = false });
        await appSettingsStore.SaveChangeAsync(
            previous,
            previous with { AlwaysOnTop = true });

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.NotificationsEnabled);
        Assert.True(settingsStore.SavedSettings.AlwaysOnTop);
    }

    [Fact]
    public async Task SettingsSaveAppliesMergedSnapshotReturnedBySharedStore()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default
        };
        var appSettingsStore = new CoordinatedAppSettingsStore(settingsStore);
        var firstWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            appSettingsStore: appSettingsStore);
        var secondDispatcher = new RecordingUiDispatcher();
        var secondWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            appSettingsStore: appSettingsStore,
            uiDispatcher: secondDispatcher);
        bool notificationsChangedOnDispatcher = false;

        secondWindow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.NotificationsEnabled))
            {
                notificationsChangedOnDispatcher = secondDispatcher.IsDispatching;
            }
        };

        await firstWindow.LoadSettingsAsync();
        await secondWindow.LoadSettingsAsync();

        firstWindow.ToggleNotificationsCommand.Execute(null);
        await firstWindow.PendingSettingsSave;
        secondWindow.ToggleAlwaysOnTopCommand.Execute(null);
        await secondWindow.PendingSettingsSave;

        Assert.False(secondWindow.NotificationsEnabled);
        Assert.True(secondWindow.AlwaysOnTop);
        Assert.True(notificationsChangedOnDispatcher);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.NotificationsEnabled);
        Assert.True(settingsStore.SavedSettings.AlwaysOnTop);
    }

    [Fact]
    public async Task UnsupportedStatusIconMasksPersistedSettingAndDoesNotSaveToggle()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(showInNotificationArea: true)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.False(viewModel.IsStatusIconSupported);
        Assert.False(viewModel.ShowInNotificationArea);
        Assert.False(viewModel.CanHideToNotificationArea);
        Assert.False(viewModel.ToggleShowInNotificationAreaCommand.CanExecute(null));
        viewModel.ToggleShowInNotificationAreaCommand.Execute(null);
        await viewModel.PendingSettingsSave;
        Assert.Null(settingsStore.SavedSettings);
    }

    [Fact]
    public async Task HideToNotificationAreaCommandRequiresRecoverableStatusIcon()
    {
        var disabled = CreateViewModel(new ManualMonotonicClock(), statusIconSupported: true);
        var unsupported = CreateViewModel(new ManualMonotonicClock());
        var visibleButNotRecoverableSettings = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(showInNotificationArea: true)
        };
        var visibleButNotRecoverable = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: visibleButNotRecoverableSettings,
            statusIconSupported: true);
        var enabledSettings = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(showInNotificationArea: true)
        };
        var enabled = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: enabledSettings,
            statusIconSupported: true,
            statusIconCanRecoverHiddenWindow: true);
        int disabledRequests = 0;
        int unsupportedRequests = 0;
        int visibleButNotRecoverableRequests = 0;
        int enabledRequests = 0;
        disabled.HideToNotificationAreaRequested += (_, _) => disabledRequests++;
        unsupported.HideToNotificationAreaRequested += (_, _) => unsupportedRequests++;
        visibleButNotRecoverable.HideToNotificationAreaRequested += (_, _) => visibleButNotRecoverableRequests++;
        enabled.HideToNotificationAreaRequested += (_, _) => enabledRequests++;
        await visibleButNotRecoverable.LoadSettingsAsync();
        await enabled.LoadSettingsAsync();

        disabled.HideToNotificationAreaCommand.Execute(null);
        unsupported.HideToNotificationAreaCommand.Execute(null);
        visibleButNotRecoverable.HideToNotificationAreaCommand.Execute(null);
        enabled.HideToNotificationAreaCommand.Execute(null);

        Assert.Equal(0, disabledRequests);
        Assert.Equal(0, unsupportedRequests);
        Assert.Equal(0, visibleButNotRecoverableRequests);
        Assert.Equal(1, enabledRequests);
        Assert.False(visibleButNotRecoverable.CanHideToNotificationArea);
        Assert.False(visibleButNotRecoverable.StatusIconMenuState.CanHideWindow);
    }

    [Fact]
    public async Task StatusIconMenuStateTracksTimerCommandAvailability()
    {
        var clock = new ManualMonotonicClock();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(showInNotificationArea: true)
        };
        var viewModel = CreateViewModel(
            clock,
            settingsStore: settingsStore,
            statusIconSupported: true,
            statusIconCanRecoverHiddenWindow: true);
        await viewModel.LoadSettingsAsync();

        StatusIconMenuState stopped = viewModel.StatusIconMenuState;
        viewModel.TimerTitle = "Tea";
        viewModel.TimerInput = "1 minute";
        viewModel.StartCommand.Execute(null);
        StatusIconMenuState running = viewModel.StatusIconMenuState;
        viewModel.PauseResumeCommand.Execute(null);
        StatusIconMenuState paused = viewModel.StatusIconMenuState;

        Assert.True(stopped.IsVisible);
        Assert.Equal("Hourglass", stopped.ToolTipText);
        Assert.False(stopped.CanPauseResume);
        Assert.False(stopped.CanStop);
        Assert.False(stopped.CanRestart);
        Assert.True(stopped.CanHideWindow);
        Assert.True(stopped.CanExit);
        Assert.Equal("Tea", running.ToolTipText);
        Assert.True(running.CanPauseResume);
        Assert.True(running.CanStop);
        Assert.True(running.CanRestart);
        Assert.Equal("Pause", running.PauseResumeText);
        Assert.True(paused.CanPauseResume);
        Assert.True(paused.CanStop);
        Assert.True(paused.CanRestart);
        Assert.Equal("Resume", paused.PauseResumeText);
    }

    [Fact]
    public async Task TogglePopUpWhenExpiredRaisesPropertyChangedAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TogglePopUpWhenExpiredCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.PopUpWhenExpired);
        Assert.Contains(nameof(MainWindowViewModel.PopUpWhenExpired), changedProperties);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.PopUpWhenExpired);
    }

    [Fact]
    public async Task TogglePromptOnExitRaisesPropertyChangedAndSavesSettings()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TogglePromptOnExitCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.PromptOnExit);
        Assert.Contains(nameof(MainWindowViewModel.PromptOnExit), changedProperties);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.PromptOnExit);
    }

    [Fact]
    public async Task ExitPromptAppliesOnlyToActiveTimersWhenEnabled()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        Assert.False(viewModel.ShouldPromptOnExit);

        viewModel.TimerInput = "1 minute";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.ShouldPromptOnExit);
        viewModel.PauseResumeCommand.Execute(null);
        Assert.True(viewModel.ShouldPromptOnExit);

        viewModel.TogglePromptOnExitCommand.Execute(null);
        await viewModel.PendingSettingsSave;
        Assert.False(viewModel.ShouldPromptOnExit);
    }

    [Fact]
    public async Task Milestone3OptionTogglesSaveSettingsAndEnforceMutualExclusion()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleReverseProgressBarCommand.Execute(null);
        viewModel.ToggleShowTimeElapsedCommand.Execute(null);
        viewModel.ToggleLoopTimerCommand.Execute(null);
        viewModel.ToggleCloseWhenExpiredCommand.Execute(null);
        viewModel.ToggleLoopSoundCommand.Execute(null);
        viewModel.ToggleDoNotKeepComputerAwakeCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.True(viewModel.ReverseProgressBar);
        Assert.True(viewModel.ShowTimeElapsed);
        Assert.False(viewModel.LoopTimer);
        Assert.False(viewModel.CloseWhenExpired);
        Assert.True(viewModel.LoopSound);
        Assert.True(viewModel.DoNotKeepComputerAwake);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.True(settingsStore.SavedSettings.ReverseProgressBar);
        Assert.True(settingsStore.SavedSettings.ShowTimeElapsed);
        Assert.False(settingsStore.SavedSettings.LoopTimer);
        Assert.False(settingsStore.SavedSettings.CloseWhenExpired);
        Assert.True(settingsStore.SavedSettings.LoopSound);
        Assert.True(settingsStore.SavedSettings.DoNotKeepComputerAwake);
    }

    [Fact]
    public void LoopTimerRestartsDurationTimersAndEmitsOneExpiryCycle()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(clock, notificationService: notificationService, audioAlertService: audioAlertService);
        int visualRequests = 0;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => visualRequests++;
        viewModel.ToggleLoopTimerCommand.Execute(null);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);

        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal(1, visualRequests);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
        Assert.Equal("00:00:01", viewModel.RemainingTime);
    }

    [Fact]
    public void LoopSoundUsesStoppablePlaybackAndStopsOnDismissal()
    {
        var clock = new ManualMonotonicClock();
        var audioAlertService = new RecordingAudioAlertService();
        var viewModel = CreateViewModel(clock, audioAlertService: audioAlertService);
        viewModel.ToggleLoopSoundCommand.Execute(null);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);

        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();
        bool dismissed = viewModel.TryHandleEscape();

        Assert.True(dismissed);
        Assert.Equal(1, audioAlertService.LoopingCallCount);
        Assert.Equal(1, audioAlertService.StopCount);
        Assert.Equal(TimerState.Stopped, viewModel.State);
    }

    [Fact]
    public void CloseWhenExpiredRequestsCloseAndSuppressesAttention()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        int closeRequests = 0;
        int attentionRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;
        viewModel.WindowAttentionRequested += (_, _) => attentionRequests++;
        viewModel.ToggleCloseWhenExpiredCommand.Execute(null);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);

        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(1, closeRequests);
        Assert.Equal(0, attentionRequests);
    }

    [Fact]
    public async Task CloseWhenExpiredPublishesCloseThroughUiDispatcher()
    {
        var clock = new ManualMonotonicClock();
        var uiDispatcher = new RecordingUiDispatcher();
        var viewModel = CreateViewModel(clock, uiDispatcher: uiDispatcher);
        var closeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.CloseRequested += (_, _) =>
        {
            Assert.True(uiDispatcher.IsDispatching);
            closeRequested.TrySetResult();
        };
        viewModel.ToggleCloseWhenExpiredCommand.Execute(null);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);

        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Task finished = await Task.WhenAny(closeRequested.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(closeRequested.Task, finished);
    }

    [Fact]
    public void LockInterfaceBlocksTimerMutationUntilExpiry()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);
        viewModel.ToggleLockInterfaceCommand.Execute(null);
        viewModel.TimerInput = "2 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.True(viewModel.IsTimerModificationLocked);
        Assert.False(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.False(viewModel.ResetCommand.CanExecute(null));
        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.False(viewModel.TryEnterTimerInputMode());

        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.False(viewModel.IsTimerModificationLocked);
        Assert.True(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void KeepAwakePreferenceAppliesImmediatelyToRunningTimer()
    {
        var clock = new ManualMonotonicClock();
        var sessionInhibitor = new RecordingSessionInhibitor();
        var viewModel = CreateViewModel(clock, sessionInhibitor: sessionInhibitor);
        viewModel.TimerInput = "1 minute";
        viewModel.StartCommand.Execute(null);

        viewModel.ToggleDoNotKeepComputerAwakeCommand.Execute(null);
        viewModel.ToggleDoNotKeepComputerAwakeCommand.Execute(null);

        Assert.Equal(2, sessionInhibitor.AcquireCount);
        Assert.Equal(1, sessionInhibitor.ReleaseCount);
    }

    [Fact]
    public void UnsupportedShutdownCannotBeEnabledOrInvoked()
    {
        var clock = new ManualMonotonicClock();
        var powerService = new RecordingSystemPowerService { IsShutdownSupported = false };
        var viewModel = CreateViewModel(clock, systemPowerService: powerService);

        viewModel.ToggleShutDownWhenExpiredCommand.Execute(null);
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.False(viewModel.ShutDownWhenExpired);
        Assert.Equal(0, powerService.ShutdownRequestCount);
    }

    [Fact]
    public async Task SettingsSaveFailureDoesNotCrashOrChangeTimerState()
    {
        var settingsStore = new RecordingSettingsStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.False(viewModel.NotificationsEnabled);
        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task LoadSettingsResumesOnCapturedSchedulerBeforePublishingState()
    {
        var scheduler = new QueuedTaskScheduler();
        var taskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            scheduler);
        var settingsStore = new DeferredSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        Task<Task> scheduledLoadTask = taskFactory.StartNew(() =>
        {
            SynchronizationContext? originalContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                return viewModel.LoadSettingsAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }
        });
        scheduler.RunNext();
        Task loadTask = await scheduledLoadTask;

        settingsStore.Complete(new LinuxAppSettings(["15 minutes"], notificationsEnabled: true));

        Assert.False(loadTask.IsCompleted);
        Assert.True(scheduler.PendingCount > 0);
        Assert.Equal("5 minutes", viewModel.TimerInput);

        while (!loadTask.IsCompleted)
        {
            Assert.True(scheduler.PendingCount > 0);
            scheduler.RunNext();
        }

        await loadTask;

        Assert.Equal("15 minutes", viewModel.TimerInput);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task LoadSettingsFailureKeepsDefaultTimerInput()
    {
        var settingsStore = new RecordingSettingsStore { ThrowOnLoad = true };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal("5 minutes", viewModel.TimerInput);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public void StartWithValidInputSavesRecentTimerInput()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Equal(["90 seconds"], settingsStore.SavedSettings.RecentTimerInputs);
    }

    [Fact]
    public async Task RecentInputCommandLoadsInputModeWithoutStartingTimer()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["15 minutes", "10 seconds"])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        await viewModel.LoadSettingsAsync();

        viewModel.SelectRecentInputCommand.Execute("10 seconds");
        await viewModel.PendingSettingsSave;

        Assert.Equal("10 seconds", viewModel.TimerInput);
        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.True(viewModel.IsTimerInputVisible);
        Assert.Equal(["15 minutes", "10 seconds"], viewModel.RecentInputMenuItems.Select(item => item.TimerInput).ToArray());
        Assert.NotNull(settingsStore.SavedActiveSession);
        Assert.Equal("10 seconds", settingsStore.SavedActiveSession.TimerInput);
    }

    [Fact]
    public async Task ClearRecentInputsUpdatesSettingsAndMenu()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["15 minutes", "10 seconds"])
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        await viewModel.LoadSettingsAsync();

        viewModel.ClearRecentInputsCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.Empty(viewModel.RecentInputMenuItems);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.Empty(settingsStore.SavedSettings.RecentTimerInputs);
    }

    [Fact]
    public async Task SaveOpenAndRemoveSavedTimerUsesSeparateDocument()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        viewModel.TimerInput = "90 seconds";
        viewModel.TimerTitle = "Tea";
        viewModel.ToggleReverseProgressBarCommand.Execute(null);
        viewModel.SelectWindowTitleModeCommand.Execute(nameof(WindowTitleMode.TimerTitlePlusTimeLeft));

        viewModel.SaveCurrentTimerCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedTimers);
        SavedTimerDefinition savedTimer = Assert.Single(settingsStore.SavedTimers.Timers);
        Assert.Equal("Tea — 90 seconds", savedTimer.Header);
        Assert.True(savedTimer.Options.ReverseProgressBar);
        Assert.Equal(WindowTitleMode.TimerTitlePlusTimeLeft, savedTimer.Options.WindowTitleMode);
        Assert.Single(viewModel.SavedTimerMenuItems);

        viewModel.TimerInput = "5 minutes";
        viewModel.TimerTitle = "";
        viewModel.SelectWindowTitleModeCommand.Execute(nameof(WindowTitleMode.TimeElapsed));
        viewModel.OpenSavedTimerCommand.Execute(savedTimer.Id);
        await viewModel.PendingSettingsSave;

        Assert.Equal("90 seconds", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.True(viewModel.ReverseProgressBar);
        Assert.Equal(WindowTitleMode.TimerTitlePlusTimeLeft, viewModel.WindowTitleMode);
        Assert.Equal(TimerState.Stopped, viewModel.State);

        viewModel.RemoveSavedTimerCommand.Execute(savedTimer.Id);
        await viewModel.PendingSettingsSave;

        Assert.Empty(viewModel.SavedTimerMenuItems);
        Assert.NotNull(settingsStore.SavedTimers);
        Assert.Empty(settingsStore.SavedTimers.Timers);
    }

    [Fact]
    public async Task OpenAllSavedTimersPublishesCurrentSavedTimerSnapshot()
    {
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);
        SavedTimersDocument? requestedSavedTimers = null;
        viewModel.OpenAllSavedTimersRequested += (_, args) => requestedSavedTimers = args.SavedTimers;
        viewModel.TimerInput = "90 seconds";
        viewModel.TimerTitle = "Tea";

        viewModel.SaveCurrentTimerCommand.Execute(null);
        SavedTimerMenuItem savedTimer = Assert.Single(viewModel.SavedTimerMenuItems);
        viewModel.OpenAllSavedTimersCommand.Execute(null);

        Assert.NotNull(requestedSavedTimers);
        SavedTimerDefinition requestedTimer = Assert.Single(requestedSavedTimers.Timers);
        Assert.Equal(savedTimer.Id, requestedTimer.Id);

        viewModel.RemoveSavedTimerCommand.Execute(savedTimer.Id);
        requestedSavedTimers = null;
        viewModel.OpenAllSavedTimersCommand.Execute(null);

        Assert.Null(requestedSavedTimers);
        await viewModel.PendingSettingsSave;
    }

    [Fact]
    public async Task CoordinatedSavedTimerSavesPreserveAdditionsFromOtherWindows()
    {
        var settingsStore = new RecordingSettingsStore();
        var savedTimersStore = new CoordinatedSavedTimersStore(settingsStore);
        var firstWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        var secondWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        await firstWindow.LoadSettingsAsync();
        await secondWindow.LoadSettingsAsync();

        firstWindow.TimerInput = "10 minutes";
        firstWindow.TimerTitle = "Tea";
        firstWindow.SaveCurrentTimerCommand.Execute(null);
        secondWindow.TimerInput = "20 minutes";
        secondWindow.TimerTitle = "Coffee";
        secondWindow.SaveCurrentTimerCommand.Execute(null);
        await Task.WhenAll(firstWindow.PendingSettingsSave, secondWindow.PendingSettingsSave);

        Assert.NotNull(settingsStore.SavedTimers);
        Assert.Equal(
            ["Coffee — 20 minutes", "Tea — 10 minutes"],
            settingsStore.SavedTimers.Timers.Select(timer => timer.Header).ToArray());
    }

    [Fact]
    public async Task CoordinatedSavedTimerRemovePreservesAdditionsFromOtherWindows()
    {
        var originalTimer = new SavedTimerDefinition("timer-1", "10 minutes", "Tea");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSavedTimers = new SavedTimersDocument(timers: [originalTimer])
        };
        var savedTimersStore = new CoordinatedSavedTimersStore(settingsStore);
        var firstWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        var secondWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        await firstWindow.LoadSettingsAsync();
        await secondWindow.LoadSettingsAsync();

        firstWindow.TimerInput = "20 minutes";
        firstWindow.TimerTitle = "Coffee";
        firstWindow.SaveCurrentTimerCommand.Execute(null);
        await firstWindow.PendingSettingsSave;
        secondWindow.RemoveSavedTimerCommand.Execute(originalTimer.Id);
        await secondWindow.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedTimers);
        SavedTimerDefinition remainingTimer = Assert.Single(settingsStore.SavedTimers.Timers);
        Assert.Equal("Coffee — 20 minutes", remainingTimer.Header);
    }

    [Fact]
    public async Task CoordinatedSavedTimerSaveDoesNotRestoreTimerRemovedByOtherWindow()
    {
        var originalTimer = new SavedTimerDefinition("timer-1", "10 minutes", "Tea");
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSavedTimers = new SavedTimersDocument(timers: [originalTimer])
        };
        var savedTimersStore = new CoordinatedSavedTimersStore(settingsStore);
        var firstWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        var secondWindow = CreateViewModel(
            new ManualMonotonicClock(),
            settingsStore: settingsStore,
            savedTimersStore: savedTimersStore);
        await firstWindow.LoadSettingsAsync();
        await secondWindow.LoadSettingsAsync();

        firstWindow.RemoveSavedTimerCommand.Execute(originalTimer.Id);
        await firstWindow.PendingSettingsSave;
        secondWindow.TimerInput = "20 minutes";
        secondWindow.TimerTitle = "Coffee";
        secondWindow.SaveCurrentTimerCommand.Execute(null);
        await secondWindow.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedTimers);
        SavedTimerDefinition remainingTimer = Assert.Single(settingsStore.SavedTimers.Timers);
        Assert.Equal("Coffee — 20 minutes", remainingTimer.Header);
    }

    [Fact]
    public async Task ActiveSessionPersistsRunningTimerAndRestoresExpiredOnStartup()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 7, 2, 8, 0, 0);
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(clock, wallClockNow: () => now, settingsStore: settingsStore);
        viewModel.TimerInput = "10 seconds";
        viewModel.TimerTitle = "Tea";
        viewModel.SelectWindowTitleModeCommand.Execute(nameof(WindowTitleMode.TimeLeftPlusTimerTitle));

        viewModel.StartCommand.Execute(null);
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedActiveSession);
        Assert.Equal(TimerState.Running, settingsStore.SavedActiveSession.State);
        Assert.Equal("10 seconds", settingsStore.SavedActiveSession.TimerInput);
        Assert.Equal("Tea", settingsStore.SavedActiveSession.TimerTitle);
        Assert.Equal(WindowTitleMode.TimeLeftPlusTimerTitle, settingsStore.SavedActiveSession.Options.WindowTitleMode);

        var restoredNotifications = new RecordingNotificationService();
        var restoredAudio = new RecordingAudioAlertService();
        var restored = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => now.AddSeconds(15),
            notificationService: restoredNotifications,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default,
                LoadedActiveSession = settingsStore.SavedActiveSession
            },
            audioAlertService: restoredAudio);
        int visualRequests = 0;
        int attentionRequests = 0;
        restored.WindowAttentionRequested += (_, _) => attentionRequests++;
        restored.ExpiryVisualFeedbackRequested += (_, _) => visualRequests++;

        await restored.LoadSettingsAsync();

        Assert.Equal(TimerState.Expired, restored.State);
        Assert.True(restored.IsCompletionTextVisible);
        Assert.Equal("Timer complete", restored.StatusText);
        Assert.Equal("Tea", restored.TimerTitle);
        Assert.Equal(1, visualRequests);
        Assert.Equal(1, attentionRequests);
        Assert.Equal(1, restoredNotifications.CallCount);
        Assert.Equal(1, restoredAudio.CallCount);
    }

    [Fact]
    public async Task RestoredExpiredSessionClearsLockInterfaceBeforeRestart()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default with { LockInterface = true },
            LoadedActiveSession = new ActiveTimerSessionDocument(
                timerInput: "10 seconds",
                timerStartInput: "10 seconds",
                timerTitle: "Tea",
                presentationMode: ActiveTimerPresentationMode.Status,
                savedAt: start.AddSeconds(5),
                state: TimerState.Running,
                startTime: start,
                endTime: end,
                timeElapsedTicks: TimeSpan.FromSeconds(5).Ticks,
                timeLeftTicks: TimeSpan.FromSeconds(5).Ticks,
                totalTimeTicks: TimeSpan.FromSeconds(10).Ticks)
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => end.AddSeconds(5),
            notificationService: notificationService,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);
        int visualRequests = 0;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => visualRequests++;

        await viewModel.LoadSettingsAsync();
        await viewModel.PendingSettingsSave;

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.True(viewModel.IsCompletionTextVisible);
        Assert.False(viewModel.LockInterface);
        Assert.False(viewModel.IsTimerModificationLocked);
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.LockInterface);
        Assert.Equal(1, visualRequests);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);

        viewModel.RestartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.False(viewModel.IsTimerModificationLocked);
    }

    [Fact]
    public async Task ActiveSessionRestoresPerWindowTimerOptions()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default,
            LoadedActiveSession = new ActiveTimerSessionDocument(
                timerInput: "10 seconds",
                timerStartInput: "10 seconds",
                timerTitle: "Tea",
                savedAt: start.AddSeconds(5),
                state: TimerState.Paused,
                timeElapsedTicks: TimeSpan.FromSeconds(5).Ticks,
                timeLeftTicks: TimeSpan.FromSeconds(5).Ticks,
                totalTimeTicks: TimeSpan.FromSeconds(10).Ticks,
                options: new SavedTimerOptions(
                    ReverseProgressBar: true,
                    ShowTimeElapsed: true,
                    LoopTimer: true,
                    DoNotKeepComputerAwake: true,
                    WindowTitleMode: WindowTitleMode.TimeElapsedPlusTimerTitle))
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => start.AddSeconds(5),
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.True(viewModel.ReverseProgressBar);
        Assert.True(viewModel.ShowTimeElapsed);
        Assert.True(viewModel.LoopTimer);
        Assert.True(viewModel.DoNotKeepComputerAwake);
        Assert.Equal(WindowTitleMode.TimeElapsedPlusTimerTitle, viewModel.WindowTitleMode);
    }

    [Fact]
    public async Task RestoredExpiredSessionQueuesSessionSaveBeforeSlowNotificationCompletes()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var notificationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationService = new RecordingNotificationService
        {
            Completion = notificationCompletion.Task
        };
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default,
            LoadedActiveSession = new ActiveTimerSessionDocument(
                timerInput: "10 seconds",
                timerStartInput: "10 seconds",
                timerTitle: "Tea",
                presentationMode: ActiveTimerPresentationMode.Status,
                savedAt: start.AddSeconds(5),
                state: TimerState.Running,
                startTime: start,
                endTime: end,
                timeElapsedTicks: TimeSpan.FromSeconds(5).Ticks,
                timeLeftTicks: TimeSpan.FromSeconds(5).Ticks,
                totalTimeTicks: TimeSpan.FromSeconds(10).Ticks)
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => end.AddSeconds(5),
            notificationService: notificationService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        await viewModel.PendingSettingsSave;

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.NotNull(settingsStore.SavedActiveSession);
        Assert.Equal(TimerState.Expired, settingsStore.SavedActiveSession.State);
        Assert.Equal(TimeSpan.Zero.Ticks, settingsStore.SavedActiveSession.TimeLeftTicks);
        Assert.Equal(1, notificationService.CallCount);

        notificationCompletion.SetResult();
    }

    [Fact]
    public async Task AlreadyExpiredActiveSessionRestoresCompletedDisplayWithoutReplayingEffects()
    {
        DateTime start = new(2026, 7, 2, 8, 0, 0);
        DateTime end = start.AddSeconds(10);
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = LinuxAppSettings.Default,
            LoadedActiveSession = new ActiveTimerSessionDocument(
                timerInput: "10 seconds",
                timerStartInput: "10 seconds",
                timerTitle: "Tea",
                presentationMode: ActiveTimerPresentationMode.Status,
                savedAt: end.AddSeconds(5),
                state: TimerState.Expired,
                startTime: start,
                endTime: end,
                timeElapsedTicks: TimeSpan.FromSeconds(15).Ticks,
                timeLeftTicks: TimeSpan.Zero.Ticks,
                timeExpiredTicks: TimeSpan.FromSeconds(5).Ticks,
                totalTimeTicks: TimeSpan.FromSeconds(10).Ticks)
        };
        var viewModel = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => end.AddSeconds(15),
            notificationService: notificationService,
            settingsStore: settingsStore,
            audioAlertService: audioAlertService);
        int visualRequests = 0;
        int attentionRequests = 0;
        viewModel.ExpiryVisualFeedbackRequested += (_, _) => visualRequests++;
        viewModel.WindowAttentionRequested += (_, _) => attentionRequests++;

        await viewModel.LoadSettingsAsync();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.True(viewModel.IsCompletionTextVisible);
        Assert.True(viewModel.HasCompletionEmphasis);
        Assert.Equal("10 seconds", viewModel.TimerInput);
        Assert.Equal("Tea", viewModel.TimerTitle);
        Assert.Equal(0, visualRequests);
        Assert.Equal(0, attentionRequests);
        Assert.Equal(0, notificationService.CallCount);
        Assert.Equal(0, audioAlertService.CallCount);
    }

    [Fact]
    public async Task ActiveSessionPreservesRunningTimerWhenEditorTextIsInvalid()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 7, 2, 8, 0, 0);
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(clock, wallClockNow: () => now, settingsStore: settingsStore);
        viewModel.TimerInput = "10 seconds";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());
        viewModel.TimerInput = "10 secon";
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedActiveSession);
        Assert.Equal("10 secon", settingsStore.SavedActiveSession.TimerInput);
        Assert.Equal("10 seconds", settingsStore.SavedActiveSession.TimerStartInput);

        now = now.AddSeconds(4);
        var sessionInhibitor = new RecordingSessionInhibitor();
        var restored = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => now,
            sessionInhibitor: sessionInhibitor,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default,
                LoadedActiveSession = settingsStore.SavedActiveSession
            });

        await restored.LoadSettingsAsync();

        Assert.Equal(TimerState.Running, restored.State);
        Assert.True(restored.IsTimerInputVisible);
        Assert.False(restored.IsRemainingTimeVisible);
        Assert.Equal("10 secon", restored.TimerInput);
        Assert.Equal("Tea", restored.TimerTitle);
        Assert.Equal(1, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public async Task ActiveSessionPreservesPausedTimerWhenEditorTextIsInvalid()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 7, 2, 8, 0, 0);
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(clock, wallClockNow: () => now, settingsStore: settingsStore);
        viewModel.TimerInput = "10 seconds";
        viewModel.TimerTitle = "Tea";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(4));
        viewModel.Tick();
        viewModel.PauseResumeCommand.Execute(null);
        Assert.True(viewModel.TryEnterTimerInputMode());
        viewModel.TimerInput = "10 secon";
        await viewModel.PendingSettingsSave;

        Assert.NotNull(settingsStore.SavedActiveSession);
        Assert.Equal("10 secon", settingsStore.SavedActiveSession.TimerInput);
        Assert.Equal("10 seconds", settingsStore.SavedActiveSession.TimerStartInput);

        var sessionInhibitor = new RecordingSessionInhibitor();
        var restored = CreateViewModel(
            new ManualMonotonicClock(),
            wallClockNow: () => now.AddMinutes(5),
            sessionInhibitor: sessionInhibitor,
            settingsStore: new RecordingSettingsStore
            {
                LoadedSettings = LinuxAppSettings.Default,
                LoadedActiveSession = settingsStore.SavedActiveSession
            });

        await restored.LoadSettingsAsync();

        Assert.Equal(TimerState.Paused, restored.State);
        Assert.True(restored.IsTimerInputVisible);
        Assert.False(restored.IsRemainingTimeVisible);
        Assert.Equal("10 secon", restored.TimerInput);
        Assert.Equal("Tea", restored.TimerTitle);
        Assert.Equal(0, sessionInhibitor.AcquireCount);
    }

    [Fact]
    public async Task DisabledActiveSessionRestoreUsesRecentInputFallback()
    {
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["15 minutes"], restoreActiveSessionOnStartup: false),
            LoadedActiveSession = new ActiveTimerSessionDocument(
                timerInput: "10 seconds",
                state: TimerState.Running,
                startTime: new DateTime(2026, 7, 2, 8, 0, 0),
                endTime: new DateTime(2026, 7, 2, 8, 0, 10),
                totalTimeTicks: TimeSpan.FromSeconds(10).Ticks)
        };
        var viewModel = CreateViewModel(new ManualMonotonicClock(), settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("15 minutes", viewModel.TimerInput);
    }

    [Fact]
    public async Task ExpiredTimerDoesNotNotifyWhenNotificationsAreDisabled()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(["1 second"], notificationsEnabled: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, notificationService.CallCount);
        Assert.Equal(1, audioAlertService.CallCount);
    }

    [Fact]
    public async Task ExpiredTimerDoesNotPlayAudioWhenAudioAlertsAreDisabled()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore
        {
            LoadedSettings = new LinuxAppSettings(
                ["1 second"],
                notificationsEnabled: true,
                audioAlertsEnabled: false)
        };
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        await viewModel.LoadSettingsAsync();
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(1, notificationService.CallCount);
        Assert.Equal(0, audioAlertService.CallCount);
    }

    [Fact]
    public async Task ExpiredTimerUsesLatestNotificationAndAudioSettings()
    {
        var clock = new ManualMonotonicClock();
        var notificationService = new RecordingNotificationService();
        var audioAlertService = new RecordingAudioAlertService();
        var settingsStore = new RecordingSettingsStore();
        var viewModel = CreateViewModel(
            clock,
            notificationService: notificationService,
            audioAlertService: audioAlertService,
            settingsStore: settingsStore);

        viewModel.ToggleNotificationsCommand.Execute(null);
        viewModel.ToggleAudioAlertsCommand.Execute(null);
        await viewModel.PendingSettingsSave;
        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal(0, notificationService.CallCount);
        Assert.Equal(0, audioAlertService.CallCount);
    }

    [Theory]
    [InlineData(TimerState.Stopped, "Ready", "Pause", true, false, "00:00:00", 0, true, false, false, true, false, false, false)]
    [InlineData(TimerState.Running, "Running", "Pause", false, true, "00:01:05", 100, false, true, false, false, true, false, true)]
    [InlineData(TimerState.Paused, "Paused", "Resume", false, false, "00:01:05", 100, false, true, false, false, false, true, true)]
    [InlineData(TimerState.Expired, "Timer complete", "Pause", false, false, "00:00:00", 0, false, false, true, false, false, false, true)]
    public void TimerViewStateProjectsDomainState(
        TimerState state,
        string statusText,
        string pauseResumeText,
        bool isInputEnabled,
        bool isRunning,
        string remainingTime,
        double progressPercent,
        bool isTimerInputVisible,
        bool isRemainingTimeVisible,
        bool isCompletionTextVisible,
        bool isStartVisible,
        bool isPauseVisible,
        bool isResumeVisible,
        bool isStopVisible)
    {
        CountdownState countdownState = CreateCountdownState(state);

        TimerViewState viewState = TimerViewState.FromTimerState("65 seconds", countdownState);

        Assert.Equal(remainingTime, viewState.RemainingTime);
        Assert.Equal(statusText, viewState.StatusText);
        Assert.Equal(pauseResumeText, viewState.PauseResumeText);
        Assert.Equal(isInputEnabled, viewState.IsInputEnabled);
        Assert.Equal(isRunning, viewState.IsRunning);
        Assert.Equal(state, viewState.State);
        Assert.Equal(progressPercent, viewState.ProgressPercent);
        Assert.Equal(isTimerInputVisible, viewState.IsTimerInputVisible);
        Assert.Equal(isRemainingTimeVisible, viewState.IsRemainingTimeVisible);
        Assert.Equal(isCompletionTextVisible, viewState.IsCompletionTextVisible);
        Assert.Equal(isStartVisible, viewState.IsStartVisible);
        Assert.Equal(isPauseVisible, viewState.IsPauseVisible);
        Assert.Equal(isResumeVisible, viewState.IsResumeVisible);
        Assert.Equal(isStopVisible, viewState.IsStopVisible);
        Assert.Equal(state != TimerState.Stopped, viewState.IsRestartVisible);
        Assert.Equal(state == TimerState.Expired, viewState.HasCompletionEmphasis);
        Assert.False(viewState.HasValidationError);
    }

    [Fact]
    public void TimerViewStateKeepsValidationSeparateFromTimerState()
    {
        TimerViewState viewState = TimerViewState.FromTimerState(
            "invalid",
            CountdownState.Stopped,
            explicitStatus: "Enter a valid current timer.",
            hasValidationError: true);

        Assert.True(viewState.HasValidationError);
        Assert.False(viewState.HasCompletionEmphasis);
    }

    [Fact]
    public void TimerViewStateCalculatesRemainingProgressByDefault()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState halfway = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(5)).State;

        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", halfway);

        Assert.Equal(50, viewState.ProgressPercent);
    }

    [Fact]
    public void TimerViewStateCalculatesReverseProgressFromElapsedTime()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState quarterElapsed = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(2.5)).State;

        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", quarterElapsed, reverseProgressBar: true);

        Assert.Equal(25, viewState.ProgressPercent);
    }

    [Fact]
    public void DesktopProgressProjectionMapsTimerStates()
    {
        DateTime startTime = new(2026, 6, 8, 10, 0, 0);
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            startTime,
            TimeSpan.Zero).State;
        CountdownState halfway = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(5)).State;
        CountdownState paused = CountdownTransitions.Pause(running, TimeSpan.FromSeconds(5)).State;
        CountdownState expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(10)).State;

        DesktopProgressRequest stoppedRequest = DesktopProgressProjection.FromViewState(
            TimerViewState.FromTimerState("10 seconds", CountdownState.Stopped),
            showProgressInTaskbar: true);
        DesktopProgressRequest runningRequest = DesktopProgressProjection.FromViewState(
            TimerViewState.FromTimerState("10 seconds", halfway),
            showProgressInTaskbar: true);
        DesktopProgressRequest pausedRequest = DesktopProgressProjection.FromViewState(
            TimerViewState.FromTimerState("10 seconds", paused),
            showProgressInTaskbar: true);
        DesktopProgressRequest expiredRequest = DesktopProgressProjection.FromViewState(
            TimerViewState.FromTimerState("10 seconds", expired),
            showProgressInTaskbar: true);
        DesktopProgressRequest disabledRequest = DesktopProgressProjection.FromViewState(
            TimerViewState.FromTimerState("10 seconds", halfway),
            showProgressInTaskbar: false);

        Assert.Equal(DesktopProgressState.Hidden, stoppedRequest.State);
        Assert.Equal(DesktopProgressState.Normal, runningRequest.State);
        Assert.Equal(0.5, runningRequest.Fraction);
        Assert.Equal(DesktopProgressState.Paused, pausedRequest.State);
        Assert.Equal(0.5, pausedRequest.Fraction);
        Assert.Equal(DesktopProgressState.Error, expiredRequest.State);
        Assert.Equal(1, expiredRequest.Fraction);
        Assert.Equal(DesktopProgressState.Hidden, disabledRequest.State);
    }

    [Fact]
    public void DesktopProgressProjectionRespectsReverseProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState quarterElapsed = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(2.5)).State;
        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", quarterElapsed, reverseProgressBar: true);

        DesktopProgressRequest request = DesktopProgressProjection.FromViewState(viewState, showProgressInTaskbar: true);

        Assert.Equal(DesktopProgressState.Normal, request.State);
        Assert.Equal(0.25, request.Fraction);
    }

    [Fact]
    public async Task DesktopProgressControllerAppliesSupportedRequestsAndSkipsDuplicates()
    {
        var service = new RecordingDesktopProgressService { Supported = true };
        var controller = new DesktopProgressController(service);
        var request = new DesktopProgressRequest(DesktopProgressState.Normal, 0.5);

        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);
        await controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Paused, 0.5));
        await controller.ClearAsync();

        Assert.Equal(2, service.SetCount);
        Assert.Equal(1, service.ClearCount);
        Assert.Equal(DesktopProgressState.Paused, service.State);
        Assert.Equal(0.5, service.Fraction);
    }

    [Fact]
    public async Task DesktopProgressControllerClearsUnsupportedRequests()
    {
        var service = new RecordingDesktopProgressService { Supported = false };
        var controller = new DesktopProgressController(service);
        var request = new DesktopProgressRequest(DesktopProgressState.Normal, 0.5);

        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);

        Assert.Equal(0, service.SetCount);
        Assert.Equal(1, service.ClearCount);
    }

    [Fact]
    public async Task DesktopProgressControllerIsolatesServiceFailures()
    {
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            ThrowOnSet = true,
            ThrowOnClear = true
        };
        var controller = new DesktopProgressController(service);

        await controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Normal, 0.5));
        await controller.ClearAsync();

        Assert.Equal(1, service.SetCount);
        Assert.Equal(1, service.ClearCount);
    }

    [Fact]
    public async Task DesktopProgressControllerRetriesSupportedRequestAfterFailedApply()
    {
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            SetFailuresRemaining = 1
        };
        var controller = new DesktopProgressController(service);
        var request = new DesktopProgressRequest(DesktopProgressState.Normal, 0.5);

        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);

        Assert.Equal(2, service.SetCount);
        Assert.Equal(DesktopProgressState.Normal, service.State);
        Assert.Equal(0.5, service.Fraction);
    }

    [Fact]
    public async Task DesktopProgressControllerRetriesHiddenRequestAfterFailedClear()
    {
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            ClearFailuresRemaining = 1
        };
        var controller = new DesktopProgressController(service);
        var request = new DesktopProgressRequest(DesktopProgressState.Hidden, 0);

        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);
        await controller.ApplyAsync(request);

        Assert.Equal(2, service.ClearCount);
        Assert.Equal(0, service.SetCount);
    }

    [Fact]
    public async Task DesktopProgressControllerSequencesClearAfterInFlightApply()
    {
        var setCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            SetCompletion = setCompletion
        };
        var controller = new DesktopProgressController(service);

        Task applyTask = controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Normal, 0.5));
        Task clearTask = controller.ClearAsync();

        Assert.Equal(1, service.SetCount);
        Assert.Equal(0, service.ClearCount);

        setCompletion.SetResult();
        await Task.WhenAll(applyTask, clearTask);

        Assert.Equal(1, service.ClearCount);
        Assert.Equal(["set:Normal:0.5", "clear"], service.Operations);
    }

    [Fact]
    public async Task DesktopProgressControllerSequencesApplyRequestsInOrder()
    {
        var firstSetCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            SetCompletion = firstSetCompletion
        };
        var controller = new DesktopProgressController(service);

        Task firstApply = controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Normal, 0.5));
        Task secondApply = controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Paused, 0.25));

        Assert.Equal(1, service.SetCount);

        service.SetCompletion = null;
        firstSetCompletion.SetResult();
        await Task.WhenAll(firstApply, secondApply);

        Assert.Equal(2, service.SetCount);
        Assert.Equal(["set:Normal:0.5", "set:Paused:0.25"], service.Operations);
    }

    [Fact]
    public async Task DesktopProgressControllerPropagatesCancellationWhileWaitingForOperationGate()
    {
        var setCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new RecordingDesktopProgressService
        {
            Supported = true,
            SetCompletion = setCompletion
        };
        var controller = new DesktopProgressController(service);
        using var cancellation = new CancellationTokenSource();

        Task applyTask = controller.ApplyAsync(new DesktopProgressRequest(DesktopProgressState.Normal, 0.5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.ClearAsync(cancellation.Token));

        setCompletion.SetResult();
        await applyTask;
        Assert.Equal(0, service.ClearCount);
    }

    [Fact]
    public void TimerViewStateDisplaysElapsedTimeWhenEnabledAndFreezesAtTotalDuration()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState quarterElapsed = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(2.5)).State;
        CountdownState expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(30)).State;

        TimerViewState runningViewState = TimerViewState.FromTimerState("10 seconds", quarterElapsed, showTimeElapsed: true);
        TimerViewState expiredViewState = TimerViewState.FromTimerState("10 seconds", expired, showTimeElapsed: true);

        Assert.Equal("00:00:02", runningViewState.RemainingTime);
        Assert.Equal("00:00:10", expiredViewState.RemainingTime);
    }

    [Fact]
    public void TimerViewStateKeepsPausedProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState halfway = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(5)).State;
        CountdownState paused = CountdownTransitions.Pause(halfway, TimeSpan.FromSeconds(5)).State;

        TimerViewState viewState = TimerViewState.FromTimerState("10 seconds", paused);

        Assert.Equal(50, viewState.ProgressPercent);
    }

    [Fact]
    public void TimerViewStateClampsProgress()
    {
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(10),
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;
        CountdownState expired = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(60)).State;

        Assert.Equal(0, TimerViewState.GetProgressPercent(expired));
        Assert.Equal(100, TimerViewState.GetProgressPercent(expired, reverseProgressBar: true));
        Assert.Equal(0, TimerViewState.GetProgressPercent(CountdownState.Stopped));
        Assert.InRange(TimerViewState.GetProgressPercent(running), 0, 100);
    }

    [Fact]
    public void TimerViewStateProgressHandlesZeroDuration()
    {
        CountdownState zeroDuration = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.Zero,
            new DateTime(2026, 6, 8, 10, 0, 0),
            TimeSpan.Zero).State;

        double progress = TimerViewState.GetProgressPercent(zeroDuration);

        Assert.False(double.IsNaN(progress));
        Assert.False(double.IsInfinity(progress));
        Assert.Equal(0, progress);
    }

    [Theory]
    [InlineData(0, 400, 0)]
    [InlineData(25, 400, 100)]
    [InlineData(50, 400, 200)]
    [InlineData(100, 400, 400)]
    [InlineData(-25, 400, 0)]
    [InlineData(125, 400, 400)]
    public void ProgressWidthConverterMapsClampedProgressToAvailableWidth(
        double progressPercent,
        double availableWidth,
        double expectedWidth)
    {
        double width = ProgressWidthConverter.CalculateWidth(progressPercent, availableWidth);

        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void ProgressWidthConverterRejectsInvalidAvailableWidth(double availableWidth)
    {
        double width = ProgressWidthConverter.CalculateWidth(50, availableWidth);

        Assert.Equal(0, width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ProgressWidthConverterRejectsNonFiniteProgress(double progressPercent)
    {
        double width = ProgressWidthConverter.CalculateWidth(progressPercent, 400);

        Assert.Equal(0, width);
    }

    [Fact]
    public void ProgressWidthConverterHandlesUnavailableBindingValues()
    {
        var converter = new ProgressWidthConverter();

        Assert.Equal(0d, converter.Convert([], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0d, converter.Convert([50d, null], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0d, converter.Convert([50d, new object()], typeof(double), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProgressWidthConverterRecalculatesForResizedWidth()
    {
        const double progressPercent = 50;

        double initialWidth = ProgressWidthConverter.CalculateWidth(progressPercent, 250);
        double resizedWidth = ProgressWidthConverter.CalculateWidth(progressPercent, 500);

        Assert.Equal(125, initialWidth);
        Assert.Equal(250, resizedWidth);
        Assert.Equal(50, progressPercent);
    }

    [Fact]
    public void ProgressLayerMatchesLegacyFullWindowBackgroundTreatment()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml"));
        XElement rootGrid = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "RootGrid");
        XElement progressLayer = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "ProgressLayer");
        XElement innerGrid = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => element.Attribute(xaml + "Name")?.Value == "InnerGrid");
        XElement progressIndicator = Assert.Single(progressLayer.Elements(avalonia + "Border"));
        XElement fillBrush = Assert.Single(
            document.Descendants(avalonia + "SolidColorBrush"),
            element => element.Attribute(xaml + "Key")?.Value == "TimerProgressFillBrush");
        XElement widthBinding = Assert.Single(progressIndicator.Elements(avalonia + "Border.Width"));
        XElement multiBinding = Assert.Single(widthBinding.Elements(avalonia + "MultiBinding"));
        XElement[] bindings = multiBinding.Elements(avalonia + "Binding").ToArray();

        Assert.Equal("True", progressLayer.Attribute("ClipToBounds")?.Value);
        Assert.Equal("False", progressLayer.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("Left", progressIndicator.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("{DynamicResource TimerProgressFillBrush}", progressIndicator.Attribute("Background")?.Value);
        Assert.Equal("#3665B3", fillBrush.Attribute("Color")?.Value);
        Assert.Equal("0.45", fillBrush.Attribute("Opacity")?.Value);
        Assert.Contains(innerGrid, progressLayer.ElementsAfterSelf());
        Assert.Equal("{StaticResource ProgressWidthConverter}", multiBinding.Attribute("Converter")?.Value);
        Assert.Equal("ProgressPercent", bindings[0].Attribute("Path")?.Value);
        Assert.Equal("ProgressLayer", bindings[1].Attribute("ElementName")?.Value);
        Assert.Equal("Bounds.Width", bindings[1].Attribute("Path")?.Value);
    }

    private static XElement FindNamedElement(
        XDocument document,
        XName elementName,
        XNamespace xamlNamespace,
        string name)
    {
        return Assert.Single(
            document.Descendants(elementName),
            element => element.Attribute(xamlNamespace + "Name")?.Value == name);
    }

    private static XElement FindButtonByContent(XDocument document, string content)
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        return Assert.Single(
            document.Descendants(avalonia + "Button"),
            element => ResolveResourceReference(element.Attribute("Content")?.Value) == content);
    }

    private static void AssertAutomationName(XElement element, string expected)
    {
        Assert.Equal(expected, ResolveResourceReference(element.Attribute("AutomationProperties.Name")?.Value));
    }

    private static void AssertAutomationNameBindsToText(XElement element)
    {
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", element.Attribute("AutomationProperties.Name")?.Value);
    }

    private static void AssertAutomationHelpText(XElement element, string expected)
    {
        Assert.Equal(expected, ResolveResourceReference(element.Attribute("AutomationProperties.HelpText")?.Value));
    }

    private static void AssertAutomationLiveSetting(XElement element, string expected)
    {
        Assert.Equal(expected, element.Attribute("AutomationProperties.LiveSetting")?.Value);
    }

    private static void AssertStyleSetter(XElement style, string property, string value)
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XElement setter = Assert.Single(
            style.Elements(avalonia + "Setter"),
            element => element.Attribute("Property")?.Value == property);

        Assert.Equal(value, setter.Attribute("Value")?.Value);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            string path = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
    }

    private static string FindRepositoryDirectory(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            string path = Path.Combine(directory.FullName, relativePath);

            if (Directory.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository directory '{relativePath}'.");
    }

    private static string ResolveResourceReference(string? value)
    {
        const string prefix = "{x:Static local:ApplicationStrings.";
        const string suffix = "}";

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(suffix, StringComparison.Ordinal))
        {
            return value;
        }

        string propertyName = value[prefix.Length..^suffix.Length];
        object? resolved = typeof(ApplicationStrings)
            .GetProperty(propertyName)
            ?.GetValue(null);

        return Assert.IsType<string>(resolved);
    }

    private static bool IsAllowedNonResourceXamlValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("{Binding ", StringComparison.Ordinal)
            || value.StartsWith("{DynamicResource ", StringComparison.Ordinal)
            || !value.Any(char.IsLetter))
        {
            return true;
        }

        string[] allowed =
        [
            "/Assets/hourglass.png",
            "False",
            "True",
            "Auto",
            "Center",
            "CenterOwner",
            "CheckBox",
            "Radio",
            "Transparent",
            "Vertical",
            "Horizontal",
            "Wrap"
        ];

        return allowed.Contains(value, StringComparer.Ordinal);
    }

    private static string ExtractMethod(string source, string methodPrefix)
    {
        int start = source.IndexOf(methodPrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method prefix '{methodPrefix}'.");

        int bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodPrefix}'.");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            char character = source[index];
            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodPrefix}'.");
    }

    private static CountdownState CreateCountdownState(TimerState state)
    {
        DateTime start = new(2026, 6, 8, 10, 0, 0);
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(65),
            start,
            TimeSpan.Zero).State;

        return state switch
        {
            TimerState.Stopped => CountdownState.Stopped,
            TimerState.Running => running,
            TimerState.Paused => CountdownTransitions.Pause(running, TimeSpan.Zero).State,
            TimerState.Expired => CountdownTransitions.Tick(running, TimeSpan.FromSeconds(65)).State,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static void AssertCommandAvailability(
        MainWindowViewModel viewModel,
        bool canStart,
        bool canPauseResume,
        bool canStop,
        bool canRestart)
    {
        Assert.Equal(canStart, viewModel.StartCommand.CanExecute(null));
        Assert.Equal(canPauseResume, viewModel.PauseResumeCommand.CanExecute(null));
        Assert.Equal(canStop, viewModel.ResetCommand.CanExecute(null));
        Assert.Equal(canRestart, viewModel.RestartCommand.CanExecute(null));
    }

    private static MainWindowViewModel CreateViewModel(
        ManualMonotonicClock clock,
        Func<DateTime>? wallClockNow = null,
        INotificationService? notificationService = null,
        ISessionInhibitor? sessionInhibitor = null,
        ISettingsStore? settingsStore = null,
        IAppSettingsStore? appSettingsStore = null,
        ISavedTimersStore? savedTimersStore = null,
        IAudioAlertService? audioAlertService = null,
        ISystemPowerService? systemPowerService = null,
        bool statusIconSupported = false,
        bool statusIconCanRecoverHiddenWindow = false,
        IUiDispatcher? uiDispatcher = null)
    {
        ISettingsStore resolvedSettingsStore = settingsStore ?? new RecordingSettingsStore();

        return new MainWindowViewModel(
            new CountdownEngine(clock),
            wallClockNow ?? (() => new DateTime(2026, 6, 8, 10, 0, 0)),
            notificationService ?? new RecordingNotificationService(),
            sessionInhibitor ?? new RecordingSessionInhibitor(),
            resolvedSettingsStore,
            appSettingsStore ?? new DirectAppSettingsStore(resolvedSettingsStore),
            savedTimersStore ?? new DirectSavedTimersStore(resolvedSettingsStore),
            audioAlertService ?? new RecordingAudioAlertService(),
            systemPowerService ?? new RecordingSystemPowerService(),
            statusIconSupported,
            statusIconCanRecoverHiddenWindow,
            uiDispatcher: uiDispatcher);
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public bool IsDispatching { get; private set; }

        public bool CheckAccess()
        {
            return this.IsDispatching;
        }

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            this.PostCount++;
            bool wasDispatching = this.IsDispatching;
            this.IsDispatching = true;
            try
            {
                action();
            }
            finally
            {
                this.IsDispatching = wasDispatching;
            }
        }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Post(action);
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T result = default!;
            this.Post(() => result = action());
            return Task.FromResult(result);
        }
    }

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan elapsed)
        {
            this.Elapsed += elapsed;
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public int CallCount { get; private set; }

        public string? Title { get; private set; }

        public string? Body { get; private set; }

        public bool ThrowOnNotify { get; init; }

        public Task Completion { get; init; } = Task.CompletedTask;

        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.Title = title;
            this.Body = body;

            if (this.ThrowOnNotify)
            {
                return Task.FromException(new InvalidOperationException("Notification failed."));
            }

            return this.Completion;
        }
    }

    private sealed class RecordingAudioAlertService : IAudioAlertService
    {
        private readonly HashSet<string> availableSoundIds = new(StringComparer.Ordinal)
        {
            AudioAlertSoundIds.LoudBeep,
            AudioAlertSoundIds.NormalBeep,
            AudioAlertSoundIds.QuietBeep
        };

        public int CallCount { get; private set; }

        public int LoopingCallCount { get; private set; }

        public int StopCount { get; private set; }

        public string? SoundId { get; private set; }

        public bool ThrowOnPlay { get; init; }

        public bool HoldPlaybackUntilCanceled { get; init; }

        public bool IsSupported => true;

        public TaskCompletionSource PlaybackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PlaybackCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSoundAvailable(string soundId)
        {
            return this.availableSoundIds.Contains(soundId);
        }

        public async Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.SoundId = soundId;

            if (this.ThrowOnPlay)
            {
                throw new InvalidOperationException("Audio failed.");
            }

            if (this.HoldPlaybackUntilCanceled)
            {
                this.PlaybackStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    this.PlaybackCanceled.TrySetResult();
                    throw;
                }
            }

            return null;
        }

        public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default)
        {
            this.LoopingCallCount++;
            this.SoundId = soundId;

            if (this.ThrowOnPlay)
            {
                return Task.FromException<IAsyncDisposable?>(new InvalidOperationException("Audio failed."));
            }

            return Task.FromResult<IAsyncDisposable?>(new RecordingPlayback(this));
        }

        public void SetSoundAvailable(string soundId, bool available)
        {
            if (available)
            {
                this.availableSoundIds.Add(soundId);
            }
            else
            {
                this.availableSoundIds.Remove(soundId);
            }
        }

        private sealed class RecordingPlayback(RecordingAudioAlertService owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.StopCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingSessionInhibitor : ISessionInhibitor
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public bool ThrowOnAcquire { get; init; }

        public ValueTask<IAsyncDisposable?> InhibitAsync(
            string reason,
            bool inhibitSuspend,
            bool inhibitIdle,
            CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;

            if (this.ThrowOnAcquire)
            {
                throw new InvalidOperationException("Inhibition failed.");
            }

            Assert.Equal("Hourglass timer is running", reason);
            Assert.True(inhibitSuspend);
            Assert.True(inhibitIdle);

            return ValueTask.FromResult<IAsyncDisposable?>(new RecordingInhibitionLease(this));
        }

        private sealed class RecordingInhibitionLease(RecordingSessionInhibitor owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.ReleaseCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        public LinuxAppSettings? LoadedSettings { get; set; }

        public SavedTimersDocument? LoadedSavedTimers { get; init; }

        public CustomThemesDocument? LoadedCustomThemes { get; init; }

        public CustomThemesDocument? LatestCustomThemes { get; set; }

        public ActiveTimerSessionDocument? LoadedActiveSession { get; init; }

        public LinuxAppSettings? SavedSettings { get; private set; }

        public SavedTimersDocument? SavedTimers { get; private set; }

        public CustomThemesDocument? SavedCustomThemes { get; private set; }

        public ActiveTimerSessionDocument? SavedActiveSession { get; private set; }

        public bool ThrowOnLoad { get; init; }

        public bool ThrowOnSave { get; init; }

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (this.ThrowOnLoad)
            {
                return Task.FromException<T?>(new InvalidOperationException("Settings failed."));
            }

            object? value = key switch
            {
                "app" => this.SavedSettings ?? this.LoadedSettings,
                "saved-timers" => this.SavedTimers ?? this.LoadedSavedTimers,
                "custom-themes" => this.LatestCustomThemes ?? this.SavedCustomThemes ?? this.LoadedCustomThemes,
                "active-session" => this.LoadedActiveSession,
                _ => null
            };

            return Task.FromResult((T?)value);
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (this.ThrowOnSave)
            {
                return Task.FromException(new InvalidOperationException("Settings save failed."));
            }

            switch (key)
            {
                case "app":
                    this.SavedSettings = Assert.IsType<LinuxAppSettings>(value);
                    break;
                case "saved-timers":
                    this.SavedTimers = Assert.IsType<SavedTimersDocument>(value);
                    break;
                case "custom-themes":
                    this.SavedCustomThemes = Assert.IsType<CustomThemesDocument>(value);
                    break;
                case "active-session":
                    this.SavedActiveSession = Assert.IsType<ActiveTimerSessionDocument>(value);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected key: {key}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DeferredSettingsStore : ISettingsStore
    {
        private readonly TaskCompletionSource<LinuxAppSettings?> loadTask = new();

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (key != "app")
            {
                return Task.FromResult<T?>(default);
            }

            return (Task<T?>)(object)this.loadTask.Task;
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Complete(LinuxAppSettings settings)
        {
            this.loadTask.SetResult(settings);
        }
    }

    private sealed class QueuedTaskScheduler : TaskScheduler
    {
        private readonly Queue<Task> tasks = [];

        public int PendingCount => this.tasks.Count;

        protected override IEnumerable<Task>? GetScheduledTasks()
        {
            return this.tasks.ToArray();
        }

        protected override void QueueTask(Task task)
        {
            this.tasks.Enqueue(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        public void RunNext()
        {
            this.TryExecuteTask(this.tasks.Dequeue());
        }
    }

    private sealed class RecordingSystemPowerService : ISystemPowerService
    {
        public bool IsShutdownSupported { get; init; }

        public int ShutdownRequestCount { get; private set; }

        public Task RequestShutdownAsync(CancellationToken cancellationToken = default)
        {
            this.ShutdownRequestCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDesktopProgressService : IDesktopProgressService
    {
        public bool Supported { get; init; }

        public bool IsSupported => this.Supported;

        public int SetCount { get; private set; }

        public int ClearCount { get; private set; }

        public double Fraction { get; private set; }

        public DesktopProgressState State { get; private set; }

        public bool ThrowOnSet { get; init; }

        public bool ThrowOnClear { get; init; }

        public int SetFailuresRemaining { get; set; }

        public int ClearFailuresRemaining { get; set; }

        public TaskCompletionSource? SetCompletion { get; set; }

        private readonly List<string> operations = [];

        public IReadOnlyList<string> Operations => this.operations;

        public Task SetProgressAsync(
            double fraction,
            DesktopProgressState state,
            CancellationToken cancellationToken = default)
        {
            this.SetCount++;
            this.Fraction = fraction;
            this.State = state;
            this.operations.Add(FormattableString.Invariant($"set:{state}:{fraction}"));

            if (this.ThrowOnSet || this.SetFailuresRemaining > 0)
            {
                this.SetFailuresRemaining = Math.Max(0, this.SetFailuresRemaining - 1);
                return Task.FromException(new InvalidOperationException("Desktop progress failed."));
            }

            return this.SetCompletion?.Task ?? Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            this.ClearCount++;
            this.operations.Add("clear");

            if (this.ThrowOnClear || this.ClearFailuresRemaining > 0)
            {
                this.ClearFailuresRemaining = Math.Max(0, this.ClearFailuresRemaining - 1);
                return Task.FromException(new InvalidOperationException("Desktop progress clear failed."));
            }

            return Task.CompletedTask;
        }
    }
}
