namespace Hourglass.Cli;

using System.Runtime.InteropServices;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        void Interrupt(object? sender, ConsoleCancelEventArgs eventArgs) { eventArgs.Cancel = true; cancellation.Cancel(); }
        Console.CancelKeyPress += Interrupt;
        using PosixSignalRegistration termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); });
        try { return await new CliApplication(new LocalRuntimeFactory(), Console.Out, Console.Error).RunAsync(args, cancellation.Token).ConfigureAwait(false); }
        finally { Console.CancelKeyPress -= Interrupt; }
    }
}
