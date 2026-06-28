namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class UnsupportedDesktopProgressService : IDesktopProgressService
{
    public bool IsSupported => false;

    public Task SetProgressAsync(
        double fraction,
        DesktopProgressState state,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
