namespace Hourglass.Linux.Avalonia;

internal sealed class WindowCloseCoordinator
{
    private readonly Action cleanup;
    private readonly Func<Task> getPendingSave;
    private readonly Action requestFinalClose;
    private bool cleanupCompleted;
    private bool closeApproved;
    private bool closePreparationInProgress;
    private Task pendingPreparation = Task.CompletedTask;

    public WindowCloseCoordinator(
        Func<Task> getPendingSave,
        Action requestFinalClose,
        Action cleanup)
    {
        this.getPendingSave = getPendingSave ?? throw new ArgumentNullException(nameof(getPendingSave));
        this.requestFinalClose = requestFinalClose ?? throw new ArgumentNullException(nameof(requestFinalClose));
        this.cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
    }

    internal Task PendingPreparation => this.pendingPreparation;

    public bool RequestClose()
    {
        if (this.closeApproved)
        {
            return false;
        }

        if (!this.closePreparationInProgress)
        {
            this.closePreparationInProgress = true;
            this.pendingPreparation = this.PrepareCloseAsync();
        }

        return true;
    }

    public void CompleteClose()
    {
        if (this.cleanupCompleted)
        {
            return;
        }

        this.cleanupCompleted = true;
        this.cleanup();
    }

    private async Task PrepareCloseAsync()
    {
        try
        {
            await this.getPendingSave();
        }
        catch (Exception)
        {
        }

        this.closeApproved = true;

        try
        {
            this.requestFinalClose();
        }
        catch (Exception)
        {
        }
    }
}
