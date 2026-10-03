namespace Hourglass.Host;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Hourglass.Application;
using Hourglass.Linux.Services;
using Hourglass.Platform;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3)); return 0; }
        if (args is ["--help"]) { Console.WriteLine("hourglass-host: internal on-demand Hourglass runtime."); return 0; }
        if (args.Length > 0 && args is not ["--background"]) { Console.Error.WriteLine("Unsupported host arguments."); return 2; }
        if (!OperatingSystem.IsLinux()) { return 6; }
        if (args is ["--background"]) { DetachTerminal(); }
        using CancellationTokenSource stopping = new();
        using PosixSignalRegistration termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stopping.Cancel(); });
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; stopping.Cancel(); });
        using ISingleInstanceService authority = new LinuxFileLockSingleInstanceService();
        using CancellationTokenSource readiness = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        readiness.CancelAfter(TimeSpan.FromSeconds(10));
        using PeriodicTimer election = new(TimeSpan.FromMilliseconds(200));
        try
        {
            while (!await authority.TryAcquireAsync(readiness.Token).ConfigureAwait(false))
            {
                var existing = await RemoteHourglassClient.ConnectAsync(cancellationToken: readiness.Token).ConfigureAwait(false);
                if (existing is ApplicationResult<RemoteHourglassClient>.Success connected) { await connected.Value.DisposeAsync().ConfigureAwait(false); return 0; }
                await election.WaitForNextTickAsync(readiness.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { return stopping.IsCancellationRequested ? 0 : 4; }
        try
        {
            if (await ExclusiveRuntimeFactory.HasUnreadableRecoveryAsync(new XdgSettingsPathService(), new JsonFileSettingsStore(new XdgSettingsPathService()), stopping.Token).ConfigureAwait(false)) { return 7; }
            await using HourglassRuntime runtime = ExclusiveRuntimeFactory.CreateRuntime();
            ApplicationResult<bool> initialized = await runtime.InitializeHostAsync(stopping.Token).ConfigureAwait(false);
            if (initialized is ApplicationResult<bool>.Failure) { return 7; }
            runtime.StartScheduler();
            await using RuntimeControlServer control = new(runtime);
            control.Start();
            await authority.StartRequestListenerAsync(GuiLaunchBridge.ForwardAsync, stopping.Token).ConfigureAwait(false);
            Stopwatch clock = Stopwatch.StartNew();
            HostIdlePolicy idle = new();
            using PeriodicTimer poll = new(TimeSpan.FromMilliseconds(200));
            try
            {
                while (await poll.WaitForNextTickAsync(stopping.Token).ConfigureAwait(false))
                {
                    idle = idle.Observe(clock.Elapsed, await runtime.RequiresAuthorityAsync(stopping.Token).ConfigureAwait(false));
                    if (idle.CanStop(clock.Elapsed) && await runtime.TryBeginIdleShutdownAsync(stopping.Token).ConfigureAwait(false)) { break; }
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            await control.DisposeAsync().ConfigureAwait(false);
            ApplicationResult<bool> durable = await runtime.FlushPersistenceAsync().ConfigureAwait(false);
            return durable is ApplicationResult<bool>.Failure ? 7 : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Console.Error.WriteLine(exception.Message); return 7; }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return 0; }
    }
    private static void DetachTerminal()
    {
        if (SetSession() < 0) { throw new IOException("Unable to detach host from the launching session."); }
        int descriptor = Open("/dev/null", 2);
        if (descriptor < 0) { throw new IOException("Unable to open host standard descriptors."); }
        try { for (int target = 0; target < 3; target++) { if (Duplicate(descriptor, target) < 0) { throw new IOException("Unable to redirect host standard descriptors."); } } }
        finally { if (descriptor > 2) { Close(descriptor); } }
    }
    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)] private static extern int SetSession();
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)] private static extern int Duplicate(int source, int target);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)] private static extern int Close(int descriptor);
}
