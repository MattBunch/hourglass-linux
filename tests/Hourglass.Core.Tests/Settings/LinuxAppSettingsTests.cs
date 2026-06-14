namespace Hourglass.Core.Tests.Settings;

using Hourglass.Settings;
using System.Text.Json;
using Xunit;

public sealed class LinuxAppSettingsTests
{
    [Fact]
    public void DefaultSettingsUseDefaultTimerInputNotificationsAndAudioAlerts()
    {
        LinuxAppSettings settings = LinuxAppSettings.Default;

        Assert.Equal("5 minutes", settings.GetInitialTimerInput("5 minutes"));
        Assert.True(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
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
    }

    [Fact]
    public void ConstructorNormalizesRecentTimerInputs()
    {
        var settings = new LinuxAppSettings(
            ["", "  15 minutes ", "15 minutes", "  "],
            notificationsEnabled: false,
            audioAlertsEnabled: false);

        Assert.Equal(["15 minutes"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.False(settings.AudioAlertsEnabled);
    }

    [Fact]
    public void AddRecentTimerInputPreservesDisabledAudioAlerts()
    {
        var settings = new LinuxAppSettings(["1 second"], notificationsEnabled: false, audioAlertsEnabled: false);

        LinuxAppSettings updated = settings.AddRecentTimerInput("2 seconds");

        Assert.Equal(["2 seconds", "1 second"], updated.RecentTimerInputs);
        Assert.False(updated.NotificationsEnabled);
        Assert.False(updated.AudioAlertsEnabled);
    }

    [Fact]
    public void OldJsonWithoutAudioAlertsEnabledDeserializesWithAudioEnabled()
    {
        const string json = """{"RecentTimerInputs":["10 seconds"],"NotificationsEnabled":false}""";

        LinuxAppSettings? settings = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(["10 seconds"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
        Assert.True(settings.AudioAlertsEnabled);
    }

    [Fact]
    public void SerializationRoundTripsAudioAlertPreference()
    {
        var settings = new LinuxAppSettings(["10 seconds"], notificationsEnabled: true, audioAlertsEnabled: false);

        string json = JsonSerializer.Serialize(settings);
        LinuxAppSettings? roundTripped = JsonSerializer.Deserialize<LinuxAppSettings>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(["10 seconds"], roundTripped.RecentTimerInputs);
        Assert.True(roundTripped.NotificationsEnabled);
        Assert.False(roundTripped.AudioAlertsEnabled);
    }
}
