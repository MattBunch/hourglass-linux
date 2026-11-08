namespace Hourglass.Linux.Services;

using System.Diagnostics;
using Hourglass.Platform;

/// <summary>GUI presentation registration is independent of runtime authority.</summary>
public static class GuiLaunchBridge
{
    private static string DirectoryPath => Path.GetDirectoryName(LinuxSingleInstanceLockPath.Resolve(
        Environment.GetEnvironmentVariable, () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
        ?? throw new InvalidOperationException("Missing runtime directory.");
    public static ISingleInstanceService CreateRegistration() => new LinuxFileLockSingleInstanceService(
        Path.Combine(DirectoryPath, "hourglass-gui.lock"), Path.Combine(DirectoryPath, "hourglass-gui.sock"));

    public static async Task ForwardAsync(SingleInstanceLaunchRequest request, CancellationToken token)
    {
        using ISingleInstanceService registration = CreateRegistration();
        if (await registration.TryAcquireAsync(token).ConfigureAwait(false))
        {
            // Release presentation registration before launching its actual owner.
            registration.Dispose();
            string sibling = Path.Combine(AppContext.BaseDirectory, "hourglass-linux");
            string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Release";
            string development = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Hourglass.Linux.Avalonia", "bin", configuration, "net10.0", "hourglass-linux"));
            ProcessStartInfo start = new(File.Exists(sibling) ? sibling : File.Exists(development) ? development : "hourglass-linux") { UseShellExecute = false };
            foreach (string argument in request.Arguments) { start.ArgumentList.Add(argument); }
            using Process? process = Process.Start(start);
            return;
        }
        await registration.SendLaunchRequestAsync(request, token).ConfigureAwait(false);
    }
}
