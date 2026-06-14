namespace Hourglass.Linux.Services.Tests;

using System.ComponentModel;
using System.Diagnostics;
using Xunit;

public sealed class NotifySendNotificationServiceTests
{
    [Fact]
    public async Task ShowTimerExpiredAsyncPassesNotifySendArguments()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var service = new NotifySendNotificationService((startInfo, _) =>
        {
            capturedStartInfo = startInfo;
            return Task.FromResult(0);
        });

        await service.ShowTimerExpiredAsync("Hourglass", "Timer complete");

        Assert.NotNull(capturedStartInfo);
        Assert.Equal("notify-send", capturedStartInfo.FileName);
        Assert.False(capturedStartInfo.UseShellExecute);
        Assert.Equal(
            ["--app-name=Hourglass", "Hourglass", "Timer complete"],
            capturedStartInfo.ArgumentList);
    }

    [Fact]
    public async Task MissingNotifySendDoesNotThrow()
    {
        var service = new NotifySendNotificationService((_, _) => throw new Win32Exception());

        await service.ShowTimerExpiredAsync("Hourglass", "Timer complete");
    }

    [Fact]
    public async Task FailedNotifySendExitDoesNotThrow()
    {
        var service = new NotifySendNotificationService((_, _) => Task.FromResult(1));

        await service.ShowTimerExpiredAsync("Hourglass", "Timer complete");
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var service = new NotifySendNotificationService(
            (_, cancellationToken) => throw new OperationCanceledException(cancellationToken));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ShowTimerExpiredAsync(
                "Hourglass",
                "Timer complete",
                cancellationTokenSource.Token));
    }
}
