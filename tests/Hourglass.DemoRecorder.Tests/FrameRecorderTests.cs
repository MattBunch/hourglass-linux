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
        string unrelatedFile = Path.Combine(frames, "keep.txt");
        File.WriteAllText(unrelatedFile, "do not overwrite");
        var recorder = new FrameRecorder(frames, 960, 540);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(recorder.PrepareEmptyDirectory);
        Assert.Contains("not empty", exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(unrelatedFile));
    }

    [Fact]
    public void CleanupRecordedFramesRetainsPreExistingEmptyFramesDirectory()
    {
        string frames = Path.Combine(this.temporaryDirectory, "frames");
        Directory.CreateDirectory(frames);
        var recorder = new FrameRecorder(frames, 960, 540);
        recorder.PrepareEmptyDirectory();

        recorder.CleanupRecordedFrames();

        Assert.True(Directory.Exists(frames));
    }

    [Fact]
    public void ProgramDoesNotClearCustomFramesDirectoryBeforeRun()
    {
        string repositoryRoot = this.temporaryDirectory;
        string frames = Path.Combine(this.temporaryDirectory, "custom frames");
        Directory.CreateDirectory(frames);
        string unrelatedFile = Path.Combine(frames, "keep.txt");
        File.WriteAllText(unrelatedFile, "do not overwrite");
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(repositoryRoot) with
        {
            FramesDirectory = frames
        };

        Program.PrepareFramesDirectoryForRun(repositoryRoot, options);

        Assert.True(File.Exists(unrelatedFile));
        var recorder = new FrameRecorder(frames, 960, 540);
        Assert.Throws<InvalidOperationException>(recorder.PrepareEmptyDirectory);
    }

    [Fact]
    public void ProgramRejectsDefaultFramesDirectoryWithSymlinkedAncestor()
    {
        string repositoryRoot = Path.Combine(this.temporaryDirectory, "repo");
        string externalRoot = Path.Combine(this.temporaryDirectory, "external");
        string defaultRoot = Path.Combine(repositoryRoot, ".tmp", "readme-demo");
        string externalFrames = Path.Combine(externalRoot, "frames");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".tmp"));
        Directory.CreateDirectory(externalFrames);
        string unrelatedFile = Path.Combine(externalFrames, "keep.txt");
        File.WriteAllText(unrelatedFile, "do not overwrite");

        try
        {
            Directory.CreateSymbolicLink(defaultRoot, externalRoot);
        }
        catch (Exception exception) when (exception is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        DemoRecorderOptions options = DemoRecorderOptions.Defaults(repositoryRoot);

        InvalidOperationException cleanupException = Assert.Throws<InvalidOperationException>(() => Program.PrepareFramesDirectoryForRun(repositoryRoot, options));

        Assert.Contains("symlinked ancestor", cleanupException.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(unrelatedFile));
    }

    [Fact]
    public void ProgramRejectsDefaultFramesDirectoryWithSymlinkedAncestorBeforeFramesDirectoryExists()
    {
        string repositoryRoot = Path.Combine(this.temporaryDirectory, "repo");
        string externalRoot = Path.Combine(this.temporaryDirectory, "external");
        string repositoryTemporaryRoot = Path.Combine(repositoryRoot, ".tmp");
        Directory.CreateDirectory(repositoryRoot);
        Directory.CreateDirectory(externalRoot);

        try
        {
            Directory.CreateSymbolicLink(repositoryTemporaryRoot, externalRoot);
        }
        catch (Exception exception) when (exception is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        DemoRecorderOptions options = DemoRecorderOptions.Defaults(repositoryRoot);

        InvalidOperationException cleanupException = Assert.Throws<InvalidOperationException>(() => Program.PrepareFramesDirectoryForRun(repositoryRoot, options));

        Assert.Contains("symlinked ancestor", cleanupException.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(externalRoot, "readme-demo", "frames")));
    }

    [Fact]
    public void ProgramRejectsExplicitNonExecutableFfmpegFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string ffmpegPath = Path.Combine(this.temporaryDirectory, "ffmpeg");
        Directory.CreateDirectory(this.temporaryDirectory);
        File.WriteAllText(ffmpegPath, "not executable");

        try
        {
            File.SetUnixFileMode(ffmpegPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        Assert.False(Program.IsExecutableAvailable(ffmpegPath));
    }

    [Fact]
    public void ProgramRejectsFfmpegFileNotExecutableByCurrentUser()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string ffmpegPath = Path.Combine(this.temporaryDirectory, "ffmpeg");
        Directory.CreateDirectory(this.temporaryDirectory);
        File.WriteAllText(ffmpegPath, "#!/usr/bin/env sh\nexit 0\n");

        try
        {
            File.SetUnixFileMode(
                ffmpegPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherExecute);
        }
        catch (Exception exception) when (exception is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        Assert.False(Program.IsExecutableAvailable(ffmpegPath));
    }

    [Fact]
    public void WrapperPreflightUsesForwardedFfmpegCommand()
    {
        string script = File.ReadAllText(FindRepositoryFile("scripts/record-readme-demo.sh"));

        Assert.Contains("ffmpeg_command=\"${args[$next_index]}\"", script, StringComparison.Ordinal);
        Assert.Contains("command -v \"$ffmpeg_command\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("command -v ffmpeg", script, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }
}
