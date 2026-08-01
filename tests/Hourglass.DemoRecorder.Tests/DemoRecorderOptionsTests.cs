using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class DemoRecorderOptionsTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-options-tests-{Guid.NewGuid():N}");

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

    [Fact]
    public void ParseRejectsCollidingOutputPathsWhenBothOutputsAreEnabled()
    {
        string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "demo-output");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            ["--gif", outputPath, "--video", outputPath],
            Directory.GetCurrentDirectory());

        Assert.False(result.IsSuccess);
        Assert.Contains("--gif and --video must point to different files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsNormalizedRelativeOutputPathCollision()
    {
        string repositoryRoot = Directory.GetCurrentDirectory();

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            ["--gif", "docs/assets/../assets/demo", "--video", "docs/assets/demo"],
            repositoryRoot);

        Assert.False(result.IsSuccess);
        Assert.Contains("--gif and --video must point to different files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsSymlinkedOutputPathCollision()
    {
        string realDirectory = Path.Combine(this.temporaryDirectory, "real");
        string aliasDirectory = Path.Combine(this.temporaryDirectory, "alias");
        Directory.CreateDirectory(realDirectory);

        try
        {
            Directory.CreateSymbolicLink(aliasDirectory, realDirectory);
        }
        catch (Exception exception) when (exception is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                Path.Combine(realDirectory, "demo"),
                "--video",
                Path.Combine(aliasDirectory, "demo")
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("--gif and --video must point to different files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsCollidingOutputPathWhenGifIsSkipped()
    {
        string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "demo-output");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            ["--gif", outputPath, "--video", outputPath, "--skip-gif"],
            Directory.GetCurrentDirectory());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipGif);
    }

    [Fact]
    public void ParseAllowsCollidingOutputPathWhenVideoIsSkipped()
    {
        string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "demo-output");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            ["--gif", outputPath, "--video", outputPath, "--skip-video"],
            Directory.GetCurrentDirectory());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipVideo);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }
}
