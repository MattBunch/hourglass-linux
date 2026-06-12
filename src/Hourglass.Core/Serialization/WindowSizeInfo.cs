namespace Hourglass.Serialization;

public enum WindowStateInfo
{
    Normal,
    Minimized,
    Maximized
}

public sealed class WindowSizeInfo
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public WindowStateInfo WindowState { get; set; }

    public WindowStateInfo RestoreWindowState { get; set; }

    public bool IsFullScreen { get; set; }
}
