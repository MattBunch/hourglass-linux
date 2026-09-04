namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class DeferredDesktopProgressService : IDesktopProgressService
{
    private readonly Func<IDesktopProgressService> backendFactory;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly object gate = new();
    private readonly TaskCompletionSource initializationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDesktopProgressService? backend;
    private DesktopProgressCommand? pendingCommand;
    private bool initializationStarted;

    public DeferredDesktopProgressService(
        Func<IDesktopProgressService> backendFactory,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public bool IsSupported
    {
        get
        {
            lock (this.gate)
            {
                return this.backend?.IsSupported == true;
            }
        }
    }

    internal Task InitializationCompleted => this.initializationCompletion.Task;

    public Task SetProgressAsync(
        double fraction,
        DesktopProgressState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return this.ApplyAsync(new DesktopProgressCommand(state, fraction), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return this.ApplyAsync(new DesktopProgressCommand(DesktopProgressState.Hidden, 0), cancellationToken);
    }

    private Task ApplyAsync(DesktopProgressCommand command, CancellationToken cancellationToken)
    {
        IDesktopProgressService? initializedBackend;

        lock (this.gate)
        {
            initializedBackend = this.backend;
            if (initializedBackend is null)
            {
                this.pendingCommand = command;
                if (!this.initializationStarted)
                {
                    this.initializationStarted = true;
                    _ = Task.Run(this.InitializeAsync);
                }

                return Task.CompletedTask;
            }
        }

        return ApplyToBackendAsync(initializedBackend, command, cancellationToken);
    }

    private async Task InitializeAsync()
    {
        try
        {
            IDesktopProgressService createdBackend = this.backendFactory();

            if (!createdBackend.IsSupported)
            {
                lock (this.gate)
                {
                    this.backend = createdBackend;
                    this.pendingCommand = null;
                }

                return;
            }

            while (true)
            {
                DesktopProgressCommand? command;
                lock (this.gate)
                {
                    command = this.pendingCommand;
                    this.pendingCommand = null;
                }

                if (command is not null)
                {
                    await ApplyToBackendAsync(createdBackend, command.Value, CancellationToken.None).ConfigureAwait(false);
                }

                lock (this.gate)
                {
                    if (this.pendingCommand is null)
                    {
                        this.backend = createdBackend;
                        return;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            this.diagnosticSink.TryRecord(new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticFailureClass.StartupConfiguration,
                "desktop-progress",
                "initialize",
                "unity",
                "Unity launcher desktop progress backend could not be initialized.",
                exception));
        }
        finally
        {
            this.initializationCompletion.TrySetResult();
        }
    }

    private static Task ApplyToBackendAsync(
        IDesktopProgressService backend,
        DesktopProgressCommand command,
        CancellationToken cancellationToken)
    {
        return command.State == DesktopProgressState.Hidden
            ? backend.ClearAsync(cancellationToken)
            : backend.SetProgressAsync(command.Fraction, command.State, cancellationToken);
    }

    private readonly record struct DesktopProgressCommand(DesktopProgressState State, double Fraction);
}
