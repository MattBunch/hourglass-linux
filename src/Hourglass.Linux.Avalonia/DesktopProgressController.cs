namespace Hourglass.Linux.Avalonia;

using Hourglass.Platform;

internal sealed class DesktopProgressController(IDesktopProgressService service)
{
    private readonly IDesktopProgressService service = service ?? throw new ArgumentNullException(nameof(service));
    private DesktopProgressRequest? lastAppliedRequest;

    public async Task ApplyAsync(DesktopProgressRequest request, CancellationToken cancellationToken = default)
    {
        if (this.lastAppliedRequest == request)
        {
            return;
        }

        this.lastAppliedRequest = request;

        try
        {
            if (!this.service.IsSupported || request.IsHidden)
            {
                await this.service.ClearAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await this.service.SetProgressAsync(request.Fraction, request.State, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await this.service.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }
        finally
        {
            this.lastAppliedRequest = new DesktopProgressRequest(DesktopProgressState.Hidden, 0);
        }
    }
}
