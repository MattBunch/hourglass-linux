namespace Hourglass.Cli;

using System.CommandLine;
using System.Diagnostics;
using Hourglass.Application;
using Hourglass.Linux.Services;
using Hourglass.Settings;

internal static class CliParity
{
    public static void Register(RootCommand root, Func<CliOutput> output,
        Func<ExclusiveRuntimePurpose, Func<IHourglassClient, Task<int>>, CancellationToken, Task<int>> withRuntime,
        Func<CancellationToken, Task<int>>? launchGui)
    {
        Command saved = new("saved", "Manage saved timers.");
        Command savedList = new("list");
        savedList.SetAction((_, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var result = await client.ListSavedTimersAsync(token).ConfigureAwait(false);
            return result is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure failed
                ? output().Failure("saved list", failed.Error) : SavedOutput("saved list", ((ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success)result).Value, output());
        }, token));
        saved.Subcommands.Add(savedList);

        Command add = new("add");
        Argument<string[]> expression = new("expression") { Arity = ArgumentArity.OneOrMore };
        Option<string?> title = new("--title", "-t");
        Option<string?> name = new("--name");
        Option<string[]> savedOptions = new("--set");
        add.Arguments.Add(expression);
        add.Options.Add(title);
        add.Options.Add(name);
        add.Options.Add(savedOptions);
        add.SetAction((parse, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var settings = await client.GetSettingsAsync(token).ConfigureAwait(false);
            if (settings is ApplicationResult<LinuxAppSettings>.Failure settingsFailure) { return output().Failure("saved add", settingsFailure.Error); }
            var applied = ApplyOptions(((ApplicationResult<LinuxAppSettings>.Success)settings).Value, parse.GetValue(savedOptions) ?? []);
            if (applied is ApplicationResult<LinuxAppSettings>.Failure invalid) { return output().Failure("saved add", invalid.Error); }
            SavedTimerDefinition timer = SavedTimerDefinition.Create(string.Join(' ', parse.GetValue(expression) ?? []), parse.GetValue(title) ?? string.Empty,
                ((ApplicationResult<LinuxAppSettings>.Success)applied).Value, parse.GetValue(name));
            var changed = await client.ChangeSavedTimersAsync(new SavedTimerChange.Add(timer), token).ConfigureAwait(false);
            if (changed is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure failure) { return output().Failure("saved add", failure.Error); }
            return await DurableAsync(client, "saved add", output(), token, () => SavedOutput("saved add", [timer], output())).ConfigureAwait(false);
        }, token));
        saved.Subcommands.Add(add);

        Command run = new("run");
        Argument<string?> selector = new("selector") { Arity = ArgumentArity.ZeroOrOne };
        Option<bool> all = new("--all");
        Option<bool> wait = new("--wait");
        run.Arguments.Add(selector);
        run.Options.Add(all);
        run.Options.Add(wait);
        run.SetAction((parse, token) =>
        {
            string? selected = parse.GetValue(selector);
            if (parse.GetValue(all) == (selected != null))
            {
                return Task.FromResult(output().Failure("saved run", new(ApplicationErrorCode.Validation, "Choose one saved timer or --all.")));
            }
            return withRuntime(ExclusiveRuntimePurpose.Sessions, async client =>
            {
                SavedTimerSelection choice = parse.GetValue(all) ? new SavedTimerSelection.All() : new SavedTimerSelection.ByNameOrId(selected!);
                var result = await client.RunSavedForegroundAsync(choice, token).ConfigureAwait(false);
                if (result is ApplicationResult<System.Collections.Immutable.ImmutableArray<ForegroundOutcome>>.Failure failed) { return output().Failure("saved run", failed.Error); }
                var outcomes = ((ApplicationResult<System.Collections.Immutable.ImmutableArray<ForegroundOutcome>>.Success)result).Value;
                return output().Data("saved run", new
                {
                    sessions = outcomes.Select(item => CliSession.FromSnapshot(item.Session)),
                    outcomes = outcomes.Select(item => item.Completion.ToString().ToLowerInvariant())
                },
                    outcomes.Select(item => $"{CliOutput.Escape(item.Session.SessionId)}\t{item.Completion.ToString().ToLowerInvariant()}"));
            }, token);
        });
        saved.Subcommands.Add(run);

        Command remove = new("remove");
        Argument<string> removeSelector = new("selector");
        remove.Arguments.Add(removeSelector);
        remove.SetAction((parse, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var timers = await client.ListSavedTimersAsync(token).ConfigureAwait(false);
            if (timers is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure failed) { return output().Failure("saved remove", failed.Error); }
            var selected = SavedTimerSelector.Resolve(((ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success)timers).Value,
                new SavedTimerSelection.ByNameOrId(parse.GetValue(removeSelector) ?? string.Empty));
            if (selected is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure invalid) { return output().Failure("saved remove", invalid.Error); }
            string id = ((ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success)selected).Value[0].Id;
            var changed = await client.ChangeSavedTimersAsync(new SavedTimerChange.Remove(id), token).ConfigureAwait(false);
            if (changed is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure failure) { return output().Failure("saved remove", failure.Error); }
            return await DurableAsync(client, "saved remove", output(), token, () => output().Data("saved remove", new { removedId = id }, [CliOutput.Escape(id)])).ConfigureAwait(false);
        }, token));
        saved.Subcommands.Add(remove);

        Command clear = new("clear");
        clear.SetAction((_, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var changed = await client.ChangeSavedTimersAsync(new SavedTimerChange.Clear(), token).ConfigureAwait(false);
            if (changed is ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Failure failed) { return output().Failure("saved clear", failed.Error); }
            return await DurableAsync(client, "saved clear", output(), token, () => output().Data("saved clear", new { cleared = true }, ["Saved timers cleared."])).ConfigureAwait(false);
        }, token));
        saved.Subcommands.Add(clear);
        root.Subcommands.Add(saved);

        Command recent = new("recent", "Recent timer expressions.");
        Command recentList = new("list");
        recentList.SetAction((_, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var values = await client.ListRecentInputsAsync(token).ConfigureAwait(false);
            return values is ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Failure failed
                ? output().Failure("recent list", failed.Error)
                : output().Data("recent list", new { inputs = ((ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Success)values).Value },
                    ((ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Success)values).Value.Select(CliOutput.Escape));
        }, token));
        recent.Subcommands.Add(recentList);
        Command recentClear = new("clear");
        recentClear.SetAction((_, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var result = await client.ClearRecentInputsAsync(token).ConfigureAwait(false);
            if (result is ApplicationResult<System.Collections.Immutable.ImmutableArray<string>>.Failure failed) { return output().Failure("recent clear", failed.Error); }
            return await DurableAsync(client, "recent clear", output(), token, () => output().Data("recent clear", new { cleared = true }, ["Recent inputs cleared."])).ConfigureAwait(false);
        }, token));
        recent.Subcommands.Add(recentClear);
        root.Subcommands.Add(recent);

        Command config = new("config", "Shared timer preferences.");
        Command configList = new("list");
        configList.SetAction((_, token) => ConfigAsync("config list", null, null, token));
        config.Subcommands.Add(configList);
        Command configGet = new("get");
        Argument<string> getKey = new("key");
        configGet.Arguments.Add(getKey);
        configGet.SetAction((parse, token) => ConfigAsync("config get", parse.GetValue(getKey), null, token));
        config.Subcommands.Add(configGet);
        Command configSet = new("set");
        Argument<string> setKey = new("key");
        Argument<string> setValue = new("value");
        configSet.Arguments.Add(setKey);
        configSet.Arguments.Add(setValue);
        configSet.SetAction((parse, token) => ConfigAsync("config set", parse.GetValue(setKey), parse.GetValue(setValue), token));
        config.Subcommands.Add(configSet);
        root.Subcommands.Add(config);

        Command update = new("update", "Edit an existing session.");
        Argument<string> sessionId = new("id");
        Option<string?> input = new("--input");
        Option<string?> newTitle = new("--title", "-t");
        Option<long?> revision = new("--revision");
        Option<string[]> changes = new("--set");
        update.Arguments.Add(sessionId);
        update.Options.Add(input);
        update.Options.Add(newTitle);
        update.Options.Add(revision);
        update.Options.Add(changes);
        update.SetAction((parse, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            string id = parse.GetValue(sessionId) ?? string.Empty;
            var existing = await client.GetSessionAsync(id, token).ConfigureAwait(false);
            if (existing is ApplicationResult<TimerSessionSnapshot>.Failure failed) { return output().Failure("update", failed.Error); }
            TimerSessionSnapshot current = ((ApplicationResult<TimerSessionSnapshot>.Success)existing).Value;
            string[] edits = parse.GetValue(changes) ?? [];
            ApplicationResult<LinuxAppSettings>? modified = edits.Length == 0 ? null : ApplyOptions(
                new LinuxSettingsSnapshot([], current.Preferences, current.Options).ToSettings(), edits);
            if (modified is ApplicationResult<LinuxAppSettings>.Failure invalid) { return output().Failure("update", invalid.Error); }
            LinuxAppSettings? settings = (modified as ApplicationResult<LinuxAppSettings>.Success)?.Value;
            var changed = await client.ExecuteAsync(new SessionCommand.Update(id, parse.GetValue(revision) ?? current.Revision,
                parse.GetValue(input), parse.GetValue(newTitle), settings == null ? null : TimerDefaults.FromSettings(settings),
                settings == null ? null : ApplicationPreferences.FromSettings(settings)), token).ConfigureAwait(false);
            if (changed is ApplicationResult<TimerSessionSnapshot>.Failure error) { return output().Failure("update", error.Error); }
            return await DurableAsync(client, "update", output(), token, () => output().Sessions("update", [((ApplicationResult<TimerSessionSnapshot>.Success)changed).Value])).ConfigureAwait(false);
        }, token));
        root.Subcommands.Add(update);

        Command unlock = new("unlock");
        Argument<string> unlockId = new("id");
        unlock.Arguments.Add(unlockId);
        unlock.SetAction((parse, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var result = await client.ExecuteAsync(new SessionCommand.Unlock(parse.GetValue(unlockId) ?? string.Empty), token).ConfigureAwait(false);
            if (result is ApplicationResult<TimerSessionSnapshot>.Failure failed) { return output().Failure("unlock", failed.Error); }
            return await DurableAsync(client, "unlock", output(), token, () => output().Sessions("unlock", [((ApplicationResult<TimerSessionSnapshot>.Success)result).Value])).ConfigureAwait(false);
        }, token));
        root.Subcommands.Add(unlock);

        Command sounds = new("sounds", "List or preview built-in alert sounds.");
        Command soundsList = new("list");
        soundsList.SetAction((_, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            var result = await client.ListSoundsAsync(token).ConfigureAwait(false);
            if (result is ApplicationResult<System.Collections.Immutable.ImmutableArray<SoundAvailability>>.Failure failed) { return output().Failure("sounds list", failed.Error); }
            var values = ((ApplicationResult<System.Collections.Immutable.ImmutableArray<SoundAvailability>>.Success)result).Value;
            return output().Data("sounds list", new { sounds = values }, values.Select(sound =>
                $"{CliOutput.Escape(sound.Id)}\t{CliOutput.Escape(sound.Name)}\t{(sound.Available ? "available" : "unavailable")}"));
        }, token));
        sounds.Subcommands.Add(soundsList);
        Command preview = new("preview");
        Argument<string> soundId = new("sound-id");
        preview.Arguments.Add(soundId);
        preview.SetAction((parse, token) => withRuntime(ExclusiveRuntimePurpose.Query, async client =>
        {
            string id = parse.GetValue(soundId) ?? string.Empty;
            var result = await client.PreviewSoundAsync(id, token).ConfigureAwait(false);
            return result is ApplicationResult<bool>.Failure failed ? output().Failure("sounds preview", failed.Error)
                : output().Data("sounds preview", new { soundId = id, completed = true }, [CliOutput.Escape(id)]);
        }, token));
        sounds.Subcommands.Add(preview);
        root.Subcommands.Add(sounds);

        Command doctor = new("doctor", "Inspect local terminal prerequisites.");
        doctor.SetAction(_ =>
        {
            IReadOnlyList<TerminalDiagnostic> checks = TerminalDiagnostics.Inspect();
            return output().Data("doctor", new { checks }, checks.Select(check =>
                $"{CliOutput.Escape(check.Name)}\t{(check.Available ? "available" : "unavailable")}\t{CliOutput.Escape(check.Detail)}"));
        });
        root.Subcommands.Add(doctor);

        Command about = new("about", "Show Hourglass version and interfaces.");
        about.SetAction(_ => output().Data("about", new
        {
            version = typeof(CliParity).Assembly.GetName().Version?.ToString(3) ?? "0.2.0",
            interfaces = new[] { "GUI", "CLI", "TUI" }
        }, ["Hourglass Linux: GUI, CLI and TUI"]));
        root.Subcommands.Add(about);

        Command gui = new("gui", "Launch or activate the graphical interface.");
        gui.SetAction(async (_, token) =>
        {
            try
            {
                int exit = await (launchGui ?? LaunchGuiAsync)(token).ConfigureAwait(false);
                return exit == 0 ? output().Data("gui", new { launched = true }, ["GUI launched."])
                    : output().Failure("gui", new(ApplicationErrorCode.RuntimeUnavailable, $"GUI launcher exited with code {exit}."));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return output().Failure("gui", new(ApplicationErrorCode.RuntimeUnavailable, "hourglass-linux executable was not found."));
            }
        });
        root.Subcommands.Add(gui);

        Task<int> ConfigAsync(string command, string? key, string? value, CancellationToken token) =>
            withRuntime(ExclusiveRuntimePurpose.Query, async client =>
            {
                var loaded = await client.GetSettingsAsync(token).ConfigureAwait(false);
                if (loaded is ApplicationResult<LinuxAppSettings>.Failure failed) { return output().Failure(command, failed.Error); }
                LinuxAppSettings settings = ((ApplicationResult<LinuxAppSettings>.Success)loaded).Value;
                if (command == "config set")
                {
                    var applied = SharedOptionRegistry.Apply(settings, [new(key ?? string.Empty, value ?? string.Empty)]);
                    if (applied is ApplicationResult<LinuxAppSettings>.Failure invalid) { return output().Failure(command, invalid.Error); }
                    var changed = await client.ChangeSettingsAsync(settings, ((ApplicationResult<LinuxAppSettings>.Success)applied).Value, token).ConfigureAwait(false);
                    if (changed is ApplicationResult<LinuxAppSettings>.Failure error) { return output().Failure(command, error.Error); }
                    settings = ((ApplicationResult<LinuxAppSettings>.Success)changed).Value;
                    var saved = await client.FlushPersistenceAsync(token).ConfigureAwait(false);
                    if (saved is ApplicationResult<bool>.Failure storage) { return output().Failure(command, storage.Error); }
                }
                if (key != null && SharedOptionRegistry.Get(settings, key) is not string entry)
                {
                    return output().Failure(command, new(ApplicationErrorCode.Validation, $"Unknown setting: {key}"));
                }
                var entries = (key == null ? SharedOptionRegistry.Keys : [key]).Select(item => new { key = item, value = SharedOptionRegistry.Get(settings, item) ?? string.Empty }).ToArray();
                return output().Data(command, new { settings = entries }, entries.Select(item => $"{item.key}\t{CliOutput.Escape(item.value)}"));
            }, token);
    }

    public static ApplicationResult<LinuxAppSettings> ApplyOptions(LinuxAppSettings settings, IEnumerable<string> overrides)
    {
        var edits = new List<KeyValuePair<string, string>>();
        foreach (string item in overrides)
        {
            int equals = item.IndexOf('=');
            if (equals <= 0) { return new ApplicationResult<LinuxAppSettings>.Failure(new(ApplicationErrorCode.Validation, "Options must use key=value.")); }
            edits.Add(new(item[..equals], item[(equals + 1)..]));
        }
        return SharedOptionRegistry.Apply(settings, edits);
    }

    private static int SavedOutput(string command, IEnumerable<SavedTimerDefinition> timers, CliOutput output)
    {
        SavedTimerDefinition[] values = timers.ToArray();
        return output.Data(command, new
        {
            savedTimers = values.Select(timer => new
            {
                id = timer.Id,
                name = timer.DisplayName,
                title = timer.TimerTitle,
                input = timer.TimerInput,
                options = timer.Options
            })
        }, values.Select(timer =>
                $"{CliOutput.Escape(timer.Id)}\t{CliOutput.Escape(timer.Header)}\t{CliOutput.Escape(timer.TimerInput)}"));
    }

    private static async Task<int> DurableAsync(IHourglassClient client, string command, CliOutput output,
        CancellationToken token, Func<int> success)
    {
        var persisted = await client.FlushPersistenceAsync(token).ConfigureAwait(false);
        return persisted is ApplicationResult<bool>.Failure failure ? output.Failure(command, failure.Error) : success();
    }

    private static Task<int> LaunchGuiAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sibling = Path.Combine(AppContext.BaseDirectory, "hourglass-linux");
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Release";
        string developmentBuild = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Hourglass.Linux.Avalonia", "bin", configuration, "net10.0", "hourglass-linux"));
        ProcessStartInfo start = new()
        {
            FileName = File.Exists(sibling) ? sibling : File.Exists(developmentBuild) ? developmentBuild : "hourglass-linux",
            UseShellExecute = false
        };
        using Process? launched = Process.Start(start);
        return Task.FromResult(launched == null ? 4 : 0);
    }
}
