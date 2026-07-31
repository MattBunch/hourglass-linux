using Hourglass.Platform;

namespace Hourglass.DemoRecorder.Services;

public sealed class DemoNotificationService : INotificationService
{
    public int NotificationCount { get; private set; }

    public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        cancellationToken.ThrowIfCancellationRequested();
        this.NotificationCount++;
        return Task.CompletedTask;
    }
}
