using System.Runtime.InteropServices;

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
    private const int MaxLinkResolutionDepth = 32;

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
        try
        {
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
        }
        catch (ArgumentException exception)
        {
            return ParseOptionsResult.Error(exception.Message);
        }

        if (options.SkipGif && options.SkipVideo)
        {
            return ParseOptionsResult.Error("At least one output must be enabled; remove --skip-gif or --skip-video.");
        }

        if (!options.SkipVideo && (options.Width % 2 != 0 || options.Height % 2 != 0))
        {
            return ParseOptionsResult.Error("MP4 output requires even --width and --height values because yuv420p video cannot encode odd dimensions. Use even dimensions or pass --skip-video.");
        }

        try
        {
            if (EnabledOutputIsDirectoryShaped(options))
            {
                return ParseOptionsResult.Error("Enabled output paths must point to files, not directories. Choose --gif and --video file paths or skip that output.");
            }

            if (EnabledOutputIsExistingDirectory(options))
            {
                return ParseOptionsResult.Error("Enabled output paths must point to files, not existing directories. Choose --gif and --video file paths or skip that output.");
            }

            if (DirectoryPathContainsDanglingSymbolicLink(options.FramesDirectory))
            {
                return ParseOptionsResult.Error("--frames-dir must not contain a dangling symbolic link. Create the symlink target directory or choose a real frames directory.");
            }

            if (DirectoryPathContainsNonDirectoryAncestor(options.FramesDirectory))
            {
                return ParseOptionsResult.Error("--frames-dir must not contain existing non-directory path components. Choose a real frames directory path.");
            }

            if (!options.SkipGif
                && !options.SkipVideo
                && StringComparer.Ordinal.Equals(CreateOutputPathIdentity(options.GifPath), CreateOutputPathIdentity(options.VideoPath)))
            {
                return ParseOptionsResult.Error("--gif and --video must point to different files when both outputs are enabled.");
            }

            if (EnabledOutputOverlapsFrameSequence(options))
            {
                return ParseOptionsResult.Error("Enabled output paths must not point to generated frame files. Choose --gif and --video paths outside --frames-dir.");
            }

            if (EnabledOutputContainsDanglingSymbolicLink(options))
            {
                return ParseOptionsResult.Error("Enabled output paths must not contain dangling symbolic links. Create the symlink target or choose a real output file path.");
            }

            if (EnabledOutputContainsNonDirectoryAncestor(options))
            {
                return ParseOptionsResult.Error("Enabled output parent directories must not contain existing non-directory path components. Choose a real output file path.");
            }

            if (EnabledOutputIsUnderDefaultFramesCleanupDirectory(options, repositoryRoot))
            {
                return ParseOptionsResult.Error("Enabled output paths must not be inside the default frames directory because it is cleared before recording. Choose --gif and --video paths outside .tmp/readme-demo/frames or pass --frames-dir.");
            }
        }
        catch (PathResolutionException exception)
        {
            return ParseOptionsResult.Error(exception.Message);
        }

        return ParseOptionsResult.Success(options);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || IsRecognizedOptionToken(args[index + 1]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static bool IsRecognizedOptionToken(string value)
    {
        return value is "--scenario"
            or "--gif"
            or "--video"
            or "--frames-dir"
            or "--frame-rate"
            or "--width"
            or "--height"
            or "--keep-frames"
            or "--skip-gif"
            or "--skip-video"
            or "--ffmpeg"
            or "--help"
            or "-h";
    }

    private static string ToAbsolutePath(string repositoryRoot, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));
    }

    private static string CreateOutputPathIdentity(string path)
    {
        string resolvedPath = CreateResolvedOutputPath(path);
        if (TryCreateExistingFileIdentity(resolvedPath, out string? identity) && identity is not null)
        {
            return identity;
        }

        return resolvedPath;
    }

    private static string CreateResolvedOutputPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return ResolveFilePath(fullPath);
        }

        string resolvedDirectory = ResolveDirectoryIdentity(directory);
        string resolvedPath = Path.Combine(resolvedDirectory, Path.GetFileName(fullPath));
        return ResolveFilePath(resolvedPath);
    }

    private static bool EnabledOutputIsExistingDirectory(DemoRecorderOptions options)
    {
        return (!options.SkipGif && Directory.Exists(CreateResolvedOutputPath(options.GifPath)))
            || (!options.SkipVideo && Directory.Exists(CreateResolvedOutputPath(options.VideoPath)));
    }

    private static bool EnabledOutputIsDirectoryShaped(DemoRecorderOptions options)
    {
        return (!options.SkipGif && IsDirectoryShapedPath(options.GifPath))
            || (!options.SkipVideo && IsDirectoryShapedPath(options.VideoPath));
    }

    private static bool IsDirectoryShapedPath(string path)
    {
        string trimmedPath = path.TrimEnd();
        if (trimmedPath.Length == 0)
        {
            return false;
        }

        char lastCharacter = trimmedPath[^1];
        return lastCharacter == Path.DirectorySeparatorChar
            || lastCharacter == Path.AltDirectorySeparatorChar
            || lastCharacter == '/'
            || lastCharacter == '\\';
    }

    private static string ResolveFilePath(string path)
    {
        return ResolveFilePath(path, [], depth: 0);
    }

    private static string ResolveFilePath(string path, HashSet<string> visitedPaths, int depth)
    {
        var info = new FileInfo(path);
        string fullPath = Path.GetFullPath(path);
        if (depth >= MaxLinkResolutionDepth)
        {
            throw new PathResolutionException($"Output path '{path}' contains too many symbolic links to resolve safely.");
        }

        if (!visitedPaths.Add(fullPath))
        {
            throw new PathResolutionException($"Output path '{path}' contains a symbolic link cycle.");
        }

        if (string.IsNullOrEmpty(info.LinkTarget))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            return string.IsNullOrWhiteSpace(directory)
                ? fullPath
                : Path.Combine(ResolveDirectoryIdentity(directory), Path.GetFileName(fullPath));
        }

        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: false);
        if (target != null)
        {
            return ResolveFilePath(target.FullName, visitedPaths, depth + 1);
        }

        string targetPath = Path.IsPathRooted(info.LinkTarget)
            ? info.LinkTarget
            : Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, info.LinkTarget);
        return ResolveFilePath(targetPath, visitedPaths, depth + 1);
    }

    private static bool EnabledOutputOverlapsFrameSequence(DemoRecorderOptions options)
    {
        string framesDirectory = ResolveDirectoryIdentity(options.FramesDirectory);
        return (!options.SkipGif && OutputOverlapsFrameSequence(options.GifPath, framesDirectory))
            || (!options.SkipVideo && OutputOverlapsFrameSequence(options.VideoPath, framesDirectory));
    }

    private static bool OutputOverlapsFrameSequence(string outputPath, string framesDirectory)
    {
        string resolvedOutputPath = CreateResolvedOutputPath(outputPath);
        return IsGeneratedFramePath(resolvedOutputPath, framesDirectory)
            || IsAncestorPath(resolvedOutputPath, framesDirectory)
            || HasGeneratedFramePathAncestor(resolvedOutputPath, framesDirectory);
    }

    private static bool HasGeneratedFramePathAncestor(string path, string framesDirectory)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(directory) && IsAncestorPath(framesDirectory, directory))
        {
            if (IsGeneratedFramePath(directory, framesDirectory))
            {
                return true;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static bool EnabledOutputIsUnderDefaultFramesCleanupDirectory(DemoRecorderOptions options, string repositoryRoot)
    {
        string framesDirectory = Path.GetFullPath(options.FramesDirectory);
        string defaultFramesDirectory = Path.GetFullPath(Defaults(repositoryRoot).FramesDirectory);
        if (!StringComparer.Ordinal.Equals(framesDirectory, defaultFramesDirectory))
        {
            return false;
        }

        return (!options.SkipGif && IsAncestorPath(framesDirectory, CreateResolvedOutputPath(options.GifPath)))
            || (!options.SkipVideo && IsAncestorPath(framesDirectory, CreateResolvedOutputPath(options.VideoPath)));
    }

    private static bool EnabledOutputContainsDanglingSymbolicLink(DemoRecorderOptions options)
    {
        return (!options.SkipGif && OutputPathContainsDanglingSymbolicLink(options.GifPath))
            || (!options.SkipVideo && OutputPathContainsDanglingSymbolicLink(options.VideoPath));
    }

    private static bool EnabledOutputContainsNonDirectoryAncestor(DemoRecorderOptions options)
    {
        return (!options.SkipGif && OutputPathContainsNonDirectoryAncestor(options.GifPath))
            || (!options.SkipVideo && OutputPathContainsNonDirectoryAncestor(options.VideoPath));
    }

    private static bool OutputPathContainsDanglingSymbolicLink(string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        return (!string.IsNullOrWhiteSpace(directory) && DirectoryPathContainsDanglingSymbolicLink(directory))
            || FileLinkIsDangling(fullPath);
    }

    private static bool OutputPathContainsNonDirectoryAncestor(string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        return !string.IsNullOrWhiteSpace(directory)
            && DirectoryPathContainsNonDirectoryAncestor(directory);
    }

    private static bool DirectoryPathContainsDanglingSymbolicLink(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        string relative = Path.GetRelativePath(root, fullDirectory);
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
            if (DirectoryLinkIsDangling(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DirectoryPathContainsNonDirectoryAncestor(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        string relative = Path.GetRelativePath(root, fullDirectory);
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
            if (File.Exists(current) && !Directory.Exists(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FileLinkIsDangling(string path)
    {
        var info = new FileInfo(path);
        if (string.IsNullOrEmpty(info.LinkTarget))
        {
            return false;
        }

        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true) is null;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static bool DirectoryLinkIsDangling(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (string.IsNullOrEmpty(info.LinkTarget))
        {
            return false;
        }

        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true) is null;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static bool IsGeneratedFramePath(string path, string framesDirectory)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return directory != null
            && StringComparer.Ordinal.Equals(ResolveDirectoryIdentity(directory), framesDirectory)
            && IsGeneratedFrameFileName(Path.GetFileName(path));
    }

    private static bool IsGeneratedFrameFileName(string fileName)
    {
        const string prefix = "frame-";
        const string suffix = ".png";
        const int minimumDigits = 5;
        int digitCount = fileName.Length - prefix.Length - suffix.Length;
        return digitCount >= minimumDigits
            && fileName.StartsWith(prefix, StringComparison.Ordinal)
            && fileName.EndsWith(suffix, StringComparison.Ordinal)
            && fileName.AsSpan(prefix.Length, digitCount).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool IsAncestorPath(string candidateAncestor, string path)
    {
        string ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateAncestor));
        string descendant = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string relativePath = Path.GetRelativePath(ancestor, descendant);
        return relativePath == "."
            || (!Path.IsPathRooted(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool TryCreateExistingFileIdentity(string path, out string? identity)
    {
        identity = null;
        if (!OperatingSystem.IsLinux() || !File.Exists(path))
        {
            return false;
        }

        if (Stat(path, out StatBuffer buffer) != 0)
        {
            return false;
        }

        identity = $"linux-file:{buffer.Device}:{buffer.Inode}";
        return true;
    }

    private static string ResolveDirectoryIdentity(string directory)
    {
        int depth = 0;
        string fullDirectory = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return fullDirectory;
        }

        string relative = Path.GetRelativePath(root, fullDirectory);
        if (relative == ".")
        {
            return root;
        }

        return ResolveDirectoryComponents(
            root,
            [],
            SplitPathComponents(relative),
            [],
            ref depth,
            directory);
    }

    private static string ResolveDirectoryComponents(
        string root,
        List<string> resolvedParts,
        IReadOnlyList<string> components,
        HashSet<string> activeLinks,
        ref int depth,
        string originalDirectory)
    {
        for (int index = 0; index < components.Count; index++)
        {
            string part = components[index];
            if (string.IsNullOrEmpty(part) || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (resolvedParts.Count > 0)
                {
                    resolvedParts.RemoveAt(resolvedParts.Count - 1);
                }

                continue;
            }

            string candidate = CombineRootAndParts(root, [.. resolvedParts, part]);
            var info = new DirectoryInfo(candidate);
            if (string.IsNullOrEmpty(info.LinkTarget))
            {
                resolvedParts.Add(part);
                continue;
            }

            string activeLinkState = CreateActiveLinkState(candidate, components, index + 1);
            if (!activeLinks.Add(activeLinkState))
            {
                throw new PathResolutionException($"Directory path '{originalDirectory}' contains a symbolic link cycle.");
            }

            if (depth >= MaxLinkResolutionDepth)
            {
                throw new PathResolutionException($"Directory path '{originalDirectory}' contains too many symbolic links to resolve safely.");
            }

            depth++;
            try
            {
                string targetRoot = root;
                List<string> targetBaseParts = [.. resolvedParts];
                string target = info.LinkTarget;
                if (Path.IsPathRooted(target))
                {
                    targetRoot = Path.GetPathRoot(target) ?? root;
                    targetBaseParts = [];
                    target = target[targetRoot.Length..];
                }

                string[] remainingComponents = [.. SplitPathComponents(target), .. components.Skip(index + 1)];
                return ResolveDirectoryComponents(
                    targetRoot,
                    targetBaseParts,
                    remainingComponents,
                    activeLinks,
                    ref depth,
                    originalDirectory);
            }
            finally
            {
                activeLinks.Remove(activeLinkState);
            }
        }

        return CombineRootAndParts(root, resolvedParts);
    }

    private static string CreateActiveLinkState(
        string linkPath,
        IReadOnlyList<string> components,
        int remainingStartIndex)
    {
        return string.Join('\0', [Path.GetFullPath(linkPath), .. components.Skip(remainingStartIndex)]);
    }

    private static string[] SplitPathComponents(string path)
    {
        return path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static string CombineRootAndParts(string root, IReadOnlyList<string> parts)
    {
        string current = root;
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
        }

        return Path.GetFullPath(current);
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int Stat(string path, out StatBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct StatBuffer
    {
        public readonly ulong Device;
        public readonly ulong Inode;
        public readonly ulong LinkCount;
        public readonly uint Mode;
        public readonly uint UserId;
        public readonly uint GroupId;
        public readonly int Padding;
        public readonly ulong DeviceId;
        public readonly long Size;
        public readonly long BlockSize;
        public readonly long Blocks;
        public readonly StatTimestamp AccessTime;
        public readonly StatTimestamp ModificationTime;
        public readonly StatTimestamp ChangeTime;
        public readonly long Reserved0;
        public readonly long Reserved1;
        public readonly long Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct StatTimestamp
    {
        public readonly long Seconds;
        public readonly long Nanoseconds;
    }

    private sealed class PathResolutionException(string message) : Exception(message);

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
