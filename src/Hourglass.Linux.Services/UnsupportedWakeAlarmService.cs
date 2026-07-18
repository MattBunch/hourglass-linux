namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class UnsupportedWakeAlarmService : IWakeAlarmService
{
    public Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
        WakeAlarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new WakeAlarmScheduleResult(
            Supported: false,
            Scheduled: false,
            Message: "Wake alarms are not supported in this environment.",
            Lease: null));
    }
}
