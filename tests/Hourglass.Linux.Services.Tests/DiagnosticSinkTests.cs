namespace Hourglass.Linux.Services.Tests;

using Hourglass.Platform;
using Xunit;

public sealed class DiagnosticSinkTests
{
    [Fact]
    public void DeduplicatingSinkEmitsOnlyFirstIdenticalEvent()
    {
        var inner = new RecordingDiagnosticSink();
        var sink = new DeduplicatingDiagnosticSink(inner);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "audio-alerts",
            "play",
            "pw-play",
            "Audio player failed.");

        sink.Record(diagnosticEvent);
        sink.Record(diagnosticEvent);

        DiagnosticEvent recorded = Assert.Single(inner.Events);
        Assert.Equal("Audio player failed.", recorded.Message);
    }

    [Fact]
    public void DeduplicatingSinkPreservesDistinctFailureMessages()
    {
        var inner = new RecordingDiagnosticSink();
        var sink = new DeduplicatingDiagnosticSink(inner);

        sink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "audio-alerts",
            "play",
            "pw-play",
            "Audio player exited with code 1."));
        sink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "audio-alerts",
            "play",
            "pw-play",
            "Audio player exited with code 127."));

        Assert.Equal(2, inner.Events.Count);
    }

    [Fact]
    public void DeduplicatingSinkSeparatesDifferentOperations()
    {
        var inner = new RecordingDiagnosticSink();
        var sink = new DeduplicatingDiagnosticSink(inner);

        sink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "desktop-progress",
            "apply",
            "unity",
            "Apply failed."));
        sink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "desktop-progress",
            "clear",
            "unity",
            "Clear failed."));

        Assert.Equal(2, inner.Events.Count);
    }

    [Fact]
    public void DeduplicatingSinkCanResetMatchingSuppressionBoundary()
    {
        var inner = new RecordingDiagnosticSink();
        var sink = new DeduplicatingDiagnosticSink(inner);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.UserRequested,
            "external-uri",
            "open",
            "xdg-open",
            "URI launcher exited with code 1.");

        sink.Record(diagnosticEvent);
        sink.Record(diagnosticEvent);
        sink.ResetDuplicateSuppression("external-uri", "open", "xdg-open");
        sink.Record(diagnosticEvent);

        Assert.Equal(2, inner.Events.Count);
    }

    [Fact]
    public void DeduplicatingSinkDoesNotPropagateInnerSinkFailure()
    {
        var sink = new DeduplicatingDiagnosticSink(new ThrowingDiagnosticSink());

        sink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.DataRecovery,
            "settings",
            "load",
            "json",
            "Settings could not be loaded."));
    }

    [Fact]
    public void SafeResetDoesNotPropagateResetFailure()
    {
        IDiagnosticSink sink = new ThrowingResetDiagnosticSink();

        sink.ResetDuplicateSuppression("external-uri", "open", "xdg-open");
    }
}

internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    public List<DiagnosticEvent> Events { get; } = [];

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        this.Events.Add(diagnosticEvent);
    }
}

internal sealed class ThrowingDiagnosticSink : IDiagnosticSink
{
    public void Record(DiagnosticEvent diagnosticEvent)
    {
        throw new InvalidOperationException("Diagnostic sink failed.");
    }
}

internal sealed class ThrowingResetDiagnosticSink : IDiagnosticSink, IDiagnosticSuppressionReset
{
    public void Record(DiagnosticEvent diagnosticEvent)
    {
    }

    public void ResetDuplicateSuppression(string category, string? operation = null, string? backend = null)
    {
        throw new InvalidOperationException("Diagnostic reset failed.");
    }
}
