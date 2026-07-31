using Hourglass.DemoRecorder.Services;
using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class HeadlessSmokeTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-headless-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task DemoApplicationRendersOneNonEmptyFrame()
    {
        HeadlessTestHost.EnsureStarted();
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
        {
            FramesDirectory = Path.Combine(this.temporaryDirectory, "frames"),
            Width = 320,
            Height = 180
        };
        var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
        recorder.PrepareEmptyDirectory();
        await using DemoContext context = await DemoContext.CreateAsync(options, recorder, new DemoPlatformServices());

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await context.ShowWindowAsync();
            await context.CaptureFrameAsync();
        });

        string framePath = Path.Combine(options.FramesDirectory, "frame-00000.png");
        var fileInfo = new FileInfo(framePath);
        Assert.True(fileInfo.Exists);
        Assert.True(fileInfo.Length > 0);
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(framePath);
        Assert.Equal(320, bitmap.PixelSize.Width);
        Assert.Equal(180, bitmap.PixelSize.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }
}
