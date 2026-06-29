namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Hourglass.Platform;
using Xunit;

public sealed class UnityLauncherDesktopProgressServiceTests
{
    [Fact]
    public async Task RunningProgressSendsVisibleClampedProgress()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.SetProgressAsync(1.25, DesktopProgressState.Normal);

        UnityLauncherEntryUpdate update = Assert.Single(sender.Updates);
        Assert.Equal(UnityLauncherDesktopProgressService.ApplicationUri, sender.ApplicationUris[0]);
        Assert.True(update.ProgressVisible);
        Assert.Equal(1, update.Progress);
        Assert.False(update.Urgent);
    }

    [Fact]
    public async Task PausedProgressKeepsVisibleFractionWithoutUrgency()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.SetProgressAsync(0.4, DesktopProgressState.Paused);

        UnityLauncherEntryUpdate update = Assert.Single(sender.Updates);
        Assert.True(update.ProgressVisible);
        Assert.Equal(0.4, update.Progress);
        Assert.False(update.Urgent);
    }

    [Fact]
    public async Task ExpiredProgressSendsFullUrgentProgress()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.SetProgressAsync(0.2, DesktopProgressState.Error);

        UnityLauncherEntryUpdate update = Assert.Single(sender.Updates);
        Assert.True(update.ProgressVisible);
        Assert.Equal(1, update.Progress);
        Assert.True(update.Urgent);
    }

    [Fact]
    public async Task HiddenProgressClearsLauncherEntry()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.SetProgressAsync(0.6, DesktopProgressState.Hidden);

        UnityLauncherEntryUpdate update = Assert.Single(sender.Updates);
        Assert.False(update.ProgressVisible);
        Assert.Equal(0, update.Progress);
        Assert.False(update.Urgent);
    }

    [Fact]
    public async Task ClearHidesProgressAndUrgency()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.ClearAsync();

        UnityLauncherEntryUpdate update = Assert.Single(sender.Updates);
        Assert.False(update.ProgressVisible);
        Assert.Equal(0, update.Progress);
        Assert.False(update.Urgent);
    }

    [Fact]
    public async Task SenderFailuresAreIsolated()
    {
        var sender = new RecordingUnityLauncherEntrySender { ThrowOnSend = true };
        var service = new UnityLauncherDesktopProgressService(sender);

        await service.SetProgressAsync(0.5, DesktopProgressState.Normal);
        await service.ClearAsync();

        Assert.Equal(2, sender.Attempts);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        var sender = new RecordingUnityLauncherEntrySender();
        var service = new UnityLauncherDesktopProgressService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SetProgressAsync(0.5, DesktopProgressState.Normal, cancellationTokenSource.Token));
    }

    private sealed class RecordingUnityLauncherEntrySender : IUnityLauncherEntrySender
    {
        public List<string> ApplicationUris { get; } = [];

        public int Attempts { get; private set; }

        public List<UnityLauncherEntryUpdate> Updates { get; } = [];

        public bool ThrowOnSend { get; init; }

        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            this.Attempts++;
            this.ApplicationUris.Add(applicationUri);

            if (this.ThrowOnSend)
            {
                throw new InvalidOperationException("Simulated D-Bus failure.");
            }

            this.Updates.Add(update);
            return Task.CompletedTask;
        }
    }
}
