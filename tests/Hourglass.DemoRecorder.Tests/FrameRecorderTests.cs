using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class FrameRecorderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-frame-tests-{Guid.NewGuid():N}");

    [Fact]
    public void FrameNamesAreStableAndZeroPadded()
    {
        Assert.Equal("frame-00000.png", FrameRecorder.GetFrameFileName(FrameRecorder.FirstFrameIndex));
        Assert.Equal("frame-00042.png", FrameRecorder.GetFrameFileName(42));
    }

    [Fact]
    public void InputPatternUsesDocumentedFfmpegSequence()
    {
        var recorder = new FrameRecorder(Path.Combine(this.temporaryDirectory, "frames with spaces"), 960, 540);

        Assert.EndsWith(Path.Combine("frames with spaces", "frame-%05d.png"), recorder.InputPattern, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareEmptyDirectoryRejectsUnrelatedFiles()
    {
        string frames = Path.Combine(this.temporaryDirectory, "frames");
        Directory.CreateDirectory(frames);
        File.WriteAllText(Path.Combine(frames, "keep.txt"), "do not overwrite");
        var recorder = new FrameRecorder(frames, 960, 540);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(recorder.PrepareEmptyDirectory);
        Assert.Contains("not empty", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }
}
