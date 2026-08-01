namespace Hourglass.DemoRecorder;

public sealed record DemoRecorderOptions(
    string Scenario,
    string GifPath,
    string VideoPath,
    string FramesDirectory,
    int FrameRate,
    int Width,
    int Height,
    bool KeepFrames,
    bool SkipGif,
    bool SkipVideo,
    string FfmpegCommand)
{
    public const string DefaultScenario = "readme";
    public const int DefaultFrameRate = 12;
    public const int DefaultWidth = 960;
    public const int DefaultHeight = 540;

    public static DemoRecorderOptions Defaults(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return new DemoRecorderOptions(
            DefaultScenario,
            Path.Combine(repositoryRoot, "docs", "assets", "demo.gif"),
            Path.Combine(repositoryRoot, "docs", "assets", "demo.mp4"),
            Path.Combine(repositoryRoot, ".tmp", "readme-demo", "frames"),
            DefaultFrameRate,
            DefaultWidth,
            DefaultHeight,
            KeepFrames: false,
            SkipGif: false,
            SkipVideo: false,
            FfmpegCommand: "ffmpeg");
    }

    public static ParseOptionsResult Parse(string[] args, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(args);

        DemoRecorderOptions options = Defaults(repositoryRoot);
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return ParseOptionsResult.Help(CreateHelpText());
                case "--scenario":
                    options = options with { Scenario = ReadValue(args, ref index, arg) };
                    break;
                case "--gif":
                    options = options with { GifPath = ToAbsolutePath(repositoryRoot, ReadValue(args, ref index, arg)) };
                    break;
                case "--video":
                    options = options with { VideoPath = ToAbsolutePath(repositoryRoot, ReadValue(args, ref index, arg)) };
                    break;
                case "--frames-dir":
                    options = options with { FramesDirectory = ToAbsolutePath(repositoryRoot, ReadValue(args, ref index, arg)) };
                    break;
                case "--frame-rate":
                    if (!int.TryParse(ReadValue(args, ref index, arg), out int frameRate) || frameRate <= 0)
                    {
                        return ParseOptionsResult.Error("--frame-rate must be a positive integer.");
                    }

                    options = options with { FrameRate = frameRate };
                    break;
                case "--width":
                    if (!int.TryParse(ReadValue(args, ref index, arg), out int width) || width <= 0)
                    {
                        return ParseOptionsResult.Error("--width must be a positive integer.");
                    }

                    options = options with { Width = width };
                    break;
                case "--height":
                    if (!int.TryParse(ReadValue(args, ref index, arg), out int height) || height <= 0)
                    {
                        return ParseOptionsResult.Error("--height must be a positive integer.");
                    }

                    options = options with { Height = height };
                    break;
                case "--keep-frames":
                    options = options with { KeepFrames = true };
                    break;
                case "--skip-gif":
                    options = options with { SkipGif = true };
                    break;
                case "--skip-video":
                    options = options with { SkipVideo = true };
                    break;
                case "--ffmpeg":
                    options = options with { FfmpegCommand = ReadValue(args, ref index, arg) };
                    break;
                default:
                    return ParseOptionsResult.Error($"Unknown argument '{arg}'. Use --help for usage.");
            }
        }

        if (options.SkipGif && options.SkipVideo)
        {
            return ParseOptionsResult.Error("At least one output must be enabled; remove --skip-gif or --skip-video.");
        }

        if (!options.SkipVideo && (options.Width % 2 != 0 || options.Height % 2 != 0))
        {
            return ParseOptionsResult.Error("MP4 output requires even --width and --height values because yuv420p video cannot encode odd dimensions. Use even dimensions or pass --skip-video.");
        }

        if (!options.SkipGif
            && !options.SkipVideo
            && StringComparer.Ordinal.Equals(Path.GetFullPath(options.GifPath), Path.GetFullPath(options.VideoPath)))
        {
            return ParseOptionsResult.Error("--gif and --video must point to different files when both outputs are enabled.");
        }

        return ParseOptionsResult.Success(options);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static string ToAbsolutePath(string repositoryRoot, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repositoryRoot, path));
    }

    private static string CreateHelpText()
    {
        return """
        Usage: dotnet run --configuration Release --project tools/Hourglass.DemoRecorder/Hourglass.DemoRecorder.csproj -- [options]

        Options:
          --scenario readme
          --gif <path>
          --video <path>
          --frames-dir <path>
          --frame-rate <integer>
          --width <integer>
          --height <integer>
          --keep-frames
          --skip-gif
          --skip-video
          --ffmpeg <path-or-command>
          --help
        """;
    }
}

public sealed record ParseOptionsResult(
    bool IsSuccess,
    bool IsHelp,
    DemoRecorderOptions? Options,
    string? Message)
{
    public static ParseOptionsResult Success(DemoRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ParseOptionsResult(true, false, options, null);
    }

    public static ParseOptionsResult Help(string message)
    {
        return new ParseOptionsResult(false, true, null, message);
    }

    public static ParseOptionsResult Error(string message)
    {
        return new ParseOptionsResult(false, false, null, message);
    }
}
