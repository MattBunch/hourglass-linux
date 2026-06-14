namespace Hourglass.Core.Tests.Settings;

using Hourglass.Settings;
using Xunit;

public sealed class LinuxAppSettingsTests
{
    [Fact]
    public void DefaultSettingsUseDefaultTimerInputAndNotifications()
    {
        LinuxAppSettings settings = LinuxAppSettings.Default;

        Assert.Equal("5 minutes", settings.GetInitialTimerInput("5 minutes"));
        Assert.True(settings.NotificationsEnabled);
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
    }

    [Fact]
    public void ConstructorNormalizesRecentTimerInputs()
    {
        var settings = new LinuxAppSettings(["", "  15 minutes ", "15 minutes", "  "], notificationsEnabled: false);

        Assert.Equal(["15 minutes"], settings.RecentTimerInputs);
        Assert.False(settings.NotificationsEnabled);
    }
}
