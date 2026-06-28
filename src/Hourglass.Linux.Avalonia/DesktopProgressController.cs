namespace Hourglass.Linux.Avalonia;

using Hourglass.Platform;

internal sealed class DesktopProgressController(IDesktopProgressService service)
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly IDesktopProgressService service = service ?? throw new ArgumentNullException(nameof(service));
    private DesktopProgressRequest? lastAppliedRequest;

    public async Task ApplyAsync(DesktopProgressRequest request, CancellationToken cancellationToken = default)
    {
        await this.operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (this.lastAppliedRequest == request)
            {
                return;
            }

            try
            {
                if (!this.service.IsSupported || request.IsHidden)
                {
                    await this.service.ClearAsync(cancellationToken).ConfigureAwait(false);
                    this.lastAppliedRequest = request;
                    return;
                }

                await this.service.SetProgressAsync(request.Fraction, request.State, cancellationToken).ConfigureAwait(false);
                this.lastAppliedRequest = request;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            this.operationGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await this.operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            try
            {
                await this.service.ClearAsync(cancellationToken).ConfigureAwait(false);
                this.lastAppliedRequest = new DesktopProgressRequest(DesktopProgressState.Hidden, 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            this.operationGate.Release();
        }
    }
}
