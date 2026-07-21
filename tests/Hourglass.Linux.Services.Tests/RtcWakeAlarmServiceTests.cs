namespace Hourglass.Linux.Services.Tests;

using Hourglass.Platform;
using Xunit;

public sealed class RtcWakeAlarmServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryScheduleWakeAsyncWritesAndVerifiesFutureUnixTime()
    {
        string path = CreateWakeAlarmFile("0");
        var service = new RtcWakeAlarmService(path, () => Now);
        DateTimeOffset wakeAt = Now.AddMinutes(10);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(new WakeAlarmRequest(wakeAt, "Timer"));

        Assert.True(result.Supported);
        Assert.True(result.Scheduled);
        Assert.NotNull(result.Lease);
        Assert.Equal(wakeAt.ToUnixTimeSeconds().ToString(), File.ReadAllText(path));
    }

    [Fact]
    public async Task LeaseCancelsAlarmOnlyWhenScheduledValueIsStillCurrent()
    {
        string path = CreateWakeAlarmFile("0");
        var service = new RtcWakeAlarmService(path, () => Now);
        DateTimeOffset wakeAt = Now.AddMinutes(10);
        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(new WakeAlarmRequest(wakeAt, "Timer"));

        Assert.NotNull(result.Lease);
        await result.Lease.DisposeAsync();

        Assert.Equal("0", File.ReadAllText(path));
    }

    [Fact]
    public async Task LeaseDoesNotCancelDifferentAlarm()
    {
        string path = CreateWakeAlarmFile("0");
        var service = new RtcWakeAlarmService(path, () => Now);
        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));
        string differentAlarm = Now.AddMinutes(20).ToUnixTimeSeconds().ToString();
        File.WriteAllText(path, differentAlarm);

        Assert.NotNull(result.Lease);
        await result.Lease.DisposeAsync();

        Assert.Equal(differentAlarm, File.ReadAllText(path));
    }

    [Fact]
    public async Task TryScheduleWakeAsyncRefusesExistingFutureAlarm()
    {
        string path = CreateWakeAlarmFile(Now.AddMinutes(5).ToUnixTimeSeconds().ToString());
        var service = new RtcWakeAlarmService(path, () => Now);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Supported);
        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Equal(Now.AddMinutes(5).ToUnixTimeSeconds().ToString(), File.ReadAllText(path));
    }

    [Fact]
    public async Task MissingWakeAlarmFileIsUnsupported()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "wakealarm");
        var service = new RtcWakeAlarmService(path, () => Now);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Supported);
        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
    }

    [Fact]
    public async Task PastWakeTimeIsUnsupported()
    {
        string path = CreateWakeAlarmFile("0");
        var service = new RtcWakeAlarmService(path, () => Now);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddSeconds(-1), "Timer"));

        Assert.False(result.Supported);
        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Equal("0", File.ReadAllText(path));
    }

    [Fact]
    public async Task CancellationAfterWritingRequestedTimestampRollsBack()
    {
        var file = new FakeWakeAlarmFile("0");
        using var cancellation = new CancellationTokenSource();
        long requestedUnixTime = Now.AddMinutes(10).ToUnixTimeSeconds();
        file.AfterWrite = value =>
        {
            if (value == requestedUnixTime.ToString())
            {
                cancellation.Cancel();
            }
        };
        RtcWakeAlarmService service = CreateService(file);

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await service.TryScheduleWakeAsync(
                new WakeAlarmRequest(Now.AddMinutes(10), "Timer"),
                cancellation.Token));

        Assert.Equal("0", file.Value);
    }

    [Fact]
    public async Task VerificationMismatchAfterWritingRequestedTimestampRollsBack()
    {
        var file = new FakeWakeAlarmFile("0")
        {
            VerificationReadValue = Now.AddMinutes(11).ToUnixTimeSeconds().ToString()
        };
        RtcWakeAlarmService service = CreateService(file);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Equal("0", file.Value);
    }

    [Fact]
    public async Task VerificationReadFailureAfterWritingRequestedTimestampRollsBack()
    {
        var file = new FakeWakeAlarmFile("0") { ThrowOnVerificationRead = true };
        RtcWakeAlarmService service = CreateService(file);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Equal("0", file.Value);
    }

    [Fact]
    public async Task RollbackDoesNotClearDifferentNewerTimestamp()
    {
        string newerAlarm = Now.AddMinutes(20).ToUnixTimeSeconds().ToString();
        var file = new FakeWakeAlarmFile("0")
        {
            VerificationReadValue = newerAlarm,
            ApplyVerificationReadValueToCurrentAlarm = true
        };
        RtcWakeAlarmService service = CreateService(file);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Scheduled);
        Assert.Null(result.Lease);
        Assert.Equal(newerAlarm, file.Value);
    }

    [Fact]
    public async Task RollbackFailurePreservesPrimaryVerificationFailureResult()
    {
        var file = new FakeWakeAlarmFile("0")
        {
            VerificationReadValue = Now.AddMinutes(11).ToUnixTimeSeconds().ToString(),
            ThrowOnRollbackWrite = true
        };
        RtcWakeAlarmService service = CreateService(file);

        WakeAlarmScheduleResult result = await service.TryScheduleWakeAsync(
            new WakeAlarmRequest(Now.AddMinutes(10), "Timer"));

        Assert.False(result.Scheduled);
        Assert.Equal("RTC wake alarm could not be verified after scheduling.", result.Message);
    }

    private static string CreateWakeAlarmFile(string value)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "wakealarm");
        File.WriteAllText(path, value);
        return path;
    }

    private static RtcWakeAlarmService CreateService(FakeWakeAlarmFile file)
    {
        return RtcWakeAlarmService.CreateForTests(
            "/tmp/hourglass-test-wakealarm",
            () => Now,
            file.ReadAllTextAsync,
            file.WriteAllTextAsync,
            _ => true);
    }

    private sealed class FakeWakeAlarmFile(string value)
    {
        private int readCount;

        public string Value { get; private set; } = value;

        public string? VerificationReadValue { get; init; }

        public bool ApplyVerificationReadValueToCurrentAlarm { get; init; }

        public bool ThrowOnVerificationRead { get; init; }

        public bool ThrowOnRollbackWrite { get; init; }

        public Action<string>? AfterWrite { get; set; }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<string>(cancellationToken);
            }

            this.readCount++;
            if (this.readCount == 2)
            {
                if (this.ThrowOnVerificationRead)
                {
                    throw new IOException("Verification read failed.");
                }

                if (this.VerificationReadValue != null)
                {
                    if (this.ApplyVerificationReadValueToCurrentAlarm)
                    {
                        this.Value = this.VerificationReadValue;
                    }

                    return Task.FromResult(this.VerificationReadValue);
                }
            }

            return Task.FromResult(this.Value);
        }

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            if (this.ThrowOnRollbackWrite && contents == "0" && this.readCount > 2)
            {
                throw new IOException("Rollback write failed.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            this.Value = contents;
            this.AfterWrite?.Invoke(contents);
            return Task.CompletedTask;
        }
    }
}
