namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Platform;
using Hourglass.Timing;
using Xunit;

public sealed class Milestone6CoordinatorTests
{
    [Fact]
    public void NewTimerCommandPublishesCoordinatorRequest()
    {
        var viewModel = new MainWindowViewModel(new CountdownEngine(new TestMonotonicClock()), () => DateTime.Now);
        int requestCount = 0;
        viewModel.NewTimerRequested += (_, _) => requestCount++;

        viewModel.NewTimerCommand.Execute(null);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task CoordinatedSessionInhibitorSharesLeaseUntilLastTimerReleases()
    {
        var inner = new RecordingSessionInhibitor();
        await using var coordinator = new CoordinatedSessionInhibitor(inner);

        IAsyncDisposable? first = await coordinator.InhibitAsync(
            "Timer one",
            inhibitSuspend: true,
            inhibitIdle: true);
        IAsyncDisposable? second = await coordinator.InhibitAsync(
            "Timer two",
            inhibitSuspend: true,
            inhibitIdle: true);

        Assert.Equal(1, inner.AcquireCount);
        Assert.Equal(0, inner.ReleaseCount);

        Assert.NotNull(first);
        await first.DisposeAsync();
        Assert.Equal(0, inner.ReleaseCount);

        Assert.NotNull(second);
        await second.DisposeAsync();
        Assert.Equal(1, inner.ReleaseCount);
    }

    private sealed class RecordingSessionInhibitor : ISessionInhibitor
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask<IAsyncDisposable?> InhibitAsync(
            string reason,
            bool inhibitSuspend,
            bool inhibitIdle,
            CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;
            return ValueTask.FromResult<IAsyncDisposable?>(new Lease(this));
        }

        private sealed class Lease(RecordingSessionInhibitor owner) : IAsyncDisposable
        {
            private RecordingSessionInhibitor? owner = owner;

            public ValueTask DisposeAsync()
            {
                RecordingSessionInhibitor? current = Interlocked.Exchange(ref this.owner, null);
                if (current != null)
                {
                    current.ReleaseCount++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }
    }
}
