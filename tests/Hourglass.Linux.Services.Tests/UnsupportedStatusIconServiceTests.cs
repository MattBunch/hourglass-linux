namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Hourglass.Platform;
using Xunit;

public sealed class UnsupportedStatusIconServiceTests
{
    [Fact]
    public async Task UnsupportedStatusIconReportsUnavailableAndAcceptsUpdates()
    {
        UnsupportedStatusIconService service = UnsupportedStatusIconService.Instance;
        int actionCount = 0;
        service.ActionRequested += (_, _) => actionCount++;

        await service.UpdateAsync(new StatusIconMenuState(
            "Hourglass",
            IsVisible: true,
            "Pause",
            CanPauseResume: true,
            CanStop: true,
            CanRestart: true,
            CanHideWindow: true,
            CanExit: true));
        await service.DisposeAsync();

        Assert.False(service.IsSupported);
        Assert.False(service.CanRecoverHiddenWindow);
        Assert.Equal(0, actionCount);
    }
}
