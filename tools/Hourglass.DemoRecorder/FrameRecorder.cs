using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Hourglass.DemoRecorder;

public sealed class FrameRecorder
{
    public const int FirstFrameIndex = 0;
    public const string FrameFilePattern = "frame-{0:D5}.png";

    private readonly string framesDirectory;
    private readonly int width;
    private readonly int height;
    private int nextFrameIndex;

    public FrameRecorder(string framesDirectory, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(framesDirectory);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        this.framesDirectory = framesDirectory;
        this.width = width;
        this.height = height;
    }

    public int FrameCount => this.nextFrameIndex;

    public string InputPattern => Path.Combine(this.framesDirectory, "frame-%05d.png");

    public static string GetFrameFileName(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, FrameFilePattern, index);
    }

    public void PrepareEmptyDirectory()
    {
        Directory.CreateDirectory(this.framesDirectory);
        if (Directory.EnumerateFileSystemEntries(this.framesDirectory).Any())
        {
            throw new InvalidOperationException($"Frames directory '{this.framesDirectory}' is not empty.");
        }
    }

    public string Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Width = this.width;
        window.Height = this.height;
        window.Position = new PixelPoint(0, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using Avalonia.Media.Imaging.WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame == null)
        {
            throw new InvalidOperationException("Avalonia headless did not return a rendered frame.");
        }

        string path = Path.Combine(this.framesDirectory, GetFrameFileName(this.nextFrameIndex));
        if (File.Exists(path))
        {
            throw new IOException($"Frame file already exists: {path}");
        }

        Directory.CreateDirectory(this.framesDirectory);
        frame.Save(path);
        this.nextFrameIndex++;
        return path;
    }
}
