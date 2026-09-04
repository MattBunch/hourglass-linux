namespace Hourglass.Linux.Avalonia.Tests;

using System.Text;
using Xunit;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void DisabledDiagnosticsDoNotWrite()
    {
        using var writer = new StringWriter();
        var diagnostics = new StartupDiagnostics(_ => null, writer);

        diagnostics.Record(StartupStage.ProcessEntry);
        diagnostics.RecordDisplayContext();

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void EnabledDiagnosticsWriteStagesAndSanitizedDisplayContext()
    {
        using var writer = new StringWriter();
        IReadOnlyDictionary<string, string?> environment = new Dictionary<string, string?>
        {
            ["HOURGLASS_STARTUP_DIAGNOSTICS"] = "1",
            ["DISPLAY"] = ":0",
            ["WAYLAND_DISPLAY"] = "wayland-0",
            ["XDG_SESSION_TYPE"] = "wayland",
            ["UNRELATED_SECRET"] = "not-recorded"
        };
        var diagnostics = new StartupDiagnostics(name => environment.GetValueOrDefault(name), writer);

        diagnostics.Record(StartupStage.ProcessEntry);
        diagnostics.RecordDisplayContext();

        string output = writer.ToString();
        Assert.Contains("[hourglass-startup] stage=ProcessEntry", output, StringComparison.Ordinal);
        Assert.Contains("display=:0", output, StringComparison.Ordinal);
        Assert.Contains("wayland-display=wayland-0", output, StringComparison.Ordinal);
        Assert.Contains("session-type=wayland", output, StringComparison.Ordinal);
        Assert.DoesNotContain("UNRELATED_SECRET", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not-recorded", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsRequireExactEnabledValue()
    {
        using var writer = new StringWriter();
        var diagnostics = new StartupDiagnostics(_ => "true", writer);

        diagnostics.Record(StartupStage.ProcessEntry);

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void DiagnosticsRecordException()
    {
        using var writer = new StringWriter();
        var diagnostics = new StartupDiagnostics(_ => "1", writer);

        diagnostics.RecordException(StartupStage.CoordinatorStartFailed, new InvalidOperationException("startup failed"));

        Assert.Contains("stage=CoordinatorStartFailed", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("exception=System.InvalidOperationException", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("message=startup failed", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriterFailuresDoNotPropagate()
    {
        var diagnostics = new StartupDiagnostics(_ => "1", new ThrowingTextWriter());

        diagnostics.Record(StartupStage.ProcessEntry);
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            throw new IOException("diagnostic writer unavailable");
        }
    }
}
