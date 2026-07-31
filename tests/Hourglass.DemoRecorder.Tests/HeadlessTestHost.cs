namespace Hourglass.DemoRecorder.Tests;

internal static class HeadlessTestHost
{
    private static readonly object Gate = new();
    private static bool initialized;

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            try
            {
                DemoAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains("Setup was already called", StringComparison.Ordinal))
            {
            }

            initialized = true;
        }
    }
}
