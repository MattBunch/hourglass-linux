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

        await encoder.EncodeAsync(
            "ffmpeg",
            "frame-%05d.png",
            12,
            960,
            Path.Combine(this.temporaryDirectory, "demo.gif"),
            Path.Combine(this.temporaryDirectory, "demo.mp4"));

        Assert.Equal(3, runner.StartInfos.Count);
        Assert.Contains("palettegen=max_colors=128", string.Join(" ", runner.StartInfos[0].ArgumentList), StringComparison.Ordinal);
        Assert.Contains("paletteuse=dither=bayer:bayer_scale=4", string.Join(" ", runner.StartInfos[1].ArgumentList), StringComparison.Ordinal);
        Assert.Contains("libx264", runner.StartInfos[2].ArgumentList);
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
}
