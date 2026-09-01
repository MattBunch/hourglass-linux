namespace Hourglass.Linux.Avalonia;

using global::Avalonia;

internal static class X11RenderingModePolicy
{
    internal static X11RenderingMode[] Create(string? sessionType, string? waylandDisplay)
    {
        return RequiresSoftwareRendering(sessionType, waylandDisplay)
            ? [X11RenderingMode.Software]
            :
            [
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            ];
    }

    internal static bool RequiresSoftwareRendering(string? sessionType, string? waylandDisplay)
    {
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(waylandDisplay);
    }
}
