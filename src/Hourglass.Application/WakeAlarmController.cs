using Hourglass.Platform;
using Hourglass.Timing;

namespace Hourglass.Application;

public sealed class WakeAlarmController(
    IWakeAlarmService wakeAlarmService,
    Func<DateTimeOffset> wallClockNow,
    IDiagnosticSink? diagnosticSink = null,
    string inhibitionReason = "Timer running") : IAsyncDisposable
{
    private static readonly TimeSpan WakeLeadTime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumFutureWakeDelay = TimeSpan.FromSeconds(15);

    private readonly IDiagnosticSink diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    private readonly IWakeAlarmService wakeAlarmService = wakeAlarmService ?? throw new ArgumentNullException(nameof(wakeAlarmService));
    private readonly Func<DateTimeOffset> wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object lifetimeGate = new();
    private readonly TaskCompletionSource callersCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? disposal;
    private int activeCalls;
    private bool stopping;
    private long generation;
    private IWakeAlarmLease? lease;
    private DateTimeOffset? scheduledWakeAt;

    public Task ApplyAsync(IEnumerable<WakeAlarmTimerSnapshot> timers, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timers);
        WakeAlarmTimerSnapshot[] snapshot = timers.ToArray();
        lock (this.lifetimeGate)
        {
            if (this.stopping) { return Task.CompletedTask; }
            this.activeCalls++;
        }
        long expectedGeneration = Interlocked.Increment(ref this.generation);
        return ApplyCoreAsync();

        async Task ApplyCoreAsync()
        {
            try
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetimeCancellation.Token);
                try { await this.ScheduleAsync(snapshot, enabled, expectedGeneration, linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (this.lifetimeCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
            }
            finally
            {
                lock (this.lifetimeGate)
                {
                    if (--this.activeCalls == 0 && this.stopping) { this.callersCompleted.TrySetResult(); }
                }
            }
        }
    }

    private async Task ScheduleAsync(WakeAlarmTimerSnapshot[] snapshot, bool enabled, long expectedGeneration, CancellationToken cancellationToken)
    {
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
                    new WakeAlarmRequest(nextWakeAt.Value, inhibitionReason),
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

            if (expectedGeneration != Interlocked.Read(ref this.generation) || cancellationToken.IsCancellationRequested)
            {
                if (result.Lease != null) { await result.Lease.DisposeAsync().ConfigureAwait(false); }
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

    internal void Invalidate() => Interlocked.Increment(ref this.generation);

    public ValueTask DisposeAsync()
    {
        lock (this.lifetimeGate)
        {
            if (this.disposal == null)
            {
                this.stopping = true;
                this.Invalidate();
                if (this.activeCalls == 0) { this.callersCompleted.TrySetResult(); }
                this.disposal = this.DisposeCoreAsync();
            }
            return new(this.disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await this.lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        await this.callersCompleted.Task.ConfigureAwait(false);
        try { await this.ReleaseAsync().ConfigureAwait(false); }
        finally { this.gate.Dispose(); this.lifetimeCancellation.Dispose(); }
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
        this.diagnosticSink.TryRecord(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.UserRequested,
            "wake-alarm",
            operation,
            this.wakeAlarmService.GetType().Name,
            message,
            exception));
    }
}

public sealed record WakeAlarmTimerSnapshot(TimerState State, DateTime? EndTime);
