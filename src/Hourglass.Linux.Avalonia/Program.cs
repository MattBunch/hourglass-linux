using Avalonia;
using Hourglass.Linux.Services;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return Run(
            args,
            () => new LinuxFileLockSingleInstanceService(),
            startArgs => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startArgs),
            Console.Error);
    }

    internal static int Run(
        string[] args,
        Func<ISingleInstanceService> singleInstanceServiceFactory,
        Func<string[], int> startDesktopLifetime,
        TextWriter errorWriter)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(singleInstanceServiceFactory);
        ArgumentNullException.ThrowIfNull(startDesktopLifetime);
        ArgumentNullException.ThrowIfNull(errorWriter);

        ISingleInstanceService? singleInstanceService = null;
        bool acquired;

        try
        {
            singleInstanceService = singleInstanceServiceFactory();
            acquired = singleInstanceService.TryAcquireAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            singleInstanceService?.Dispose();
            errorWriter.WriteLine($"Hourglass could not acquire the single-instance lock: {exception.Message}");
            return 1;
        }

        if (!acquired)
        {
            singleInstanceService.Dispose();
            return 0;
        }

        try
        {
            return startDesktopLifetime(args);
        }
        finally
        {
            singleInstanceService.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
