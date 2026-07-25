namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Platform;

internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    public List<DiagnosticEvent> Events { get; } = [];

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        this.Events.Add(diagnosticEvent);
    }
}
