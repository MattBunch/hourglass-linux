namespace Hourglass.ReleaseTool;

internal sealed class ReleaseToolApplication
{
    private const string VersionCommand = "version";
    private const string ValidateVersionCommand = "validate-version";

    private readonly IProjectVersionReader projectVersionReader;
    private readonly IAppStreamMetadataReader appStreamMetadataReader;

    public ReleaseToolApplication(
        IProjectVersionReader? projectVersionReader = null,
        IAppStreamMetadataReader? appStreamMetadataReader = null)
    {
        this.projectVersionReader = projectVersionReader ?? new ProjectVersionReader();
        this.appStreamMetadataReader = appStreamMetadataReader ?? new AppStreamMetadata();
    }

    public async Task<int> RunAsync(string[] args, TextWriter standardOutput, TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (args.Length == 1 && IsHelpArgument(args[0]))
        {
            await standardOutput.WriteLineAsync(Usage()).ConfigureAwait(false);
            return 0;
        }

        if (args.Length == 0)
        {
            return await WriteUsageErrorAsync("A command is required.", standardError).ConfigureAwait(false);
        }

        string command = args[0];
        if (!StringComparer.Ordinal.Equals(command, VersionCommand) && !StringComparer.Ordinal.Equals(command, ValidateVersionCommand))
        {
            return await WriteUsageErrorAsync($"Unknown command: {command}", standardError).ConfigureAwait(false);
        }

        if (args.Length == 2 && IsHelpArgument(args[1]))
        {
            await standardOutput.WriteLineAsync(CommandUsage(command)).ConfigureAwait(false);
            return 0;
        }

        CommandParseResult parseResult = ParseOptions(command, args[1..]);
        if (!parseResult.IsSuccess || parseResult.Options == null)
        {
            return await WriteUsageErrorAsync(parseResult.Error!, standardError).ConfigureAwait(false);
        }

        try
        {
            ReleaseToolOptions options = parseResult.Options;
            string? repositoryRoot = null;
            string? projectPath = options.ProjectPath;
            string? metainfoPath = options.MetainfoPath;
            if (projectPath == null || (StringComparer.Ordinal.Equals(command, ValidateVersionCommand) && metainfoPath == null))
            {
                repositoryRoot = FindRepositoryRoot();
                if (repositoryRoot == null)
                {
                    await standardError.WriteLineAsync("Could not find the repository root containing Hourglass.Linux.sln. Supply explicit paths.").ConfigureAwait(false);
                    return 1;
                }
            }

            projectPath ??= Path.Combine(repositoryRoot!, "src", "Hourglass.Linux.Avalonia", "Hourglass.Linux.Avalonia.csproj");
            ProjectVersionReadResult projectResult = await this.projectVersionReader.ReadAsync(projectPath).ConfigureAwait(false);
            if (!projectResult.IsSuccess)
            {
                await standardError.WriteLineAsync(projectResult.Error).ConfigureAwait(false);
                return 1;
            }

            if (StringComparer.Ordinal.Equals(command, VersionCommand))
            {
                await standardOutput.WriteLineAsync(projectResult.Version).ConfigureAwait(false);
                return 0;
            }

            metainfoPath ??= Path.Combine(repositoryRoot!, "packaging", "linux", "io.github.MattBunch.Hourglass.metainfo.xml");
            AppStreamMetadataReadResult metadataResult = this.appStreamMetadataReader.Read(metainfoPath);
            if (!metadataResult.IsSuccess)
            {
                await standardError.WriteLineAsync(metadataResult.Error).ConfigureAwait(false);
                return 1;
            }

            ReleaseVersionValidationResult validationResult = ReleaseVersionValidator.Validate(projectResult.Version!, metadataResult.Version!, options.Tag);
            if (!validationResult.IsSuccess)
            {
                await standardError.WriteLineAsync(validationResult.Error).ConfigureAwait(false);
                return 1;
            }

            await standardOutput.WriteLineAsync($"Release version validation passed: {projectResult.Version}").ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            await standardError.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static CommandParseResult ParseOptions(string command, string[] arguments)
    {
        string? projectPath = null;
        string? metainfoPath = null;
        string? tag = null;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (IsHelpArgument(argument))
            {
                return CommandParseResult.Failure("Help must be the only command argument.");
            }

            bool isKnownOption = argument is "--project" ||
                (StringComparer.Ordinal.Equals(command, ValidateVersionCommand) && argument is "--metainfo" or "--tag");
            if (!isKnownOption)
            {
                return CommandParseResult.Failure($"Unknown option for {command}: {argument}");
            }

            if (index == arguments.Length - 1 || string.IsNullOrEmpty(arguments[index + 1]))
            {
                return CommandParseResult.Failure($"{argument} requires a value.");
            }

            string value = arguments[++index];
            switch (argument)
            {
                case "--project":
                    projectPath = value;
                    break;
                case "--metainfo" when StringComparer.Ordinal.Equals(command, ValidateVersionCommand):
                    metainfoPath = value;
                    break;
                case "--tag" when StringComparer.Ordinal.Equals(command, ValidateVersionCommand):
                    tag = value;
                    break;
                default:
                    return CommandParseResult.Failure($"Unknown option for {command}: {argument}");
            }
        }

        return CommandParseResult.Success(new ReleaseToolOptions(projectPath, metainfoPath, tag));
    }

    private static bool IsHelpArgument(string argument) => argument is "--help" or "-h";

    private static string? FindRepositoryRoot()
    {
        foreach (string startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
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
        }

        return null;
    }

    private static async Task<int> WriteUsageErrorAsync(string error, TextWriter standardError)
    {
        await standardError.WriteLineAsync(error).ConfigureAwait(false);
        await standardError.WriteLineAsync(Usage()).ConfigureAwait(false);
        return 2;
    }

    private static string Usage() => """
        Usage:
          Hourglass.ReleaseTool version [--project <project-file>]
          Hourglass.ReleaseTool validate-version [--project <project-file>] [--metainfo <metainfo-file>] [--tag <tag>]
        """;

    private static string CommandUsage(string command) => StringComparer.Ordinal.Equals(command, VersionCommand)
        ? "Hourglass.ReleaseTool version [--project <project-file>]"
        : "Hourglass.ReleaseTool validate-version [--project <project-file>] [--metainfo <metainfo-file>] [--tag <tag>]";

    private sealed record ReleaseToolOptions(string? ProjectPath, string? MetainfoPath, string? Tag);

    private sealed record CommandParseResult(ReleaseToolOptions? Options, string? Error)
    {
        public bool IsSuccess => Options != null;

        public static CommandParseResult Success(ReleaseToolOptions options) => new(options, null);

        public static CommandParseResult Failure(string error) => new(null, error);
    }
}
