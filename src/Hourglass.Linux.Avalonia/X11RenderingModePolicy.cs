namespace Hourglass.Linux.Avalonia;

using global::Avalonia;

internal static class X11RenderingModePolicy
{
    internal static X11RenderingMode[] Create()
    {
        return [X11RenderingMode.Software];
    }
}
