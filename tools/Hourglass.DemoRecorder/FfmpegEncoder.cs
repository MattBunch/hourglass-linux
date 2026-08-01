using System.Diagnostics;
using System.Text;

namespace Hourglass.DemoRecorder;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class FfmpegEncoder
{
    private readonly IProcessRunner processRunner;

    public FfmpegEncoder(IProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new SystemProcessRunner();
    }

    public async Task EncodeAsync(
        string ffmpegCommand,
        string inputPattern,
        int frameRate,
        int width,
        string? gifPath,
        string? videoPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPattern);

        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        string? palettePath = null;
        string? temporaryGifPath = null;
        string? temporaryVideoPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(gifPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gifPath)!);
                palettePath = Path.Combine(Path.GetTempPath(), $"hourglass-demo-palette-{Guid.NewGuid():N}.png");
                temporaryGifPath = CreateTemporaryOutputPath(gifPath);
                await this.RunCheckedAsync(CreatePaletteStartInfo(ffmpegCommand, inputPattern, frameRate, width, palettePath), palettePath, cancellationToken)
                    .ConfigureAwait(false);
                await this.RunCheckedAsync(CreateGifStartInfo(ffmpegCommand, inputPattern, palettePath, frameRate, width, temporaryGifPath), temporaryGifPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(videoPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(videoPath)!);
                temporaryVideoPath = CreateTemporaryOutputPath(videoPath);
                try
                {
                    await this.RunCheckedAsync(CreateMp4StartInfo(ffmpegCommand, inputPattern, frameRate, temporaryVideoPath), temporaryVideoPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (exception.Message.Contains("Unknown encoder 'libx264'", StringComparison.Ordinal))
                {
                    DeleteFileIfExists(temporaryVideoPath);
                    await this.RunCheckedAsync(CreateMp4StartInfo(ffmpegCommand, inputPattern, frameRate, temporaryVideoPath, "libopenh264"), temporaryVideoPath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            PublishSuccessfulOutputs(
                string.IsNullOrWhiteSpace(gifPath) ? null : new EncodedOutput(temporaryGifPath!, gifPath),
                string.IsNullOrWhiteSpace(videoPath) ? null : new EncodedOutput(temporaryVideoPath!, videoPath));
            temporaryGifPath = null;
            temporaryVideoPath = null;
        }
        finally
        {
            DeleteFileIfExists(palettePath);
            DeleteFileIfExists(temporaryGifPath);
            DeleteFileIfExists(temporaryVideoPath);
        }
    }

    public static ProcessStartInfo CreatePaletteStartInfo(
        string ffmpegCommand,
        string inputPattern,
        int frameRate,
        int width,
        string palettePath)
    {
        var startInfo = CreateBaseStartInfo(ffmpegCommand);
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(frameRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPattern);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"scale={width}:-1:flags=lanczos,palettegen=max_colors=128");
        startInfo.ArgumentList.Add(palettePath);
        return startInfo;
    }

    public static ProcessStartInfo CreateGifStartInfo(
        string ffmpegCommand,
        string inputPattern,
        string palettePath,
        int frameRate,
        int width,
        string gifPath)
    {
        var startInfo = CreateBaseStartInfo(ffmpegCommand);
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(frameRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPattern);
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(palettePath);
        startInfo.ArgumentList.Add("-lavfi");
        startInfo.ArgumentList.Add($"scale={width}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("gif");
        startInfo.ArgumentList.Add(gifPath);
        return startInfo;
    }

    public static ProcessStartInfo CreateMp4StartInfo(
        string ffmpegCommand,
        string inputPattern,
        int frameRate,
        string videoPath)
    {
        return CreateMp4StartInfo(ffmpegCommand, inputPattern, frameRate, videoPath, "libx264");
    }

    private static ProcessStartInfo CreateMp4StartInfo(
        string ffmpegCommand,
        string inputPattern,
        int frameRate,
        string videoPath,
        string codec)
    {
        var startInfo = CreateBaseStartInfo(ffmpegCommand);
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(frameRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPattern);
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add(codec);
        startInfo.ArgumentList.Add("-crf");
        startInfo.ArgumentList.Add("20");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("mp4");
        startInfo.ArgumentList.Add(videoPath);
        return startInfo;
    }

    private static string CreateTemporaryOutputPath(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath)!;
        string fileName = Path.GetFileName(finalPath);
        return Path.Combine(directory, $".{fileName}.hourglass-demo-{Guid.NewGuid():N}.tmp");
    }

    private static void PublishSuccessfulOutputs(params EncodedOutput?[] outputs)
    {
        foreach (EncodedOutput? output in outputs)
        {
            if (output is null)
            {
                continue;
            }

            File.Move(output.TemporaryPath, output.FinalPath, overwrite: true);
        }
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ProcessStartInfo CreateBaseStartInfo(string ffmpegCommand)
    {
        return new ProcessStartInfo
        {
            FileName = ffmpegCommand,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
    }

    private async Task RunCheckedAsync(ProcessStartInfo startInfo, string expectedOutput, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await this.processRunner.RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("FFmpeg was not found on PATH. Install ffmpeg and rerun ./scripts/record-readme-demo.sh.", exception);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed with exit code {result.ExitCode}:{Environment.NewLine}{result.StandardError}");
        }

        var fileInfo = new FileInfo(expectedOutput);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new InvalidOperationException($"FFmpeg completed but did not create a non-empty output file: {expectedOutput}{Environment.NewLine}{result.StandardError}");
        }
    }

    private sealed record EncodedOutput(string TemporaryPath, string FinalPath);
}
