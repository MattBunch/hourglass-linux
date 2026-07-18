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

    private static string CreateWakeAlarmFile(string value)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "wakealarm");
        File.WriteAllText(path, value);
        return path;
    }
}
