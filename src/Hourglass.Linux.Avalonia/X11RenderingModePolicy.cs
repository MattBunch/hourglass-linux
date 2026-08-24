namespace Hourglass.Linux.Avalonia;

using global::Avalonia;

internal static class X11RenderingModePolicy
{
    internal static X11RenderingMode[] Create(bool isDri3Available)
    {
        return isDri3Available
            ?
            [
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            ]
            : [X11RenderingMode.Software];
    }
}
