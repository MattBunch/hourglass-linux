namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Xunit;

public sealed class DbusUnityLauncherEntrySenderTests
{
    [Fact]
    public async Task UnityOwnerRestartReEmitsLatestRunningUpdate()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(true, 0.25, false);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);
        bus.RaiseUnityOwnerChanged();

        Assert.Equal(2, bus.Updates.Count);
        Assert.Equal(update, bus.Updates[1]);
        Assert.Equal(UnityLauncherDesktopProgressService.ApplicationUri, bus.ApplicationUris[1]);
    }

    [Fact]
    public async Task UnityOwnerRestartReEmitsLatestPausedUpdate()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(true, 0.4, false);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);
        bus.RaiseUnityOwnerChanged();

        Assert.Equal(update, bus.Updates[1]);
    }

    [Fact]
    public async Task UnityOwnerRestartReEmitsLatestExpiredUrgentUpdate()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(true, 1, true);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);
        bus.RaiseUnityOwnerChanged();

        Assert.Equal(update, bus.Updates[1]);
    }

    [Fact]
    public async Task UnityOwnerRestartReEmitsHiddenUpdate()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(false, 0, false);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);
        bus.RaiseUnityOwnerChanged();

        Assert.Equal(update, bus.Updates[1]);
    }

    [Fact]
    public void UnityOwnerRestartBeforeAnyUpdateDoesNotSend()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        _ = new DbusUnityLauncherEntrySender(bus);

        bus.RaiseUnityOwnerChanged();

        Assert.Empty(bus.Updates);
    }

    [Theory]
    [InlineData("com.canonical.Unity", ":1.42", true)]
    [InlineData("com.canonical.Unity", "", false)]
    [InlineData("org.example.OtherShell", ":1.42", false)]
    public void NameOwnerChangedFilterReEmitsOnlyWhenUnityBecomesOwned(
        string name,
        string newOwner,
        bool expected)
    {
        Assert.Equal(expected, TmdsUnityLauncherEntryBus.ShouldReemitForNameOwnerChanged(name, newOwner));
    }

    [Fact]
    public async Task ReEmitFailureIsIsolated()
    {
        var bus = new RecordingUnityLauncherEntryBus();
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(true, 0.5, false);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);
        bus.ThrowOnSend = true;
        bus.RaiseUnityOwnerChanged();

        Assert.Equal(2, bus.SendAttempts);
        Assert.Single(bus.Updates);
    }

    [Fact]
    public async Task WatchRegistrationFailureDoesNotPreventNormalSends()
    {
        var bus = new RecordingUnityLauncherEntryBus { ThrowOnWatch = true };
        var sender = new DbusUnityLauncherEntrySender(bus);
        var update = new UnityLauncherEntryUpdate(true, 0.75, false);

        await sender.SendUpdateAsync(UnityLauncherDesktopProgressService.ApplicationUri, update);

        Assert.Equal(update, Assert.Single(bus.Updates));
    }

    private sealed class RecordingUnityLauncherEntryBus : IUnityLauncherEntryBus
    {
        private Action? ownerChanged;

        public List<string> ApplicationUris { get; } = [];

        public int SendAttempts { get; private set; }

        public bool ThrowOnSend { get; set; }

        public bool ThrowOnWatch { get; init; }

        public List<UnityLauncherEntryUpdate> Updates { get; } = [];

        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            this.SendAttempts++;

            if (this.ThrowOnSend)
            {
                throw new InvalidOperationException("Simulated D-Bus send failure.");
            }

            this.ApplicationUris.Add(applicationUri);
            this.Updates.Add(update);
            return Task.CompletedTask;
        }

        public void WatchUnityOwnerChanged(Action ownerChanged)
        {
            if (this.ThrowOnWatch)
            {
                throw new InvalidOperationException("Simulated D-Bus watch failure.");
            }

            this.ownerChanged = ownerChanged;
        }

        public void RaiseUnityOwnerChanged()
        {
            this.ownerChanged?.Invoke();
        }
    }
}
