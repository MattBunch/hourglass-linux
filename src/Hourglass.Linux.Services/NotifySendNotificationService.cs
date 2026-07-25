namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;

public sealed class NotifySendNotificationService : INotificationService
{
    private const string AppNameArgument = "--app-name=Hourglass";
    private const string NotifySendExecutable = "notify-send";

    private readonly string executableName;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync;

    public NotifySendNotificationService()
        : this(RunProcessAsync, NotifySendExecutable, NoOpDiagnosticSink.Instance)
    {
    }

    public NotifySendNotificationService(IDiagnosticSink diagnosticSink)
        : this(RunProcessAsync, NotifySendExecutable, diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink)))
    {
    }

    internal NotifySendNotificationService(
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync,
        string executableName = NotifySendExecutable,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.runProcessAsync = runProcessAsync ?? throw new ArgumentNullException(nameof(runProcessAsync));
        this.executableName = string.IsNullOrWhiteSpace(executableName)
            ? throw new ArgumentException("Executable name must not be empty.", nameof(executableName))
            : executableName;
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public async Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        ProcessStartInfo startInfo = CreateStartInfo(this.executableName, title, body);

        try
        {
            int exitCode = await this.runProcessAsync(startInfo, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                this.RecordFailure($"Notification command exited with code {exitCode}.", null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            this.RecordFailure("Notification command could not be started.", exception);
        }
        catch (InvalidOperationException exception)
        {
            this.RecordFailure("Notification command failed before completion.", exception);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executableName, string title, string body)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executableName,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(AppNameArgument);
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add(body);

        return startInfo;
    }

    private static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return -1;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private void RecordFailure(string message, Exception? exception)
    {
        this.diagnosticSink.TryRecord(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "notifications",
            "show-expired",
            this.executableName,
            message,
            exception));
    }
}
