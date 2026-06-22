namespace Hourglass.Linux.Avalonia.Tests;

using Xunit;

public sealed class WindowCloseCoordinatorTests
{
    [Fact]
    public async Task CompletedPendingSaveProducesOneOrderlyCloseCycle()
    {
        int finalCloseCount = 0;
        int cleanupCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () => Task.CompletedTask,
            () => finalCloseCount++,
            () => cleanupCount++);

        bool firstCloseCancelled = coordinator.RequestClose();
        await coordinator.PendingPreparation;
        bool finalCloseCancelled = coordinator.RequestClose();
        coordinator.CompleteClose();
        coordinator.CompleteClose();

        Assert.True(firstCloseCancelled);
        Assert.False(finalCloseCancelled);
        Assert.Equal(1, finalCloseCount);
        Assert.Equal(1, cleanupCount);
    }

    [Fact]
    public async Task IncompletePendingSaveDefersFinalCloseUntilCompletion()
    {
        var saveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () => saveCompletion.Task,
            () => finalCloseCount++,
            () => { });

        Assert.True(coordinator.RequestClose());
        Assert.Equal(0, finalCloseCount);

        saveCompletion.SetResult();
        await coordinator.PendingPreparation;

        Assert.Equal(1, finalCloseCount);
        Assert.False(coordinator.RequestClose());
    }

    [Fact]
    public async Task RepeatedCloseRequestsShareOnePendingSaveAndOneFinalClose()
    {
        var saveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int pendingSaveRequestCount = 0;
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () =>
            {
                pendingSaveRequestCount++;
                return saveCompletion.Task;
            },
            () => finalCloseCount++,
            () => { });

        Assert.True(coordinator.RequestClose());
        Assert.True(coordinator.RequestClose());
        Assert.True(coordinator.RequestClose());
        Assert.Equal(1, pendingSaveRequestCount);
        Assert.Equal(0, finalCloseCount);

        saveCompletion.SetResult();
        await coordinator.PendingPreparation;

        Assert.Equal(1, pendingSaveRequestCount);
        Assert.Equal(1, finalCloseCount);
    }

    [Fact]
    public async Task FaultedPendingSaveStillApprovesFinalClose()
    {
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () => Task.FromException(new InvalidOperationException("Settings save failed.")),
            () => finalCloseCount++,
            () => { });

        Exception? exception = await Record.ExceptionAsync(async () =>
        {
            Assert.True(coordinator.RequestClose());
            await coordinator.PendingPreparation;
        });

        Assert.Null(exception);
        Assert.Equal(1, finalCloseCount);
        Assert.False(coordinator.RequestClose());
    }

    [Fact]
    public async Task FinalCloseCallbackFailureDoesNotEscapePreparation()
    {
        var coordinator = new WindowCloseCoordinator(
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("Close failed."),
            () => { });

        Exception? exception = await Record.ExceptionAsync(async () =>
        {
            Assert.True(coordinator.RequestClose());
            await coordinator.PendingPreparation;
        });

        Assert.Null(exception);
        Assert.False(coordinator.RequestClose());
    }
}
