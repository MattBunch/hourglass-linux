using Hourglass.Platform;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

internal sealed class WakeAlarmController(
    IWakeAlarmService wakeAlarmService,
    Func<DateTimeOffset> wallClockNow,
    IDiagnosticSink? diagnosticSink = null) : IAsyncDisposable
{
    private const string WakeAlarmReason = "Hourglass timer is running";
    private static readonly TimeSpan WakeLeadTime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumFutureWakeDelay = TimeSpan.FromSeconds(15);

    private readonly IDiagnosticSink diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    private readonly IWakeAlarmService wakeAlarmService = wakeAlarmService ?? throw new ArgumentNullException(nameof(wakeAlarmService));
    private readonly Func<DateTimeOffset> wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
    private readonly SemaphoreSlim gate = new(1, 1);
    private IWakeAlarmLease? lease;
    private DateTimeOffset? scheduledWakeAt;

    public async Task ApplyAsync(
        IEnumerable<WakeAlarmTimerSnapshot> timers,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timers);

        WakeAlarmTimerSnapshot[] snapshot = timers.ToArray();
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset? nextWakeAt = enabled
                ? this.GetNextWakeAt(snapshot)
                : null;
            if (nextWakeAt == this.scheduledWakeAt)
            {
                return;
            }

            await this.ReleaseAsync().ConfigureAwait(false);
            if (nextWakeAt == null)
            {
                return;
            }

            WakeAlarmScheduleResult result;
            try
            {
                this.diagnosticSink.ResetDuplicateSuppression("wake-alarm", "schedule", this.wakeAlarmService.GetType().Name);
                result = await this.wakeAlarmService.TryScheduleWakeAsync(
                    new WakeAlarmRequest(nextWakeAt.Value, WakeAlarmReason),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                this.RecordFailure("schedule", "Wake alarm scheduling failed.", exception);
                return;
            }

            if (!result.Scheduled)
            {
                this.RecordFailure("schedule", result.Message, null);
                return;
            }

            if (result.Lease != null)
            {
                this.lease = result.Lease;
                this.scheduledWakeAt = nextWakeAt;
            }
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.ReleaseAsync().ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
            this.gate.Dispose();
        }
    }

    private DateTimeOffset? GetNextWakeAt(IEnumerable<WakeAlarmTimerSnapshot> timers)
    {
        DateTimeOffset now = this.wallClockNow();
        DateTimeOffset minimumWakeAt = now.Add(MinimumFutureWakeDelay);
        DateTimeOffset? nextTimerExpiry = null;
        foreach (WakeAlarmTimerSnapshot timer in timers)
        {
            if (timer.State != TimerState.Running || !timer.EndTime.HasValue)
            {
                continue;
            }

            DateTimeOffset expiry = new(DateTime.SpecifyKind(timer.EndTime.Value, DateTimeKind.Local));
            if (expiry <= minimumWakeAt || (nextTimerExpiry.HasValue && expiry >= nextTimerExpiry.Value))
            {
                continue;
            }

            nextTimerExpiry = expiry;
        }

        if (!nextTimerExpiry.HasValue)
        {
            return null;
        }

        DateTimeOffset targetWakeAt = nextTimerExpiry.Value.Subtract(WakeLeadTime);
        return targetWakeAt < minimumWakeAt ? minimumWakeAt : targetWakeAt;
    }

    private async Task ReleaseAsync()
    {
        IWakeAlarmLease? currentLease = this.lease;
        this.lease = null;
        this.scheduledWakeAt = null;
        if (currentLease == null)
        {
            return;
        }

        try
        {
            await currentLease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.RecordFailure("release", "Wake alarm release failed.", exception);
        }
    }

    private void RecordFailure(string operation, string message, Exception? exception)
    {
        this.diagnosticSink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.UserRequested,
            "wake-alarm",
            operation,
            this.wakeAlarmService.GetType().Name,
            message,
            exception));
    }
}

internal sealed record WakeAlarmTimerSnapshot(TimerState State, DateTime? EndTime);
