namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class UnsupportedStatusIconService : IStatusIconService
{
    public static UnsupportedStatusIconService Instance { get; } = new();

    public bool IsSupported => false;

    public event EventHandler<StatusIconActionRequestedEventArgs>? ActionRequested
    {
        add { }
        remove { }
    }

    public Task UpdateAsync(StatusIconMenuState state, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
