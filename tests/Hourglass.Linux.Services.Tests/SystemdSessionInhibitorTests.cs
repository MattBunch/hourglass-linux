namespace Hourglass.Linux.Services.Tests;

using System.ComponentModel;
using System.Diagnostics;
using Xunit;

public sealed class SystemdSessionInhibitorTests
{
    [Theory]
    [InlineData(true, true, "sleep:idle")]
    [InlineData(true, false, "sleep")]
    [InlineData(false, true, "idle")]
    public async Task InhibitAsyncStartsSystemdInhibitWithExpectedArguments(
        bool inhibitSuspend,
        bool inhibitIdle,
        string expectedWhat)
    {
        ProcessStartInfo? capturedStartInfo = null;
        var service = new SystemdSessionInhibitor(startInfo =>
        {
            capturedStartInfo = startInfo;
            return null;
        });

        await service.InhibitAsync("Hourglass timer is running", inhibitSuspend, inhibitIdle);

        Assert.NotNull(capturedStartInfo);
        Assert.Equal("systemd-inhibit", capturedStartInfo.FileName);
        Assert.False(capturedStartInfo.UseShellExecute);
        Assert.Equal(
            [$"--what={expectedWhat}", "--mode=block", "--why=Hourglass timer is running", "sleep", "infinity"],
            capturedStartInfo.ArgumentList);
    }

    [Fact]
    public async Task InhibitAsyncReturnsNullWhenNothingIsRequested()
    {
        var service = new SystemdSessionInhibitor(_ => throw new InvalidOperationException("Should not start."));

        IAsyncDisposable? lease = await service.InhibitAsync("unused", inhibitSuspend: false, inhibitIdle: false);

        Assert.Null(lease);
    }

    [Fact]
    public async Task MissingSystemdInhibitReturnsNull()
    {
        var service = new SystemdSessionInhibitor(_ => throw new Win32Exception());

        IAsyncDisposable? lease = await service.InhibitAsync("Hourglass timer is running", inhibitSuspend: true, inhibitIdle: true);

        Assert.Null(lease);
    }

    [Fact]
    public async Task CancellationIsPropagatedBeforeStartingProcess()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var service = new SystemdSessionInhibitor(_ => throw new InvalidOperationException("Should not start."));

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.InhibitAsync(
                "Hourglass timer is running",
                inhibitSuspend: true,
                inhibitIdle: true,
                cancellationTokenSource.Token));
    }
}
