namespace Hourglass.Platform;

using System.Diagnostics;
using System.Globalization;

public sealed class NoOpDiagnosticSink : IDiagnosticSink
{
    public static NoOpDiagnosticSink Instance { get; } = new();

    private NoOpDiagnosticSink()
    {
    }

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
    }
}

public sealed class TraceDiagnosticSink : IDiagnosticSink
{
    public static TraceDiagnosticSink Instance { get; } = new();

    private TraceDiagnosticSink()
    {
    }

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "[{0}] {1}/{2}/{3}/{4}: {5}",
            diagnosticEvent.Severity,
            diagnosticEvent.FailureClass,
            diagnosticEvent.Category,
            diagnosticEvent.Operation,
            diagnosticEvent.Backend,
            diagnosticEvent.Message);

        if (diagnosticEvent.Exception == null)
        {
            Trace.WriteLine(line);
            return;
        }

        Trace.WriteLine($"{line} ({diagnosticEvent.Exception.GetType().Name}: {diagnosticEvent.Exception.Message})");
    }
}

public sealed class DeduplicatingDiagnosticSink(IDiagnosticSink inner) : IDiagnosticSink
{
    private readonly Lock gate = new();
    private readonly IDiagnosticSink inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly HashSet<DiagnosticKey> emitted = [];

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        var key = new DiagnosticKey(
            diagnosticEvent.Severity,
            diagnosticEvent.FailureClass,
            diagnosticEvent.Category,
            diagnosticEvent.Operation,
            diagnosticEvent.Backend);

        lock (this.gate)
        {
            if (!this.emitted.Add(key))
            {
                return;
            }
        }

        this.inner.Record(diagnosticEvent);
    }

    private readonly record struct DiagnosticKey(
        DiagnosticSeverity Severity,
        DiagnosticFailureClass FailureClass,
        string Category,
        string Operation,
        string Backend);
}

public static class DiagnosticSinkFactory
{
    public static IDiagnosticSink CreateDefault()
    {
        return new DeduplicatingDiagnosticSink(TraceDiagnosticSink.Instance);
    }
}
