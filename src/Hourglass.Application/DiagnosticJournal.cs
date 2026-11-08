namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Platform;

public sealed record ApplicationDiagnostic(DiagnosticSeverity Severity, string Category, string Operation, string Message, long Sequence);

/// <summary>Bounded, thread-safe diagnostic history for terminal clients.</summary>
public sealed class DiagnosticJournal : IDiagnosticSink
{
    private const int MaximumEntries = 128;
    private readonly object gate = new();
    private readonly Queue<ApplicationDiagnostic> entries = new();
    private long sequence;

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        lock (this.gate)
        {
            if (this.entries.Count == MaximumEntries) { this.entries.Dequeue(); }
            this.entries.Enqueue(new(diagnosticEvent.Severity, diagnosticEvent.Category, diagnosticEvent.Operation,
                diagnosticEvent.Message, ++this.sequence));
        }
    }

    public ImmutableArray<ApplicationDiagnostic> Snapshot()
    {
        lock (this.gate) { return this.entries.ToImmutableArray(); }
    }
}
