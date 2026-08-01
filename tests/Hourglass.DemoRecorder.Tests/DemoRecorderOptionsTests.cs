using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class DemoRecorderOptionsTests
{
    [Fact]
    public void ParseRejectsOddWidthWhenVideoIsEnabled()
    {
        ParseOptionsResult result = DemoRecorderOptions.Parse(["--width", "961"], Directory.GetCurrentDirectory());

        Assert.False(result.IsSuccess);
        Assert.Contains("MP4 output requires even --width and --height values", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOddHeightWhenVideoIsEnabled()
    {
        ParseOptionsResult result = DemoRecorderOptions.Parse(["--height", "541"], Directory.GetCurrentDirectory());

        Assert.False(result.IsSuccess);
        Assert.Contains("MP4 output requires even --width and --height values", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsOddDimensionsForGifOnlyOutput()
    {
        ParseOptionsResult result = DemoRecorderOptions.Parse(
            ["--width", "961", "--height", "541", "--skip-video"],
            Directory.GetCurrentDirectory());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.Equal(961, result.Options.Width);
        Assert.Equal(541, result.Options.Height);
        Assert.True(result.Options.SkipVideo);
    }
}
