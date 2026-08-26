namespace Hourglass.Linux.Avalonia;

using System.Runtime.InteropServices;

internal static class X11Dri3Support
{
    private const string XcbLibrary = "libxcb.so.1";
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
            IntPtr connection = xcb_connect(IntPtr.Zero, out int defaultScreenIndex);
            if (connection == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (xcb_connection_has_error(connection) != 0)
                {
                    return false;
                }

                return HasUsableDevice(connection, defaultScreenIndex);
            }
            finally
            {
                xcb_disconnect(connection);
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

    private static bool HasUsableDevice(IntPtr connection, int defaultScreenIndex)
    {
        if (defaultScreenIndex < 0)
        {
            return false;
        }

        IntPtr setup = xcb_get_setup(connection);
        if (setup == IntPtr.Zero)
        {
            return false;
        }

        XcbScreenIterator screen = xcb_setup_roots_iterator(setup);
        for (int screenIndex = 0; screenIndex < defaultScreenIndex; screenIndex++)
        {
            if (screen.Remaining <= 0)
            {
                return false;
            }

            xcb_screen_next(ref screen);
        }

        if (screen.Remaining <= 0 || screen.Data == IntPtr.Zero)
        {
            return false;
        }

        uint rootWindow = unchecked((uint)Marshal.ReadInt32(screen.Data));
        XcbDri3OpenCookie openRequest = xcb_dri3_open(
            connection,
            rootWindow,
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

    [DllImport(XcbLibrary)]
    private static extern IntPtr xcb_connect(IntPtr displayName, out int defaultScreenIndex);

    [DllImport(XcbLibrary)]
    private static extern int xcb_connection_has_error(IntPtr connection);

    [DllImport(XcbLibrary)]
    private static extern void xcb_disconnect(IntPtr connection);

    [DllImport(XcbLibrary)]
    private static extern IntPtr xcb_get_setup(IntPtr connection);

    [DllImport(XcbLibrary)]
    private static extern XcbScreenIterator xcb_setup_roots_iterator(IntPtr setup);

    [DllImport(XcbLibrary)]
    private static extern void xcb_screen_next(ref XcbScreenIterator screen);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct XcbScreenIterator
    {
        internal IntPtr Data;
        internal int Remaining;
        internal int Index;
    }
}
