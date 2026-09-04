namespace Hourglass.Linux.Services.Tests;

using Hourglass.Linux.Services;
using Hourglass.Platform;
using Xunit;

public sealed class DeferredDesktopProgressServiceTests
{
    [Fact]
    public void ConstructionDoesNotInvokeBackendFactory()
    {
        int factoryCalls = 0;

        _ = new DeferredDesktopProgressService(() =>
        {
            factoryCalls++;
            return UnsupportedDesktopProgressService.Instance;
        });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task FirstProgressRequestStartsDiscoveryWithoutWaitingForIt()
    {
        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DeferredDesktopProgressService(() =>
        {
            discoveryStarted.SetResult();
            releaseDiscovery.Task.GetAwaiter().GetResult();
            return UnsupportedDesktopProgressService.Instance;
        });

        Task request = service.SetProgressAsync(0.5, DesktopProgressState.Normal);

        Assert.True(request.IsCompletedSuccessfully);
        await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseDiscovery.SetResult();
    }

    [Fact]
    public async Task LatestPendingProgressIsReplayedWhenSupportedBackendResolves()
    {
        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new RecordingDesktopProgressService();
        var service = new DeferredDesktopProgressService(() =>
        {
            discoveryStarted.SetResult();
            releaseDiscovery.Task.GetAwaiter().GetResult();
            return backend;
        });

        await service.SetProgressAsync(0.25, DesktopProgressState.Normal);
        await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.SetProgressAsync(0.75, DesktopProgressState.Paused);
        releaseDiscovery.SetResult();

        await backend.OperationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["set:Paused:0.75"], backend.Operations);
        Assert.True(service.IsSupported);
    }

    [Fact]
    public async Task PendingClearSupersedesEarlierProgressRequest()
    {
        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new RecordingDesktopProgressService();
        var service = new DeferredDesktopProgressService(() =>
        {
            discoveryStarted.SetResult();
            releaseDiscovery.Task.GetAwaiter().GetResult();
            return backend;
        });

        await service.SetProgressAsync(0.5, DesktopProgressState.Normal);
        await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ClearAsync();
        releaseDiscovery.SetResult();

        await backend.OperationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["clear"], backend.Operations);
    }

    [Fact]
    public async Task UnavailableOrFaultedDiscoveryRemainsNoOp()
    {
        var unavailable = new DeferredDesktopProgressService(() => UnsupportedDesktopProgressService.Instance);
        var faultObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new DeferredDesktopProgressService(() =>
        {
            faultObserved.SetResult();
            throw new InvalidOperationException("D-Bus unavailable");
        });

        await unavailable.SetProgressAsync(0.5, DesktopProgressState.Normal);
        await unavailable.ClearAsync();
        await faulted.SetProgressAsync(0.5, DesktopProgressState.Normal);
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await faulted.ClearAsync();

        Assert.False(unavailable.IsSupported);
        Assert.False(faulted.IsSupported);
    }

    private sealed class RecordingDesktopProgressService : IDesktopProgressService
    {
        private readonly List<string> operations = [];

        public TaskCompletionSource OperationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Operations
        {
            get
            {
                lock (this.operations)
                {
                    return this.operations.ToArray();
                }
            }
        }

        public bool IsSupported => true;

        public Task SetProgressAsync(
            double fraction,
            DesktopProgressState state,
            CancellationToken cancellationToken = default)
        {
            this.AddOperation($"set:{state}:{fraction}");
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            this.AddOperation("clear");
            return Task.CompletedTask;
        }

        private void AddOperation(string operation)
        {
            lock (this.operations)
            {
                this.operations.Add(operation);
            }

            this.OperationCompleted.TrySetResult();
        }
    }
}
