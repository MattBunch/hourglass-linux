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
        Assert.Equal("Click to enter title", titleInput.Attribute("PlaceholderText")?.Value);
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

        viewModel.TimerInput = "still invalid";
        Assert.False(viewModel.HasValidationError);

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
            .ToDictionary(element => element.Attribute("Header")!.Value, StringComparer.Ordinal);

        Assert.Equal("{Binding AlwaysOnTop}", window.Attribute("Topmost")?.Value);
        Assert.Equal("Transparent", rootGrid.Attribute("Background")?.Value);
        Assert.Equal("{Binding StartCommand}", menuItems["Start"].Attribute("Command")?.Value);
        Assert.Equal("{Binding PauseResumeCommand}", menuItems["{Binding PauseResumeText}"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ResetCommand}", menuItems["Stop"].Attribute("Command")?.Value);
        Assert.Equal("{Binding RestartCommand}", menuItems["Restart"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Notifications"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding NotificationsEnabled, Mode=OneWay}", menuItems["Notifications"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleNotificationsCommand}", menuItems["Notifications"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Sound"].Attribute("ToggleType")?.Value);
        Assert.Equal("{Binding AudioAlertsEnabled, Mode=OneWay}", menuItems["Sound"].Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ToggleAudioAlertsCommand}", menuItems["Sound"].Attribute("Command")?.Value);
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
        Dictionary<string, XElement> advancedItems = menuItems["Advanced options"]
            .Elements(avalonia + "MenuItem")
            .Where(element => element.Attribute("Header") != null)
            .ToDictionary(element => element.Attribute("Header")!.Value, StringComparer.Ordinal);
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
        Assert.Equal(
            "{Binding CanHideToNotificationArea}",
            menuItems["Hide to notification area"].Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding HideToNotificationAreaCommand}",
            menuItems["Hide to notification area"].Attribute("Command")?.Value);
        Assert.Equal("CheckBox", menuItems["Full screen"].Attribute("ToggleType")?.Value);
        Assert.Equal("FullScreenMenuItemClick", menuItems["Full screen"].Attribute("Click")?.Value);
        Assert.Equal("ExitMenuItemClick", menuItems["Exit"].Attribute("Click")?.Value);
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
            .ToDictionary(element => element.Attribute("Content")?.Value ?? string.Empty, StringComparer.Ordinal);

        Assert.Equal("CenterOwner", window.Attribute("WindowStartupLocation")?.Value);
        Assert.Equal("True", buttons["Cancel"].Attribute("IsCancel")?.Value);
        Assert.Equal("True", buttons["Exit"].Attribute("IsDefault")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value == "A timer is still running. Exit Hourglass?");
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

        Assert.Contains("this.Close();", exitHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingSettingsSave", exitHandler, StringComparison.Ordinal);
        Assert.Contains("this.windowAttentionController?.RequestAttention();", statusIconExit, StringComparison.Ordinal);
        Assert.Contains("this.Close();", statusIconExit, StringComparison.Ordinal);
        Assert.True(
            statusIconExit.IndexOf("this.windowAttentionController?.RequestAttention();", StringComparison.Ordinal)
            < statusIconExit.IndexOf("this.Close();", StringComparison.Ordinal));
        Assert.Contains("this.Closing += this.WindowClosing;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = this.closeCoordinator.RequestClose();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.closeCoordinator.CompleteClose();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.PrepareCloseAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LinuxDesktopProgressServiceFactory.CreateDefault()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("this.viewModel.PendingSettingsSave", prepareClose, StringComparison.Ordinal);
        Assert.Contains("this.desktopProgressController.ClearAsync()", prepareClose, StringComparison.Ordinal);
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
        int applyStatusIconStart = codeBehind.IndexOf("private void ApplyStatusIconState()", StringComparison.Ordinal);
        int applyStatusIconEnd = codeBehind.IndexOf("private void HideToNotificationAreaRequested", applyStatusIconStart, StringComparison.Ordinal);
        string applyStatusIcon = codeBehind[applyStatusIconStart..applyStatusIconEnd];

        Assert.Contains("avares://hourglass-linux/Assets/hourglass.png", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AssetLoader.Open(StatusIconResourceUri)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new AvaloniaStatusIconService(new WindowIcon(iconStream))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("new AvaloniaStatusIconService(Path.Combine", codeBehind, StringComparison.Ordinal);
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
        Assert.NotNull(settingsStore.SavedSettings);
        Assert.False(settingsStore.SavedSettings.AudioAlertsEnabled);
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
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal("5 minutes", viewModel.TimerInput);

        scheduler.RunNext();
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
        Assert.Equal("{StaticResource TimerProgressFillBrush}", progressIndicator.Attribute("Background")?.Value);
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
        IAudioAlertService? audioAlertService = null,
        ISystemPowerService? systemPowerService = null,
        bool statusIconSupported = false,
        bool statusIconCanRecoverHiddenWindow = false)
    {
        return new MainWindowViewModel(
            new CountdownEngine(clock),
            wallClockNow ?? (() => new DateTime(2026, 6, 8, 10, 0, 0)),
            notificationService ?? new RecordingNotificationService(),
            sessionInhibitor ?? new RecordingSessionInhibitor(),
            settingsStore ?? new RecordingSettingsStore(),
            audioAlertService ?? new RecordingAudioAlertService(),
            systemPowerService ?? new RecordingSystemPowerService(),
            statusIconSupported,
            statusIconCanRecoverHiddenWindow);
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

        public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.Title = title;
            this.Body = body;

            if (this.ThrowOnNotify)
            {
                return Task.FromException(new InvalidOperationException("Notification failed."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAudioAlertService : IAudioAlertService
    {
        public int CallCount { get; private set; }

        public int LoopingCallCount { get; private set; }

        public int StopCount { get; private set; }

        public string? SoundId { get; private set; }

        public bool ThrowOnPlay { get; init; }

        public Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            this.SoundId = soundId;

            if (this.ThrowOnPlay)
            {
                return Task.FromException<IAsyncDisposable?>(new InvalidOperationException("Audio failed."));
            }

            return Task.FromResult<IAsyncDisposable?>(null);
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
        public LinuxAppSettings? LoadedSettings { get; init; }

        public LinuxAppSettings? SavedSettings { get; private set; }

        public bool ThrowOnLoad { get; init; }

        public bool ThrowOnSave { get; init; }

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (this.ThrowOnLoad)
            {
                return Task.FromException<T?>(new InvalidOperationException("Settings failed."));
            }

            return Task.FromResult((T?)(object?)this.LoadedSettings);
        }

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            Assert.Equal("app", key);

            if (this.ThrowOnSave)
            {
                return Task.FromException(new InvalidOperationException("Settings save failed."));
            }

            this.SavedSettings = Assert.IsType<LinuxAppSettings>(value);
            return Task.CompletedTask;
        }
    }

    private sealed class DeferredSettingsStore : ISettingsStore
    {
        private readonly TaskCompletionSource<LinuxAppSettings?> loadTask = new();

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            Assert.Equal("app", key);
            Assert.Equal(typeof(LinuxAppSettings), typeof(T));

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
