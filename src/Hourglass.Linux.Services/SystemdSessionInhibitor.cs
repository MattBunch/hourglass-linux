namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;

public sealed class SystemdSessionInhibitor : ISessionInhibitor
{
    private const string DefaultExecutableName = "systemd-inhibit";
    private const string DefaultSleepExecutableName = "sleep";
    private const string SleepDuration = "infinity";

    private readonly string executableName;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public SystemdSessionInhibitor()
        : this(StartProcess, DefaultExecutableName, NoOpDiagnosticSink.Instance)
    {
    }

    public SystemdSessionInhibitor(IDiagnosticSink diagnosticSink)
        : this(StartProcess, DefaultExecutableName, diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink)))
    {
    }

    internal SystemdSessionInhibitor(
        Func<ProcessStartInfo, Process?> startProcess,
        string executableName = DefaultExecutableName,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
        this.executableName = string.IsNullOrWhiteSpace(executableName)
            ? throw new ArgumentException("Executable name must not be empty.", nameof(executableName))
            : executableName;
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reason);

        if (!inhibitSuspend && !inhibitIdle)
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = CreateStartInfo(this.executableName, reason, inhibitSuspend, inhibitIdle);

        try
        {
            Process? process = this.startProcess(startInfo);
            if (process == null)
            {
                this.RecordFailure("Session inhibitor command did not return a process.", null);
            }

            return ValueTask.FromResult<IAsyncDisposable?>(process == null ? null : new InhibitionLease(process));
        }
        catch (Win32Exception exception)
        {
            this.RecordFailure("Session inhibitor command could not be started.", exception);
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
        catch (InvalidOperationException exception)
        {
            this.RecordFailure("Session inhibitor command failed before completion.", exception);
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executableName,
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executableName,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add($"--what={GetWhatArgument(inhibitSuspend, inhibitIdle)}");
        startInfo.ArgumentList.Add("--mode=block");
        startInfo.ArgumentList.Add($"--why={reason}");
        startInfo.ArgumentList.Add(DefaultSleepExecutableName);
        startInfo.ArgumentList.Add(SleepDuration);

        return startInfo;
    }

    private static string GetWhatArgument(bool inhibitSuspend, bool inhibitIdle)
    {
        return (inhibitSuspend, inhibitIdle) switch
        {
            (true, true) => "sleep:idle",
            (true, false) => "sleep",
            (false, true) => "idle",
            _ => throw new ArgumentException("At least one inhibition target must be requested.")
        };
    }

    private static Process? StartProcess(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo);
    }

    private void RecordFailure(string message, Exception? exception)
    {
        this.diagnosticSink.TryRecord(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "session-inhibition",
            "acquire",
            this.executableName,
            message,
            exception));
    }

    private sealed class InhibitionLease(Process process) : IAsyncDisposable
    {
        private readonly Process process = process;

        public async ValueTask DisposeAsync()
        {
            if (!this.process.HasExited)
            {
                this.process.Kill();
                await this.process.WaitForExitAsync().ConfigureAwait(false);
            }

            this.process.Dispose();
        }
    }
}
