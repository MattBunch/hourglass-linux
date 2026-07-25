namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Platform;
using Hourglass.Timing;
using Xunit;

public sealed class WakeAlarmControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsyncSchedulesEarliestRunningTimerWithLeadTime()
    {
        var service = new RecordingWakeAlarmService();
        await using var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync(
            [
                RunningTimer(Now.AddMinutes(10)),
                RunningTimer(Now.AddMinutes(5)),
                new WakeAlarmTimerSnapshot(TimerState.Paused, Now.AddMinutes(1).LocalDateTime)
            ],
            enabled: true);

        WakeAlarmRequest request = Assert.Single(service.Requests);
        Assert.Equal(Now.AddMinutes(5).AddSeconds(-15), request.WakeAt);
    }

    [Fact]
    public async Task ApplyAsyncReplacesLeaseWhenEarliestTimerChanges()
    {
        var service = new RecordingWakeAlarmService();
        await using var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);
        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(6))], enabled: true);

        Assert.Equal(2, service.Requests.Count);
        Assert.Equal(1, service.Leases[0].DisposeCount);
        Assert.Equal(0, service.Leases[1].DisposeCount);
    }

    [Fact]
    public async Task ApplyAsyncCancelsLeaseWhenWakeAlarmsAreDisabled()
    {
        var service = new RecordingWakeAlarmService();
        await using var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);
        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: false);

        Assert.Single(service.Requests);
        Assert.Equal(1, service.Leases[0].DisposeCount);
    }

    [Fact]
    public async Task ApplyAsyncCancelsLeaseWhenNoRunningTimersRemain()
    {
        var service = new RecordingWakeAlarmService();
        await using var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);
        await controller.ApplyAsync([new WakeAlarmTimerSnapshot(TimerState.Paused, Now.AddMinutes(10).LocalDateTime)], enabled: true);

        Assert.Single(service.Requests);
        Assert.Equal(1, service.Leases[0].DisposeCount);
    }

    [Fact]
    public async Task ApplyAsyncDoesNotScheduleWhenMinimumDelayWouldMissExpiry()
    {
        var service = new RecordingWakeAlarmService();
        await using var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync([RunningTimer(Now.AddSeconds(15))], enabled: true);

        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task DisposeAsyncReleasesActiveLease()
    {
        var service = new RecordingWakeAlarmService();
        var controller = new WakeAlarmController(service, () => Now);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);
        await controller.DisposeAsync();

        Assert.Equal(1, service.Leases[0].DisposeCount);
    }

    [Fact]
    public async Task ScheduleFailureRecordsDiagnosticAndDoesNotThrow()
    {
        var service = new ThrowingWakeAlarmService();
        var diagnostics = new RecordingDiagnosticSink();
        await using var controller = new WakeAlarmController(service, () => Now, diagnostics);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);

        DiagnosticEvent diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticFailureClass.UserRequested, diagnostic.FailureClass);
        Assert.Equal("wake-alarm", diagnostic.Category);
        Assert.Equal("schedule", diagnostic.Operation);
    }

    [Fact]
    public async Task UnsuccessfulScheduleResultRecordsDiagnosticAndDoesNotThrow()
    {
        var service = new UnsuccessfulWakeAlarmService();
        var diagnostics = new RecordingDiagnosticSink();
        await using var controller = new WakeAlarmController(service, () => Now, diagnostics);

        await controller.ApplyAsync([RunningTimer(Now.AddMinutes(10))], enabled: true);

        DiagnosticEvent diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticFailureClass.UserRequested, diagnostic.FailureClass);
        Assert.Equal("wake-alarm", diagnostic.Category);
        Assert.Equal("schedule", diagnostic.Operation);
        Assert.Equal("Wake alarm unavailable.", diagnostic.Message);
    }

    private static WakeAlarmTimerSnapshot RunningTimer(DateTimeOffset endTime)
    {
        return new WakeAlarmTimerSnapshot(TimerState.Running, endTime.LocalDateTime);
    }

    private sealed class RecordingWakeAlarmService : IWakeAlarmService
    {
        public List<WakeAlarmRequest> Requests { get; } = [];

        public List<RecordingWakeAlarmLease> Leases { get; } = [];

        public Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
            WakeAlarmRequest request,
            CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            var lease = new RecordingWakeAlarmLease();
            this.Leases.Add(lease);
            return Task.FromResult(new WakeAlarmScheduleResult(
                Supported: true,
                Scheduled: true,
                Message: "Scheduled",
                Lease: lease));
        }
    }

    private sealed class RecordingWakeAlarmLease : IWakeAlarmLease
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            this.DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingWakeAlarmService : IWakeAlarmService
    {
        public Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
            WakeAlarmRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Wake scheduling failed.");
        }
    }

    private sealed class UnsuccessfulWakeAlarmService : IWakeAlarmService
    {
        public Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
            WakeAlarmRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WakeAlarmScheduleResult(
                Supported: false,
                Scheduled: false,
                Message: "Wake alarm unavailable.",
                Lease: null));
        }
    }
}
