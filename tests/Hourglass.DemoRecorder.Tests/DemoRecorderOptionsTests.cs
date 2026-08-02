using System.Runtime.InteropServices;
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
    public void ParseNormalizesRootedOutputPathsBeforeEncoding()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string rawPath = Path.Combine(this.temporaryDirectory, "assets", "..", "demo.gif");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                rawPath,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.Equal(Path.Combine(this.temporaryDirectory, "demo.gif"), result.Options.GifPath);
    }

    [Fact]
    public void ParseRejectsEnabledGifPathThatNamesExistingDirectory()
    {
        string outputDirectory = Path.Combine(this.temporaryDirectory, "gif-output");
        Directory.CreateDirectory(outputDirectory);

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                outputDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must point to files, not existing directories", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsEnabledVideoPathThatNamesExistingDirectory()
    {
        string outputDirectory = Path.Combine(this.temporaryDirectory, "video-output");
        Directory.CreateDirectory(outputDirectory);

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--video",
                outputDirectory,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must point to files, not existing directories", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsSkippedOutputPathThatNamesExistingDirectory()
    {
        string skippedOutputDirectory = Path.Combine(this.temporaryDirectory, "skipped-output");
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");
        Directory.CreateDirectory(skippedOutputDirectory);

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                skippedOutputDirectory,
                "--video",
                videoPath,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipGif);
        Assert.Equal(videoPath, result.Options.VideoPath);
    }

    [Fact]
    public void ParseRejectsEnabledGifPathWithTrailingDirectorySeparator()
    {
        string outputDirectory = Path.Combine(this.temporaryDirectory, "new-gif-output") + Path.DirectorySeparatorChar;

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                outputDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must point to files, not directories", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsEnabledVideoPathWithTrailingWindowsDirectorySeparator()
    {
        string outputDirectory = Path.Combine(this.temporaryDirectory, "new-video-output") + "\\";

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--video",
                outputDirectory,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must point to files, not directories", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsSkippedOutputPathWithTrailingDirectorySeparator()
    {
        string skippedOutputDirectory = Path.Combine(this.temporaryDirectory, "skipped-output") + Path.DirectorySeparatorChar;
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                skippedOutputDirectory,
                "--video",
                videoPath,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipGif);
        Assert.Equal(videoPath, result.Options.VideoPath);
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
    public void ParseRejectsLeafSymlinkOutputPathCollision()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string realPath = Path.Combine(this.temporaryDirectory, "real.mp4");
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias.gif");
        File.WriteAllText(realPath, "existing output");

        try
        {
            File.CreateSymbolicLink(aliasPath, realPath);
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
                aliasPath,
                "--video",
                realPath
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("--gif and --video must point to different files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsHardLinkedOutputPathCollision()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string realPath = Path.Combine(this.temporaryDirectory, "real.mp4");
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias.gif");
        File.WriteAllText(realPath, "existing output");

        if (!TryCreateHardLink(aliasPath, realPath))
        {
            return;
        }

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                aliasPath,
                "--video",
                realPath
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("--gif and --video must point to different files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOutputPathInsideGeneratedFrameSequence()
    {
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        string framePath = Path.Combine(framesDirectory, FrameRecorder.GetFrameFileName(0));

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                framePath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsFrameDirectoryNestedUnderEnabledOutputPath()
    {
        string outputPath = Path.Combine(this.temporaryDirectory, "result");
        string framesDirectory = Path.Combine(outputPath, "frames");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                outputPath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOutputPathInsideWideGeneratedFrameSequence()
    {
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        string framePath = Path.Combine(framesDirectory, "frame-100000.png");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                framePath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDanglingSymlinkOutputPathInsideGeneratedFrameSequence()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        Directory.CreateDirectory(framesDirectory);
        string framePath = Path.Combine(framesDirectory, FrameRecorder.GetFrameFileName(0));
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias.gif");

        try
        {
            File.CreateSymbolicLink(aliasPath, framePath);
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
                aliasPath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDanglingSymlinkChainOutputPathInsideGeneratedFrameSequence()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        Directory.CreateDirectory(framesDirectory);
        string framePath = Path.Combine(framesDirectory, FrameRecorder.GetFrameFileName(0));
        string secondLinkPath = Path.Combine(this.temporaryDirectory, "second-link");
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias.gif");

        try
        {
            File.CreateSymbolicLink(secondLinkPath, framePath);
            File.CreateSymbolicLink(aliasPath, secondLinkPath);
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
                aliasPath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOutputPathWhenDanglingSymlinkChainExceedsResolverLimit()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        Directory.CreateDirectory(framesDirectory);
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias-00.gif");

        try
        {
            for (int index = 0; index < 33; index++)
            {
                string linkPath = Path.Combine(this.temporaryDirectory, $"alias-{index:00}.gif");
                string targetPath = index == 32
                    ? Path.Combine(framesDirectory, FrameRecorder.GetFrameFileName(0))
                    : Path.Combine(this.temporaryDirectory, $"alias-{index + 1:00}.gif");
                File.CreateSymbolicLink(linkPath, targetPath);
            }
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
                aliasPath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("too many symbolic links", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOutputPathThroughDanglingDirectorySymlinkInsideGeneratedFrameSequence()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        string pendingDirectory = Path.Combine(this.temporaryDirectory, "pending");
        string aliasPath = Path.Combine(this.temporaryDirectory, "alias.gif");

        try
        {
            Directory.CreateSymbolicLink(pendingDirectory, framesDirectory);
            File.CreateSymbolicLink(aliasPath, Path.Combine(pendingDirectory, FrameRecorder.GetFrameFileName(0)));
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
                aliasPath,
                "--frames-dir",
                framesDirectory,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not point to generated frame files", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsSkippedOutputPathInsideGeneratedFrameSequence()
    {
        string framesDirectory = Path.Combine(this.temporaryDirectory, "frames");
        string framePath = Path.Combine(framesDirectory, FrameRecorder.GetFrameFileName(0));

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                framePath,
                "--video",
                Path.Combine(this.temporaryDirectory, "demo.mp4"),
                "--frames-dir",
                framesDirectory,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipGif);
    }

    [Fact]
    public void ParseRejectsEnabledOutputPathUnderDefaultFramesDirectory()
    {
        DemoRecorderOptions defaults = DemoRecorderOptions.Defaults(this.temporaryDirectory);
        string outputPath = Path.Combine(defaults.FramesDirectory, "archive.gif");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                outputPath,
                "--skip-video"
            ],
            this.temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not be inside the default frames directory", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsSkippedOutputPathUnderDefaultFramesDirectory()
    {
        DemoRecorderOptions defaults = DemoRecorderOptions.Defaults(this.temporaryDirectory);
        string skippedOutputPath = Path.Combine(defaults.FramesDirectory, "archive.gif");
        string videoPath = Path.Combine(this.temporaryDirectory, "docs", "assets", "demo.mp4");

        ParseOptionsResult result = DemoRecorderOptions.Parse(
            [
                "--gif",
                skippedOutputPath,
                "--video",
                videoPath,
                "--skip-gif"
            ],
            this.temporaryDirectory);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.SkipGif);
        Assert.Equal(videoPath, result.Options.VideoPath);
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

    private static bool TryCreateHardLink(string linkPath, string targetPath)
    {
        return OperatingSystem.IsLinux() && Link(targetPath, linkPath) == 0;
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string targetPath, string linkPath);
}
