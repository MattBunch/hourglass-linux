namespace Hourglass.Linux.Avalonia;

using System.Runtime.InteropServices;

internal static class X11Dri3Support
{
    private const string Dri3ExtensionName = "DRI3";
    private const string X11Library = "libX11.so.6";

    internal static bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return XQueryExtension(display, Dri3ExtensionName, out _, out _, out _) != 0;
            }
            finally
            {
                _ = XCloseDisplay(display);
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    [DllImport(X11Library)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(X11Library, CharSet = CharSet.Ansi)]
    private static extern int XQueryExtension(
        IntPtr display,
        string extensionName,
        out int majorOpcode,
        out int firstEvent,
        out int firstError);

    [DllImport(X11Library)]
    private static extern int XCloseDisplay(IntPtr display);
}
