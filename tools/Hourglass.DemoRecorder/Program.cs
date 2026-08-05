using System.Globalization;
using Avalonia;
using Hourglass.DemoRecorder.Scenarios;
using Hourglass.DemoRecorder.Services;

namespace Hourglass.DemoRecorder;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        ParseOptionsResult parseResult;
        try
        {
            parseResult = DemoRecorderOptions.Parse(args, repositoryRoot);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        if (parseResult.IsHelp)
        {
            Console.WriteLine(parseResult.Message);
            return 0;
        }

        if (!parseResult.IsSuccess || parseResult.Options == null)
        {
            Console.Error.WriteLine(parseResult.Message);
            return 2;
        }

        DemoRecorderOptions options = parseResult.Options;
        IDemoScenario? scenario = CreateScenario(options.Scenario);
        if (scenario == null)
        {
            Console.Error.WriteLine($"Unknown demo scenario '{options.Scenario}'. Available scenarios: readme.");
            return 2;
        }

        if (!options.SkipGif || !options.SkipVideo)
        {
            if (!IsExecutableAvailable(options.FfmpegCommand))
            {
                Console.Error.WriteLine($"FFmpeg command '{options.FfmpegCommand}' was not found or is not executable. Install ffmpeg and rerun ./scripts/record-readme-demo.sh.");
                return 1;
            }
        }

        FrameRecorder? frameRecorder = null;
        int exitCode;
        try
        {
            PrepareFramesDirectoryForRun(repositoryRoot, options);
            frameRecorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
            frameRecorder.PrepareEmptyDirectory();
            DemoAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

            var services = new DemoPlatformServices();
            await using (DemoContext context = await DemoContext.CreateAsync(options, frameRecorder, services).ConfigureAwait(true))
            {
                var runner = new DemoScenarioRunner(scenario, context);
                Console.WriteLine("Rendering README demo frames...");
                int frameCount = await runner.RunAsync().ConfigureAwait(true);
                Console.WriteLine($"Rendered {frameCount} frames.");
            }

            var encoder = new FfmpegEncoder();
            string? gifPath = options.SkipGif ? null : options.GifPath;
            string? videoPath = options.SkipVideo ? null : options.VideoPath;
            if (gifPath != null)
            {
                Console.WriteLine($"Encoding {Path.GetRelativePath(repositoryRoot, gifPath)}...");
            }

            if (videoPath != null)
            {
                Console.WriteLine($"Encoding {Path.GetRelativePath(repositoryRoot, videoPath)}...");
            }

            await encoder.EncodeAsync(
                options.FfmpegCommand,
                frameRecorder.InputPattern,
                options.FrameRate,
                options.Width,
                gifPath,
                videoPath).ConfigureAwait(false);

            Console.WriteLine("Created:");
            if (gifPath != null)
            {
                Console.WriteLine($"  {Path.GetRelativePath(repositoryRoot, gifPath)}");
            }

            if (videoPath != null)
            {
                Console.WriteLine($"  {Path.GetRelativePath(repositoryRoot, videoPath)}");
            }

            exitCode = 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Recording was canceled.");
            exitCode = 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            exitCode = 1;
        }

        if (!options.KeepFrames)
        {
            CleanupFramesDirectoryForRun(frameRecorder);
        }

        return exitCode;
    }

    internal static void PrepareFramesDirectoryForRun(string repositoryRoot, DemoRecorderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(options);

        string defaultFramesDirectory = DemoRecorderOptions.Defaults(repositoryRoot).FramesDirectory;
        if (Path.GetFullPath(options.FramesDirectory) == Path.GetFullPath(defaultFramesDirectory))
        {
            if (PathContainsSymlink(defaultFramesDirectory))
            {
                throw new InvalidOperationException("The default frames directory has a symlinked ancestor. Remove the symlink or choose a safe --frames-dir path before recording.");
            }

            if (Directory.Exists(options.FramesDirectory))
            {
                Directory.Delete(options.FramesDirectory, recursive: true);
            }
        }
    }

    private static bool PathContainsSymlink(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            return false;
        }

        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(part) || part == ".")
            {
                continue;
            }

            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (!string.IsNullOrEmpty(info.LinkTarget))
            {
                return true;
            }
        }

        return false;
    }

    private static void CleanupFramesDirectoryForRun(FrameRecorder? frameRecorder)
    {
        frameRecorder?.CleanupRecordedFrames();
    }

    private static IDemoScenario? CreateScenario(string name)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(name, DemoRecorderOptions.DefaultScenario)
            ? new ReadmeDemoScenario()
            : null;
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hourglass.Linux.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    internal static bool IsExecutableAvailable(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return IsExecutableFile(command);
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(IsExecutableFile);
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path) || Directory.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            return ProbeExecutable(path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool ProbeExecutable(string path)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add("-version");

            if (!process.Start())
            {
                return false;
            }

            const int preflightTimeoutMilliseconds = 5000;
            if (!process.WaitForExit(preflightTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
