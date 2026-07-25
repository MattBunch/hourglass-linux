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

public sealed class DeduplicatingDiagnosticSink(IDiagnosticSink inner) : IDiagnosticSink, IDiagnosticSuppressionReset
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
            diagnosticEvent.Backend,
            diagnosticEvent.Message,
            diagnosticEvent.Exception?.GetType().FullName ?? string.Empty,
            diagnosticEvent.Exception?.Message ?? string.Empty);

        lock (this.gate)
        {
            if (!this.emitted.Add(key))
            {
                return;
            }
        }

        this.inner.Record(diagnosticEvent);
    }

    public void ResetDuplicateSuppression(string category, string? operation = null, string? backend = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        lock (this.gate)
        {
            this.emitted.RemoveWhere(key =>
                StringComparer.Ordinal.Equals(key.Category, category)
                && (operation == null || StringComparer.Ordinal.Equals(key.Operation, operation))
                && (backend == null || StringComparer.Ordinal.Equals(key.Backend, backend)));
        }
    }

    private readonly record struct DiagnosticKey(
        DiagnosticSeverity Severity,
        DiagnosticFailureClass FailureClass,
        string Category,
        string Operation,
        string Backend,
        string Message,
        string ExceptionType,
        string ExceptionMessage);
}

public static class DiagnosticSinkExtensions
{
    public static void ResetDuplicateSuppression(
        this IDiagnosticSink diagnosticSink,
        string category,
        string? operation = null,
        string? backend = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticSink);

        if (diagnosticSink is IDiagnosticSuppressionReset resettable)
        {
            resettable.ResetDuplicateSuppression(category, operation, backend);
        }
    }
}

public static class DiagnosticSinkFactory
{
    public static IDiagnosticSink CreateDefault()
    {
        return new DeduplicatingDiagnosticSink(TraceDiagnosticSink.Instance);
    }
}
