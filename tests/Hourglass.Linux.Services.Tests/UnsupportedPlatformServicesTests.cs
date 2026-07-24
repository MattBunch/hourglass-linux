namespace Hourglass.Linux.Services.Tests;

using Hourglass.Platform;
using Xunit;

public sealed class UnsupportedPlatformServicesTests
{
    [Fact]
    public async Task UnsupportedNotificationServiceAcceptsExpiryNotifications()
    {
        await UnsupportedNotificationService.Instance.ShowTimerExpiredAsync("Timer", "Done");
    }

    [Fact]
    public async Task UnsupportedExternalUriLauncherRejectsLaunchesWithoutThrowing()
    {
        bool opened = await UnsupportedExternalUriLauncher.Instance.OpenAsync(new Uri("https://example.test"));

        Assert.False(opened);
    }

    [Fact]
    public async Task UnsupportedSessionInhibitorReturnsNoLease()
    {
        IAsyncDisposable? lease = await UnsupportedSessionInhibitor.Instance.InhibitAsync(
            "Timer running",
            inhibitSuspend: true,
            inhibitIdle: true);

        Assert.Null(lease);
    }

    [Fact]
    public async Task UnsupportedAudioAlertServiceReportsUnavailableAndReturnsNoPlayback()
    {
        UnsupportedAudioAlertService service = UnsupportedAudioAlertService.Instance;

        IAsyncDisposable? playback = await service.PlayAlertAsync(AudioAlertSoundIds.NormalBeep);
        IAsyncDisposable? loopingPlayback = await service.PlayAlertLoopingAsync(AudioAlertSoundIds.NormalBeep);

        Assert.False(service.IsSupported);
        Assert.False(service.IsSoundAvailable(AudioAlertSoundIds.NormalBeep));
        Assert.Null(playback);
        Assert.Null(loopingPlayback);
    }

    [Fact]
    public async Task UnsupportedWakeAlarmServiceReportsUnavailableAndReturnsNoLease()
    {
        WakeAlarmScheduleResult result = await UnsupportedWakeAlarmService.Instance.TryScheduleWakeAsync(
            new WakeAlarmRequest(DateTimeOffset.Now.AddMinutes(5), "Timer running"));

        Assert.False(result.Supported);
        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedSystemPowerServiceReportsShutdownUnavailableAndAcceptsRequests()
    {
        UnsupportedSystemPowerService service = UnsupportedSystemPowerService.Instance;

        await service.RequestShutdownAsync();

        Assert.False(service.IsShutdownSupported);
    }
}
