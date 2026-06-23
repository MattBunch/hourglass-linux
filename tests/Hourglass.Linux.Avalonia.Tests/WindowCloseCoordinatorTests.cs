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
            () => Task.FromResult(true),
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
            () => Task.FromResult(true),
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
            () => Task.FromResult(true),
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
            () => Task.FromResult(true),
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
            () => Task.FromResult(true),
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

    [Fact]
    public async Task RejectedApprovalCancelsCloseAndAllowsLaterRetry()
    {
        int approvalCount = 0;
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () => Task.FromResult(++approvalCount > 1),
            () => Task.CompletedTask,
            () => finalCloseCount++,
            () => { });

        Assert.True(coordinator.RequestClose());
        await coordinator.PendingPreparation;
        Assert.Equal(0, finalCloseCount);

        Assert.True(coordinator.RequestClose());
        await coordinator.PendingPreparation;

        Assert.Equal(2, approvalCount);
        Assert.Equal(1, finalCloseCount);
        Assert.False(coordinator.RequestClose());
    }

    [Fact]
    public async Task RepeatedRequestsShareOnePendingApproval()
    {
        var approvalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int approvalCount = 0;
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () =>
            {
                approvalCount++;
                return approvalCompletion.Task;
            },
            () => Task.CompletedTask,
            () => finalCloseCount++,
            () => { });

        Assert.True(coordinator.RequestClose());
        Assert.True(coordinator.RequestClose());
        Assert.Equal(1, approvalCount);

        approvalCompletion.SetResult(true);
        await coordinator.PendingPreparation;

        Assert.Equal(1, approvalCount);
        Assert.Equal(1, finalCloseCount);
    }

    [Fact]
    public async Task ApprovalFailureCancelsCloseWithoutEscaping()
    {
        int finalCloseCount = 0;
        var coordinator = new WindowCloseCoordinator(
            () => Task.FromException<bool>(new InvalidOperationException("Dialog failed.")),
            () => Task.CompletedTask,
            () => finalCloseCount++,
            () => { });

        Exception? exception = await Record.ExceptionAsync(async () =>
        {
            Assert.True(coordinator.RequestClose());
            await coordinator.PendingPreparation;
        });

        Assert.Null(exception);
        Assert.Equal(0, finalCloseCount);
        Assert.True(coordinator.RequestClose());
    }
}
