namespace Hourglass.Core.Tests.Settings;

using Hourglass.Settings;
using System.Text.Json;
using Xunit;

public sealed class LinuxAppSettingsTests
{
    [Fact]
    public void DefaultSettingsUseDefaultTimerInputAndAlertOptions()
    {
        LinuxAppSettings settings = LinuxAppSettings.Default;

        Assert.Equal("5 minutes", settings.GetInitialTimerInput("5 minutes"));
        Assert.True(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.PopUpWhenExpired);
        Assert.True(settings.PromptOnExit);
        Assert.False(settings.ReverseProgressBar);
        Assert.False(settings.ShowTimeElapsed);
        Assert.False(settings.LoopTimer);
        Assert.False(settings.LoopSound);
        Assert.False(settings.CloseWhenExpired);
        Assert.False(settings.LockInterface);
        Assert.False(settings.DoNotKeepComputerAwake);
        Assert.False(settings.ShutDownWhenExpired);
        Assert.Empty(settings.RecentTimerInputs);
    }

    [Fact]
    public void AddRecentTimerInputReturnsNewSettingsWithTrimmedMostRecentInput()
    {
        LinuxAppSettings original = LinuxAppSettings.Default;

        LinuxAppSettings updated = original
            .AddRecentTimerInput(" 10 seconds ")
            .AddRecentTimerInput("5 minutes")
            .AddRecentTimerInput("10 seconds");

        Assert.Empty(original.RecentTimerInputs);
        Assert.Equal(["10 seconds", "5 minutes"], updated.RecentTimerInputs);
        Assert.Equal("10 seconds", updated.GetInitialTimerInput("default"));
        Assert.True(updated.NotificationsEnabled);
        Assert.True(updated.AudioAlertsEnabled);
        Assert.False(updated.AlwaysOnTop);
        Assert.True(updated.PopUpWhenExpired);
        Assert.True(updated.PromptOnExit);
        Assert.False(updated.ReverseProgressBar);
        Assert.False(updated.ShowTimeElapsed);
        Assert.False(updated.LoopTimer);
        Assert.False(updated.LoopSound);
        Assert.False(updated.CloseWhenExpired);
        Assert.False(updated.LockInterface);
        Assert.False(updated.DoNotKeepComputerAwake);
        Assert.False(updated.ShutDownWhenExpired);
    }

    [Fact]
    public void ConstructorNormalizesRecentTimerInputs()
    {
        var settings = new LinuxAppSettings(
            ["", "  15 minutes ", "15 minutes", "  "],
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
            shutDownWhenExpired: true);

        Assert.Equal(["15 minutes"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.False(settings.AudioAlertsEnabled);
        Assert.True(settings.AlwaysOnTop);
        Assert.False(settings.PopUpWhenExpired);
        Assert.False(settings.PromptOnExit);
        Assert.True(settings.ReverseProgressBar);
        Assert.True(settings.ShowTimeElapsed);
        Assert.True(settings.LoopTimer);
        Assert.True(settings.LoopSound);
        Assert.True(settings.CloseWhenExpired);
        Assert.True(settings.LockInterface);
        Assert.True(settings.DoNotKeepComputerAwake);
        Assert.True(settings.ShutDownWhenExpired);
    }

    [Fact]
    public void AddRecentTimerInputPreservesOptions()
    {
        var settings = new LinuxAppSettings(
            ["1 second"],
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
            shutDownWhenExpired: true);

        LinuxAppSettings updated = settings.AddRecentTimerInput("2 seconds");

        Assert.Equal(["2 seconds", "1 second"], updated.RecentTimerInputs);
        Assert.False(updated.NotificationsEnabled);
        Assert.False(updated.AudioAlertsEnabled);
        Assert.True(updated.AlwaysOnTop);
        Assert.False(updated.PopUpWhenExpired);
        Assert.False(updated.PromptOnExit);
        Assert.True(updated.ReverseProgressBar);
        Assert.True(updated.ShowTimeElapsed);
        Assert.True(updated.LoopTimer);
        Assert.True(updated.LoopSound);
        Assert.True(updated.CloseWhenExpired);
        Assert.True(updated.LockInterface);
        Assert.True(updated.DoNotKeepComputerAwake);
        Assert.True(updated.ShutDownWhenExpired);
    }

    [Fact]
    public void OlderJsonWithoutNewOptionsUsesDefaults()
    {
        const string json = """{"RecentTimerInputs":["10 seconds"],"NotificationsEnabled":false}""";

        LinuxAppSettings? settings = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(["10 seconds"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.PopUpWhenExpired);
        Assert.True(settings.PromptOnExit);
        Assert.False(settings.ReverseProgressBar);
        Assert.False(settings.ShowTimeElapsed);
        Assert.False(settings.LoopTimer);
        Assert.False(settings.LoopSound);
        Assert.False(settings.CloseWhenExpired);
        Assert.False(settings.LockInterface);
        Assert.False(settings.DoNotKeepComputerAwake);
        Assert.False(settings.ShutDownWhenExpired);
    }

    [Fact]
    public void SerializationRoundTripsAllPreferences()
    {
        var settings = new LinuxAppSettings(
            ["10 seconds"],
            notificationsEnabled: true,
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
            shutDownWhenExpired: true);

        string json = JsonSerializer.Serialize(settings);
        LinuxAppSettings? roundTripped = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(["10 seconds"], roundTripped.RecentTimerInputs);
        Assert.True(roundTripped.NotificationsEnabled);
        Assert.False(roundTripped.AudioAlertsEnabled);
        Assert.True(roundTripped.AlwaysOnTop);
        Assert.False(roundTripped.PopUpWhenExpired);
        Assert.False(roundTripped.PromptOnExit);
        Assert.True(roundTripped.ReverseProgressBar);
        Assert.True(roundTripped.ShowTimeElapsed);
        Assert.True(roundTripped.LoopTimer);
        Assert.True(roundTripped.LoopSound);
        Assert.True(roundTripped.CloseWhenExpired);
        Assert.True(roundTripped.LockInterface);
        Assert.True(roundTripped.DoNotKeepComputerAwake);
        Assert.True(roundTripped.ShutDownWhenExpired);
    }
}
