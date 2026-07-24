namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Hourglass.Platform;
using Xunit;

public sealed class UnsupportedDesktopProgressServiceTests
{
    [Fact]
    public async Task UnsupportedBackendReportsUnavailableAndAcceptsProgressCalls()
    {
        UnsupportedDesktopProgressService service = UnsupportedDesktopProgressService.Instance;

        await service.SetProgressAsync(0.5, DesktopProgressState.Normal);
        await service.ClearAsync();

        Assert.False(service.IsSupported);
    }
}
