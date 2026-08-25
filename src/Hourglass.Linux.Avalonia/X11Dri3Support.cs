namespace Hourglass.Linux.Avalonia;

using System.Runtime.InteropServices;

internal static class X11Dri3Support
{
    private const string X11Library = "libX11.so.6";
    private const string X11XcbLibrary = "libX11-xcb.so.1";
    private const string XcbDri3Library = "libxcb-dri3.so.0";
    private const string LibcLibrary = "libc.so.6";

    internal static bool HasUsableDevice()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            if (XInitThreads() == 0)
            {
                return false;
            }

            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return HasUsableDevice(display);
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

    private static bool HasUsableDevice(IntPtr display)
    {
        IntPtr connection = XGetXCBConnection(display);
        if (connection == IntPtr.Zero)
        {
            return false;
        }

        XcbDri3OpenCookie openRequest = xcb_dri3_open(
            connection,
            XDefaultRootWindow(display),
            provider: 0);
        IntPtr reply = xcb_dri3_open_reply(connection, openRequest, out IntPtr error);

        try
        {
            if (error != IntPtr.Zero || reply == IntPtr.Zero || Marshal.ReadByte(reply, 1) == 0)
            {
                return false;
            }

            IntPtr fileDescriptors = xcb_dri3_open_reply_fds(connection, reply);
            if (fileDescriptors == IntPtr.Zero)
            {
                return false;
            }

            int fileDescriptor = Marshal.ReadInt32(fileDescriptors);
            if (fileDescriptor < 0)
            {
                return false;
            }

            _ = close(fileDescriptor);
            return true;
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                free(error);
            }

            if (reply != IntPtr.Zero)
            {
                free(reply);
            }
        }
    }

    [DllImport(X11Library)]
    private static extern int XInitThreads();

    [DllImport(X11Library)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(X11Library)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(X11Library)]
    private static extern uint XDefaultRootWindow(IntPtr display);

    [DllImport(X11XcbLibrary)]
    private static extern IntPtr XGetXCBConnection(IntPtr display);

    [DllImport(XcbDri3Library)]
    private static extern XcbDri3OpenCookie xcb_dri3_open(IntPtr connection, uint drawable, uint provider);

    [DllImport(XcbDri3Library)]
    private static extern IntPtr xcb_dri3_open_reply(
        IntPtr connection,
        XcbDri3OpenCookie cookie,
        out IntPtr error);

    [DllImport(XcbDri3Library)]
    private static extern IntPtr xcb_dri3_open_reply_fds(IntPtr connection, IntPtr reply);

    [DllImport(LibcLibrary)]
    private static extern int close(int fileDescriptor);

    [DllImport(LibcLibrary)]
    private static extern void free(IntPtr pointer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct XcbDri3OpenCookie(uint Sequence);
}
