using Avalonia;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using System.Net.Sockets;

namespace Hourglass.Linux.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return Run(
            args,
            () => new LinuxFileLockSingleInstanceService(),
            request =>
            {
                App.InitialLaunchRequest = request;
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(request.Arguments.ToArray());
            },
            Console.Error);
    }

    internal static int Run(
        string[] args,
        Func<ISingleInstanceService> singleInstanceServiceFactory,
        Func<SingleInstanceLaunchRequest, int> startDesktopLifetime,
        TextWriter errorWriter)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(singleInstanceServiceFactory);
        ArgumentNullException.ThrowIfNull(startDesktopLifetime);
        ArgumentNullException.ThrowIfNull(errorWriter);

        CommandLineParseResult parseResult = LinuxCommandLineParser.Parse(args);
        if (!parseResult.IsSuccess || parseResult.Request == null)
        {
            errorWriter.WriteLine(parseResult.ErrorMessage);
            return 2;
        }

        SingleInstanceLaunchRequest request = parseResult.Request;
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
            errorWriter.WriteLine(ApplicationStrings.FormatSingleInstanceLockFailed(exception.Message));
            return 1;
        }

        if (!acquired)
        {
            try
            {
                singleInstanceService.SendLaunchRequestAsync(request).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception exception) when (exception is IOException or SocketException or TimeoutException or OperationCanceledException)
            {
                errorWriter.WriteLine(ApplicationStrings.FormatSingleInstanceContactFailed(exception.Message));
                return 1;
            }
            finally
            {
                singleInstanceService.Dispose();
            }
        }

        try
        {
            try
            {
                singleInstanceService.StartRequestListenerAsync(
                    (receivedRequest, _) => SingleInstanceLaunchRequestDispatcher.Shared.DispatchAsync(receivedRequest))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException)
            {
                errorWriter.WriteLine(ApplicationStrings.FormatSingleInstanceListenerFailed(exception.Message));
            }

            return startDesktopLifetime(request);
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
            .With(CreateX11PlatformOptions())
            .LogToTrace();
    }

    internal static X11PlatformOptions CreateX11PlatformOptions()
    {
        return new X11PlatformOptions
        {
            WmClass = "hourglass",
            RenderingMode = X11RenderingModePolicy.Create()
        };
    }
}
