namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class UnsupportedSystemPowerService : ISystemPowerService
{
    public bool IsShutdownSupported => false;

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
