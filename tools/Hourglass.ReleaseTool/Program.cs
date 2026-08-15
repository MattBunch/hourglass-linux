namespace Hourglass.ReleaseTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return new ReleaseToolApplication().RunAsync(args, Console.Out, Console.Error);
    }
}
