namespace Hourglass.Tui;

using System.Runtime.InteropServices;
using Hourglass.Application;
using Hourglass.Linux.Services;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.Out.WriteLine("hourglass-tui: interactive Hourglass timer. Keys: n new, e edit, Tab select, Space pause/resume, ? help, q quit.");
            return 0;
        }
        if (args is ["--version"])
        {
            Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");
            return 0;
        }
        if (args.Length > 0 || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("The TUI requires an interactive terminal. Use --help or --version for information.");
            return 2;
        }

        using CancellationTokenSource interrupted = new();
        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs) { eventArgs.Cancel = true; interrupted.Cancel(); }
        Console.CancelKeyPress += Cancel;
        using PosixSignalRegistration termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            interrupted.Cancel();
        });
        try
        {
            ApplicationResult<ExclusiveRuntimeLease> opened = await new ExclusiveRuntimeFactory()
                .OpenAsync(ExclusiveRuntimePurpose.Sessions, interrupted.Token).ConfigureAwait(false);
            if (opened is ApplicationResult<ExclusiveRuntimeLease>.Failure unavailable)
            {
                Console.Error.WriteLine(unavailable.Error.Message);
                return unavailable.Error.Code switch
                {
                    ApplicationErrorCode.PersistenceFailure => 7,
                    ApplicationErrorCode.TransportFailure => 5,
                    ApplicationErrorCode.Unsupported => 6,
                    _ => 4
                };
            }
            await using ExclusiveRuntimeLease lease = ((ApplicationResult<ExclusiveRuntimeLease>.Success)opened).Value;
            TuiController controller = new(lease.Client);
            await controller.RefreshAsync(interrupted.Token).ConfigureAwait(false);
            try
            {
                Action? tickHook = Environment.GetEnvironmentVariable("HOURGLASS_TUI_TEST_THROW") == "1"
                    ? () => throw new InvalidOperationException("Intentional TUI test failure.") : null;
                TuiShell shell = new(controller, interrupted.Token, tickHook);
                bool cleanQuit = shell.Run();
                if (interrupted.IsCancellationRequested)
                {
                    using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
                    await controller.CloseOwnedAsync(cleanup.Token).ConfigureAwait(false);
                    return 130;
                }
                if (!cleanQuit) { Console.Error.WriteLine(controller.State.Error ?? "The TUI could not complete its shutdown."); }
                return cleanQuit ? 0 : 7;
            }
            catch (Exception exception)
            {
                // TuiShell.Run disposes the terminal before control reaches this handler.
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }
        catch (OperationCanceledException) when (interrupted.IsCancellationRequested) { return 130; }
        finally { Console.CancelKeyPress -= Cancel; }
    }
}
