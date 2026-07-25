namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class UnityLauncherDesktopProgressService : IDesktopProgressService
{
    public const string ApplicationUri = "application://io.github.MattBunch.Hourglass.desktop";

    private readonly IUnityLauncherEntrySender sender;
    private readonly IDiagnosticSink diagnosticSink;

    public UnityLauncherDesktopProgressService(IUnityLauncherEntrySender sender, IDiagnosticSink? diagnosticSink = null)
    {
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public bool IsSupported => true;

    public Task SetProgressAsync(
        double fraction,
        DesktopProgressState state,
        CancellationToken cancellationToken = default)
    {
        UnityLauncherEntryUpdate update = CreateUpdate(fraction, state);

        return this.SendUpdateSafelyAsync(update, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return this.SendUpdateSafelyAsync(new UnityLauncherEntryUpdate(false, 0, false), cancellationToken);
    }

    private static UnityLauncherEntryUpdate CreateUpdate(double fraction, DesktopProgressState state)
    {
        return state switch
        {
            DesktopProgressState.Hidden => new UnityLauncherEntryUpdate(false, 0, false),
            DesktopProgressState.Error => new UnityLauncherEntryUpdate(true, 1, true),
            _ => new UnityLauncherEntryUpdate(true, Math.Clamp(fraction, 0, 1), false)
        };
    }

    private async Task SendUpdateSafelyAsync(
        UnityLauncherEntryUpdate update,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.sender.SendUpdateAsync(ApplicationUri, update, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.diagnosticSink.Record(new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticFailureClass.BestEffort,
                "desktop-progress",
                update.ProgressVisible ? "set-progress" : "clear",
                "unity-launcher-entry",
                "Launcher progress update failed.",
                exception));
        }
    }
}
