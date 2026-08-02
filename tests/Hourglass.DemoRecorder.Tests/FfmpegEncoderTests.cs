using System.Diagnostics;
using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class FfmpegEncoderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-ffmpeg-tests-{Guid.NewGuid():N}");

    [Fact]
    public void GifPaletteCommandUsesArgumentListAndQuotedPathsAreNotShellConcatenated()
    {
        ProcessStartInfo startInfo = FfmpegEncoder.CreatePaletteStartInfo(
            "ffmpeg",
            "/tmp/frames with spaces/frame-%05d.png",
            12,
            960,
            "/tmp/output palette.png");

        Assert.Equal("ffmpeg", startInfo.FileName);
        Assert.Contains("/tmp/frames with spaces/frame-%05d.png", startInfo.ArgumentList);
        Assert.Contains("scale=960:-1:flags=lanczos,palettegen=max_colors=128", startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void Mp4CommandUsesCompatibleH264Settings()
    {
        ProcessStartInfo startInfo = FfmpegEncoder.CreateMp4StartInfo("ffmpeg", "frame-%05d.png", 12, "demo.mp4");

        Assert.Contains("libx264", startInfo.ArgumentList);
        Assert.Contains("yuv420p", startInfo.ArgumentList);
        Assert.Contains("+faststart", startInfo.ArgumentList);
        AssertArgumentPair(startInfo, "-f", "mp4");
    }

    [Fact]
    public void GifCommandForcesGifMuxer()
    {
        ProcessStartInfo startInfo = FfmpegEncoder.CreateGifStartInfo(
            "ffmpeg",
            "frame-%05d.png",
            "palette.png",
            12,
            960,
            "demo");

        AssertArgumentPair(startInfo, "-f", "gif");
    }

    [Fact]
    public async Task NonZeroExitIncludesStderr()
    {
        var encoder = new FfmpegEncoder(new RecordingProcessRunner(exitCode: 1, stderr: "bad input", createOutputs: false));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            Path.Combine(this.temporaryDirectory, "demo.gif"),
            null));

        Assert.Contains("bad input", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingOutputFailsEvenWhenProcessSucceeds()
    {
        var encoder = new FfmpegEncoder(new RecordingProcessRunner(exitCode: 0, stderr: "", createOutputs: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            Path.Combine(this.temporaryDirectory, "demo.gif"),
            null));
    }

    [Fact]
    public async Task EncodeBuildsPaletteGifAndMp4Commands()
    {
        var runner = new RecordingProcessRunner(exitCode: 0, stderr: "", createOutputs: true);
        var encoder = new FfmpegEncoder(runner);
        string gifPath = Path.Combine(this.temporaryDirectory, "demo.gif");
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");

        await encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            gifPath,
            videoPath);

        Assert.Equal(3, runner.StartInfos.Count);
        Assert.Contains("palettegen=max_colors=128", string.Join(" ", runner.StartInfos[0].ArgumentList), StringComparison.Ordinal);
        Assert.Contains("paletteuse=dither=bayer:bayer_scale=4", string.Join(" ", runner.StartInfos[1].ArgumentList), StringComparison.Ordinal);
        AssertArgumentPair(runner.StartInfos[1], "-f", "gif");
        Assert.Contains("libx264", runner.StartInfos[2].ArgumentList);
        AssertArgumentPair(runner.StartInfos[2], "-f", "mp4");
        Assert.NotEqual(gifPath, runner.StartInfos[1].ArgumentList[^1]);
        Assert.NotEqual(videoPath, runner.StartInfos[2].ArgumentList[^1]);
        Assert.Equal("output", File.ReadAllText(gifPath));
        Assert.Equal("output", File.ReadAllText(videoPath));
        Assert.Empty(Directory.EnumerateFiles(this.temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public async Task EncodePreservesFinalOutputsWhenLaterPassFails()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string gifPath = Path.Combine(this.temporaryDirectory, "demo.gif");
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");
        File.WriteAllText(gifPath, "existing gif");
        File.WriteAllText(videoPath, "existing video");
        var runner = new SequencedProcessRunner([0, 0, 1], stderr: "video failed");
        var encoder = new FfmpegEncoder(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            gifPath,
            videoPath));

        Assert.Equal("existing gif", File.ReadAllText(gifPath));
        Assert.Equal("existing video", File.ReadAllText(videoPath));
        Assert.Empty(Directory.EnumerateFiles(this.temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public async Task EncodeRollsBackFirstPublishedOutputWhenSecondPublishFails()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string gifPath = Path.Combine(this.temporaryDirectory, "demo.gif");
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");
        File.WriteAllText(gifPath, "existing gif");
        File.WriteAllText(videoPath, "existing video");
        var runner = new RecordingProcessRunner(exitCode: 0, stderr: "", createOutputs: true);
        var fileOperations = new FailingPublishFileOperations(videoPath);
        var encoder = new FfmpegEncoder(runner, fileOperations);

        await Assert.ThrowsAsync<IOException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            gifPath,
            videoPath));

        Assert.Equal("existing gif", File.ReadAllText(gifPath));
        Assert.Equal("existing video", File.ReadAllText(videoPath));
        Assert.Empty(Directory.EnumerateFiles(this.temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public async Task EncodeAttemptsEveryBackupRestoreWhenOneRollbackRestoreFails()
    {
        Directory.CreateDirectory(this.temporaryDirectory);
        string gifPath = Path.Combine(this.temporaryDirectory, "demo.gif");
        string videoPath = Path.Combine(this.temporaryDirectory, "demo.mp4");
        File.WriteAllText(gifPath, "existing gif");
        File.WriteAllText(videoPath, "existing video");
        var runner = new RecordingProcessRunner(exitCode: 0, stderr: "", createOutputs: true);
        var fileOperations = new FailingRestoreFileOperations(videoPath);
        var encoder = new FfmpegEncoder(runner, fileOperations);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            gifPath,
            videoPath));

        Assert.Contains("rollback did not fully restore", exception.Message, StringComparison.Ordinal);
        Assert.Equal("existing gif", File.ReadAllText(gifPath));
        Assert.False(File.Exists(videoPath));
        Assert.Single(Directory.EnumerateFiles(this.temporaryDirectory, ".demo.mp4.hourglass-demo-backup-*.tmp"));
    }

    [Fact]
    public async Task EncodeDeletesPaletteWhenGifEncodingFails()
    {
        var runner = new SequencedProcessRunner([0, 1], stderr: "gif failed");
        var encoder = new FfmpegEncoder(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            Path.Combine(this.temporaryDirectory, "demo.gif"),
            null));

        string palettePath = runner.StartInfos[0].ArgumentList[^1];
        Assert.False(File.Exists(palettePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }

    private sealed class RecordingProcessRunner(int exitCode, string stderr, bool createOutputs) : IProcessRunner
    {
        public List<ProcessStartInfo> StartInfos { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            this.StartInfos.Add(startInfo);
            if (createOutputs && startInfo.ArgumentList.Count > 0)
            {
                string output = startInfo.ArgumentList[^1];
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, "output");
            }

            return Task.FromResult(new ProcessResult(exitCode, "", stderr));
        }
    }

    private sealed class SequencedProcessRunner(IReadOnlyList<int> exitCodes, string stderr) : IProcessRunner
    {
        private int index;

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            this.StartInfos.Add(startInfo);
            int exitCode = exitCodes[Math.Min(this.index, exitCodes.Count - 1)];
            this.index++;
            if (exitCode == 0 && startInfo.ArgumentList.Count > 0)
            {
                string output = startInfo.ArgumentList[^1];
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, "output");
            }

            return Task.FromResult(new ProcessResult(exitCode, "", stderr));
        }
    }

    private sealed class FailingPublishFileOperations(string failingFinalPath) : IFileOperations
    {
        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (StringComparer.Ordinal.Equals(destFileName, failingFinalPath)
                && sourceFileName.Contains(".hourglass-demo-", StringComparison.Ordinal)
                && !sourceFileName.Contains(".hourglass-demo-backup-", StringComparison.Ordinal))
            {
                throw new IOException("simulated publish failure");
            }

            File.Move(sourceFileName, destFileName, overwrite);
        }
    }

    private sealed class FailingRestoreFileOperations(string failingFinalPath) : IFileOperations
    {
        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (StringComparer.Ordinal.Equals(destFileName, failingFinalPath)
                && sourceFileName.Contains(".hourglass-demo-backup-", StringComparison.Ordinal))
            {
                throw new IOException("simulated restore failure");
            }

            if (StringComparer.Ordinal.Equals(destFileName, failingFinalPath)
                && sourceFileName.Contains(".hourglass-demo-", StringComparison.Ordinal)
                && !sourceFileName.Contains(".hourglass-demo-backup-", StringComparison.Ordinal))
            {
                throw new IOException("simulated publish failure");
            }

            File.Move(sourceFileName, destFileName, overwrite);
        }
    }

    private static void AssertArgumentPair(ProcessStartInfo startInfo, string option, string value)
    {
        int index = startInfo.ArgumentList.IndexOf(option);
        Assert.InRange(index, 0, startInfo.ArgumentList.Count - 2);
        Assert.Equal(value, startInfo.ArgumentList[index + 1]);
    }
}
