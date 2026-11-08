namespace Hourglass.Linux.Services;

using System.Diagnostics;
using Hourglass.Application;

public sealed class HostBootstrap(Func<ProcessStartInfo, Process?>? startProcess = null, Func<string>? resolveExecutable = null)
{
    public async Task<ApplicationResult<RemoteHourglassClient>> StartAsync(RuntimeClientKind kind, CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo start = new((resolveExecutable ?? ResolveExecutable)())
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--background");
            using Process? child = (startProcess ?? Process.Start)(start);
            if (child == null) { return Failed("Host process did not start."); }
            // The child replaces these descriptors before startup. No terminal or parent-owned pipe remains in the runtime.
            child.StandardInput.Close();
            return await RemoteHourglassClient.ConnectWhenReadyAsync(cancellationToken: cancellationToken, kind: kind).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return Failed($"Unable to start hourglass-host: {exception.Message}"); }
    }
    public static string ResolveExecutable() => FrontendExecutable.Resolve(AppContext.BaseDirectory, "hourglass-host", "Hourglass.Host", "host");
    private static ApplicationResult<RemoteHourglassClient> Failed(string message) => new ApplicationResult<RemoteHourglassClient>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, message));
}
