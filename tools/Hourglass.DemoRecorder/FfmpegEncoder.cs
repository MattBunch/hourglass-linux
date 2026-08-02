using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
    private readonly IFileOperations fileOperations;

    public FfmpegEncoder(IProcessRunner? processRunner = null)
        : this(processRunner, new SystemFileOperations())
    {
    }

    internal FfmpegEncoder(IProcessRunner? processRunner, IFileOperations fileOperations)
    {
        ArgumentNullException.ThrowIfNull(fileOperations);

        this.processRunner = processRunner ?? new SystemProcessRunner();
        this.fileOperations = fileOperations;
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
        Exception? encodeFailure = null;
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
                    this.DeleteFileIfExists(temporaryVideoPath);
                    await this.RunCheckedAsync(CreateMp4StartInfo(ffmpegCommand, inputPattern, frameRate, temporaryVideoPath, "libopenh264"), temporaryVideoPath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            this.PublishSuccessfulOutputs(
                string.IsNullOrWhiteSpace(gifPath) ? null : new EncodedOutput(temporaryGifPath!, gifPath),
                string.IsNullOrWhiteSpace(videoPath) ? null : new EncodedOutput(temporaryVideoPath!, videoPath));
            temporaryGifPath = null;
            temporaryVideoPath = null;
        }
        catch (Exception exception)
        {
            encodeFailure = exception;
        }

        IReadOnlyList<Exception> cleanupFailures = this.CleanUpTemporaryFiles(palettePath, temporaryGifPath, temporaryVideoPath);
        if (encodeFailure is not null)
        {
            if (cleanupFailures.Count > 0)
            {
                List<Exception> failures = [encodeFailure, .. cleanupFailures];
                throw new AggregateException("Demo output encoding failed, and temporary output cleanup did not fully complete.", failures);
            }

            ExceptionDispatchInfo.Capture(encodeFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("Demo output encoding completed, but temporary output cleanup did not fully complete.", cleanupFailures);
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

    private void PublishSuccessfulOutputs(params EncodedOutput?[] outputs)
    {
        List<PublishedOutput> publishedOutputs = [];
        List<BackedUpOutput> backedUpOutputs = [];
        try
        {
            foreach (EncodedOutput? output in outputs)
            {
                if (output is null)
                {
                    continue;
                }

                string backupPath = CreateBackupOutputPath(output.FinalPath);
                if (this.fileOperations.Exists(output.FinalPath))
                {
                    this.fileOperations.Move(output.FinalPath, backupPath, overwrite: true);
                    backedUpOutputs.Add(new BackedUpOutput(output.FinalPath, backupPath));
                }

                this.fileOperations.Move(output.TemporaryPath, output.FinalPath, overwrite: false);
                publishedOutputs.Add(new PublishedOutput(output.FinalPath));
            }
        }
        catch (Exception publishException)
        {
            IReadOnlyList<Exception> rollbackFailures = this.RollBackPublishedOutputs(publishedOutputs, backedUpOutputs);
            if (rollbackFailures.Count > 0)
            {
                List<Exception> failures = [publishException, .. rollbackFailures];
                throw new AggregateException("Failed to publish encoded demo outputs, and rollback did not fully restore the previous outputs.", failures);
            }

            throw;
        }

        foreach (BackedUpOutput output in backedUpOutputs)
        {
            this.DeleteFileIfExists(output.BackupPath);
        }
    }

    private IReadOnlyList<Exception> RollBackPublishedOutputs(
        IEnumerable<PublishedOutput> publishedOutputs,
        IEnumerable<BackedUpOutput> backedUpOutputs)
    {
        List<Exception> failures = [];

        foreach (PublishedOutput output in publishedOutputs.Reverse())
        {
            try
            {
                this.DeleteFileIfExists(output.FinalPath);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Failed to delete partially published demo output '{output.FinalPath}'.", exception));
            }
        }

        foreach (BackedUpOutput output in backedUpOutputs.Reverse())
        {
            try
            {
                this.DeleteFileIfExists(output.FinalPath);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Failed to clear demo output path '{output.FinalPath}' before restoring its backup.", exception));
            }

            try
            {
                this.fileOperations.Move(output.BackupPath, output.FinalPath, overwrite: false);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Failed to restore previous demo output from '{output.BackupPath}' to '{output.FinalPath}'.", exception));
            }
        }

        return failures;
    }

    private static string CreateBackupOutputPath(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath)!;
        string fileName = Path.GetFileName(finalPath);
        return Path.Combine(directory, $".{fileName}.hourglass-demo-backup-{Guid.NewGuid():N}.tmp");
    }

    private void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && this.fileOperations.Exists(path))
        {
            this.fileOperations.Delete(path);
        }
    }

    private IReadOnlyList<Exception> CleanUpTemporaryFiles(params string?[] paths)
    {
        List<Exception> failures = [];
        foreach (string? path in paths)
        {
            try
            {
                this.DeleteFileIfExists(path);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Failed to delete temporary demo output file '{path}'.", exception));
            }
        }

        return failures;
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

    private sealed record BackedUpOutput(string FinalPath, string BackupPath);

    private sealed record PublishedOutput(string FinalPath);
}

internal interface IFileOperations
{
    bool Exists(string path);

    void Delete(string path);

    void Move(string sourceFileName, string destFileName, bool overwrite);
}

internal sealed class SystemFileOperations : IFileOperations
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
        File.Move(sourceFileName, destFileName, overwrite);
    }
}
