namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;

public sealed class LinuxExternalUriLauncher : IExternalUriLauncher
{
    private const string BrowserLauncherExecutable = "xdg-open";

    private readonly string executableName;
    private readonly IDiagnosticSink diagnosticSink;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync;

    public LinuxExternalUriLauncher()
        : this(RunProcessAsync, BrowserLauncherExecutable, NoOpDiagnosticSink.Instance)
    {
    }

    public LinuxExternalUriLauncher(IDiagnosticSink diagnosticSink)
        : this(RunProcessAsync, BrowserLauncherExecutable, diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink)))
    {
    }

    internal LinuxExternalUriLauncher(
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync,
        string executableName = BrowserLauncherExecutable,
        IDiagnosticSink? diagnosticSink = null)
    {
        this.runProcessAsync = runProcessAsync ?? throw new ArgumentNullException(nameof(runProcessAsync));
        this.executableName = string.IsNullOrWhiteSpace(executableName)
            ? throw new ArgumentException("Executable name must not be empty.", nameof(executableName))
            : executableName;
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

    public async Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!IsSupportedUri(uri))
        {
            return false;
        }

        try
        {
            int exitCode = await this.runProcessAsync(
                    CreateStartInfo(this.executableName, uri),
                    cancellationToken)
                .ConfigureAwait(false);
            bool opened = exitCode == 0;
            if (!opened)
            {
                this.RecordFailure($"URI launcher exited with code {exitCode}.", null);
            }

            return opened;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            this.RecordFailure("URI launcher command could not be started.", exception);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            this.RecordFailure("URI launcher command failed before completion.", exception);
            return false;
        }
    }

    internal static bool IsSupportedUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }

    internal static ProcessStartInfo CreateStartInfo(string executableName, Uri uri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executableName,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(uri.AbsoluteUri);
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
        this.diagnosticSink.Record(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.UserRequested,
            "external-uri",
            "open",
            this.executableName,
            message,
            exception));
    }
}
