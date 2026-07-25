namespace Hourglass.Linux.Avalonia;

using Hourglass.Platform;

internal sealed class DesktopProgressController(IDesktopProgressService service, IDiagnosticSink? diagnosticSink = null)
{
    private readonly IDiagnosticSink diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
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
            catch (Exception exception)
            {
                this.RecordFailure(request.IsHidden ? "clear" : "apply", exception);
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
            catch (Exception exception)
            {
                this.RecordFailure("clear", exception);
            }
        }
        finally
        {
            this.operationGate.Release();
        }
    }

    private void RecordFailure(string operation, Exception exception)
    {
        this.diagnosticSink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "desktop-progress",
            operation,
            this.service.GetType().Name,
            "Desktop progress update failed.",
            exception));
    }
}
