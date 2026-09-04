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
        StartupDiagnostics startupDiagnostics = StartupDiagnostics.CreateDefault(Console.Error);
        RegisterUnhandledExceptionDiagnostics(startupDiagnostics);
        startupDiagnostics.Record(StartupStage.ProcessEntry);
        startupDiagnostics.RecordDisplayContext();

        return Run(
            args,
            () => new LinuxFileLockSingleInstanceService(),
            request =>
            {
                App.InitialLaunchRequest = request;
                return BuildAvaloniaApp(startupDiagnostics).StartWithClassicDesktopLifetime(request.Arguments.ToArray());
            },
            Console.Error,
            startupDiagnostics);
    }

    internal static int Run(
        string[] args,
        Func<ISingleInstanceService> singleInstanceServiceFactory,
        Func<SingleInstanceLaunchRequest, int> startDesktopLifetime,
        TextWriter errorWriter)
    {
        return Run(
            args,
            singleInstanceServiceFactory,
            startDesktopLifetime,
            errorWriter,
            StartupDiagnostics.Disabled);
    }

    internal static int Run(
        string[] args,
        Func<ISingleInstanceService> singleInstanceServiceFactory,
        Func<SingleInstanceLaunchRequest, int> startDesktopLifetime,
        TextWriter errorWriter,
        StartupDiagnostics startupDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(singleInstanceServiceFactory);
        ArgumentNullException.ThrowIfNull(startDesktopLifetime);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(startupDiagnostics);

        CommandLineParseResult parseResult = LinuxCommandLineParser.Parse(args);
        if (!parseResult.IsSuccess || parseResult.Request == null)
        {
            errorWriter.WriteLine(parseResult.ErrorMessage);
            return 2;
        }

        startupDiagnostics.Record(StartupStage.CommandLineParsed);
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
                startupDiagnostics.Record(StartupStage.SingleInstanceForwarded);
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
            startupDiagnostics.Record(StartupStage.SingleInstanceAcquired);
            try
            {
                singleInstanceService.StartRequestListenerAsync(
                    (receivedRequest, _) => SingleInstanceLaunchRequestDispatcher.Shared.DispatchAsync(receivedRequest))
                    .GetAwaiter()
                    .GetResult();
                startupDiagnostics.Record(StartupStage.RequestListenerStarted);
            }
            catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException)
            {
                errorWriter.WriteLine(ApplicationStrings.FormatSingleInstanceListenerFailed(exception.Message));
            }

            try
            {
                startupDiagnostics.Record(StartupStage.DesktopLifetimeStarting);
                return startDesktopLifetime(request);
            }
            catch (Exception exception)
            {
                startupDiagnostics.RecordException(StartupStage.DesktopLifetimeFailed, exception);
                throw;
            }
        }
        finally
        {
            singleInstanceService.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(StartupDiagnostics.Disabled);
    }

    internal static AppBuilder BuildAvaloniaApp(StartupDiagnostics startupDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(startupDiagnostics);

        startupDiagnostics.Record(StartupStage.AppBuilderCreated);
        AppBuilder appBuilder = AppBuilder.Configure(() => new App(startupDiagnostics))
            .UsePlatformDetect()
            .With(CreateX11PlatformOptions())
            .LogToTrace();
        startupDiagnostics.Record(StartupStage.PlatformDetectionConfigured);
        return appBuilder;
    }

    internal static X11PlatformOptions CreateX11PlatformOptions()
    {
        return new X11PlatformOptions
        {
            WmClass = "hourglass",
            RenderingMode = X11RenderingModePolicy.Create()
        };
    }

    private static void RegisterUnhandledExceptionDiagnostics(StartupDiagnostics startupDiagnostics)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                startupDiagnostics.RecordException(StartupStage.UnhandledException, exception);
            }
            else
            {
                startupDiagnostics.Record(StartupStage.UnhandledException);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            startupDiagnostics.RecordException(StartupStage.UnobservedTaskException, eventArgs.Exception);
    }
}
