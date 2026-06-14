namespace Hourglass.Linux.Services;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Platform;

public sealed class NotifySendNotificationService : INotificationService
{
    private const string AppNameArgument = "--app-name=Hourglass";
    private const string NotifySendExecutable = "notify-send";

    private readonly string executableName;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync;

    public NotifySendNotificationService()
        : this(RunProcessAsync, NotifySendExecutable)
    {
    }

    internal NotifySendNotificationService(
        Func<ProcessStartInfo, CancellationToken, Task<int>> runProcessAsync,
        string executableName = NotifySendExecutable)
    {
        this.runProcessAsync = runProcessAsync ?? throw new ArgumentNullException(nameof(runProcessAsync));
        this.executableName = string.IsNullOrWhiteSpace(executableName)
            ? throw new ArgumentException("Executable name must not be empty.", nameof(executableName))
            : executableName;
    }

    public async Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        ProcessStartInfo startInfo = CreateStartInfo(this.executableName, title, body);

        try
        {
            _ = await this.runProcessAsync(startInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
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
}
