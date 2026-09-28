namespace Hourglass.Cli;

using System.CommandLine;
using System.Collections.Immutable;
using Hourglass.Application;
using Hourglass.Settings;

public sealed class CliApplication(ICliRuntimeFactory runtimeFactory, TextWriter output, TextWriter error)
{
    private static readonly ImmutableHashSet<string> ReservedCommands = ImmutableHashSet.Create(StringComparer.Ordinal,
        "start", "list", "status", "pause", "resume", "stop", "restart", "dismiss", "version", "saved", "recent", "config", "update", "unlock", "gui", "tui", "doctor", "about");

    public async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Option<bool> json = new("--json") { Recursive = true };
        Option<bool> plain = new("--plain") { Recursive = true };
        RootCommand root = new("Hourglass scriptable CLI. This interim release supports an exclusive foreground runtime.");
        root.Options.Add(json);
        root.Options.Add(plain);
        string commandName = "help";
        CliOutput writer = new(output, error, CliOutputMode.Human);

        Command version = new("version", "Print the application version.");
        version.SetAction(_ => writer.Version(typeof(CliApplication).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"));
        root.Subcommands.Add(version);

        Command start = new("start", "Run a timer in the foreground until completion or interruption.");
        Argument<string[]> expression = new("expression") { Arity = ArgumentArity.OneOrMore };
        Option<string?> title = new("--title", "-t");
        Option<bool> wait = new("--wait");
        Option<bool> detach = new("--detach");
        start.Arguments.Add(expression);
        start.Options.Add(title);
        start.Options.Add(wait);
        start.Options.Add(detach);
        start.SetAction(async (parse, token) =>
        {
            if (parse.GetValue(detach))
            {
                return writer.Failure("start", new(parse.GetValue(wait) ? ApplicationErrorCode.Validation : ApplicationErrorCode.Unsupported,
                    parse.GetValue(wait) ? "--wait and --detach cannot be combined." : "Detached timers require the later shared host milestone."));
            }
            return await WithRuntimeAsync(CliRuntimePurpose.Foreground, async client =>
            {
                ApplicationResult<LinuxAppSettings> settings = await client.GetSettingsAsync(token).ConfigureAwait(false);
                if (settings is ApplicationResult<LinuxAppSettings>.Failure failed) { return writer.Failure("start", failed.Error); }
                LinuxAppSettings defaults = ((ApplicationResult<LinuxAppSettings>.Success)settings).Value;
                CreateSessionRequest request = new(Guid.NewGuid().ToString("N"), string.Join(' ', parse.GetValue(expression) ?? []).Trim(),
                    parse.GetValue(title) ?? string.Empty, TimerDefaults.FromSettings(defaults), ApplicationPreferences.FromSettings(defaults));
                ApplicationResult<ForegroundOutcome> result = await client.RunForegroundAsync(request, token).ConfigureAwait(false);
                return result switch
                {
                    ApplicationResult<ForegroundOutcome>.Success completed => writer.Sessions("start", [completed.Value.Session], completed.Value.Completion.ToString().ToLowerInvariant()),
                    ApplicationResult<ForegroundOutcome>.Failure foregroundFailed => writer.Failure("start", foregroundFailed.Error),
                    _ => throw new InvalidOperationException("Unknown application result.")
                };
            }, token).ConfigureAwait(false);
        });
        root.Subcommands.Add(start);

        Command list = new("list", "List live sessions; preserved recovery records are not live sessions.");
        list.SetAction((_, token) => WithRuntimeAsync(CliRuntimePurpose.Query, async client =>
        {
            ApplicationResult<ImmutableArray<TimerSessionSnapshot>> result = await client.ListSessionsAsync(token).ConfigureAwait(false);
            return result is ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success success
                ? writer.Sessions("list", success.Value) : writer.Failure("list", ((ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure)result).Error);
        }, token));
        root.Subcommands.Add(list);

        foreach (string name in new[] { "status", "pause", "resume", "stop", "restart", "dismiss" })
        {
            Command command = new(name, $"{name} a live session by its exact ID.");
            Argument<string> id = new("id");
            command.Arguments.Add(id);
            command.SetAction((parse, token) => WithRuntimeAsync(CliRuntimePurpose.Query, async client =>
            {
                string selected = parse.GetValue(id) ?? string.Empty;
                if (selected == "all" && name is "pause" or "resume" or "stop")
                {
                    SessionBatchCommand batch = name switch { "pause" => SessionBatchCommand.Pause, "resume" => SessionBatchCommand.Resume, _ => SessionBatchCommand.Stop };
                    ApplicationResult<ImmutableArray<TimerSessionSnapshot>> result = await client.ExecuteAllAsync(batch, token).ConfigureAwait(false);
                    if (result is ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure failed) { return writer.Failure(name, failed.Error); }
                    ApplicationResult<bool> durable = await client.FlushPersistenceAsync(token).ConfigureAwait(false);
                    return durable is ApplicationResult<bool>.Failure storage ? writer.Failure(name, storage.Error)
                        : writer.Sessions(name, ((ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success)result).Value);
                }
                ApplicationResult<TimerSessionSnapshot> single = name == "status"
                    ? await client.GetSessionAsync(selected, token).ConfigureAwait(false)
                    : await client.ExecuteAsync(name switch
                    {
                        "pause" => new SessionCommand.Pause(selected),
                        "resume" => new SessionCommand.Resume(selected),
                        "stop" => new SessionCommand.Stop(selected),
                        "restart" => new SessionCommand.Restart(selected),
                        _ => new SessionCommand.Dismiss(selected)
                    }, token).ConfigureAwait(false);
                if (single is ApplicationResult<TimerSessionSnapshot>.Failure missing) { return writer.Failure(name, missing.Error); }
                if (name != "status")
                {
                    ApplicationResult<bool> durable = await client.FlushPersistenceAsync(token).ConfigureAwait(false);
                    if (durable is ApplicationResult<bool>.Failure storage) { return writer.Failure(name, storage.Error); }
                }
                return writer.Sessions(name, [((ApplicationResult<TimerSessionSnapshot>.Success)single).Value]);
            }, token));
            root.Subcommands.Add(command);
        }

        string[] normalized = Normalize(arguments);
        ParseResult parsed = root.Parse(normalized);
        commandName = parsed.CommandResult.Command == root ? "help" : parsed.CommandResult.Command.Name;
        writer = new(output, error, parsed.GetValue(json) ? CliOutputMode.Json : parsed.GetValue(plain) ? CliOutputMode.Plain : CliOutputMode.Human);
        int titleOptions = 0;
        foreach (string token in OptionTokens(normalized))
        {
            string name = token.Split('=', 2)[0];
            if (name is "--title" or "-t") { titleOptions++; }
            if (name.StartsWith("-", StringComparison.Ordinal) && name is not ("--title" or "-t" or "--json" or "--plain" or "--wait" or "--detach" or "--help" or "-h" or "-?"))
            {
                return writer.Failure(commandName, new(ApplicationErrorCode.Validation, $"Unrecognized option: {name}"));
            }
        }
        if (titleOptions > 1) { return writer.Failure(commandName, new(ApplicationErrorCode.Validation, "The title option may only be specified once.")); }
        if (parsed.GetValue(json) && parsed.GetValue(plain)) { return writer.Failure(commandName, new(ApplicationErrorCode.Validation, "--json and --plain cannot be combined.")); }
        if (parsed.Errors.Count > 0) { return writer.Failure(commandName, new(ApplicationErrorCode.Validation, string.Join(" ", parsed.Errors.Select(failure => failure.Message)))); }
        try
        {
            return await parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return writer.Failure(commandName, new(ApplicationErrorCode.RuntimeUnavailable, "Foreground operation interrupted."), 130);
        }
        catch (Exception exception) { return writer.Failure(commandName, new(ApplicationErrorCode.InternalFailure, exception.Message)); }

        async Task<int> WithRuntimeAsync(CliRuntimePurpose purpose, Func<IHourglassClient, Task<int>> operation, CancellationToken token)
        {
            ApplicationResult<CliRuntimeLease> connected = await runtimeFactory.OpenAsync(purpose, token).ConfigureAwait(false);
            if (connected is ApplicationResult<CliRuntimeLease>.Failure failed) { return writer.Failure(commandName, failed.Error); }
            await using CliRuntimeLease lease = ((ApplicationResult<CliRuntimeLease>.Success)connected).Value;
            return await operation(lease.Client).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> OptionTokens(string[] arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            string value = arguments[index];
            if (value == "--") { yield break; }
            yield return value;
            if (value is "--title" or "-t") { index++; }
        }
    }

    private static string[] Normalize(string[] arguments)
    {
        if (arguments.Length == 0) { return ["--help"]; }
        for (int index = 0; index < arguments.Length; index++)
        {
            string value = arguments[index];
            if (value is "--help" or "-h" or "-?") { return arguments.ToArray(); }
            if (value == "--version") { return arguments.Select(argument => argument == "--version" ? "version" : argument).ToArray(); }
            if (value is "--json" or "--plain" or "--wait" or "--detach") { continue; }
            if (value is "--title" or "-t") { index++; continue; }
            if (ReservedCommands.Contains(value)) { return arguments.ToArray(); }
            return ["start", .. arguments];
        }
        return ["start", .. arguments];
    }
}
