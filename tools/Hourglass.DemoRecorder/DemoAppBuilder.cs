using Avalonia;
using Avalonia.Headless;
using Hourglass.Linux.Avalonia;

namespace Hourglass.DemoRecorder;

public static class DemoAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .LogToTrace();
    }
}
