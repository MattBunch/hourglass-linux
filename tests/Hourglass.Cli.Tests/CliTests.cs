namespace Hourglass.Cli.Tests;

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Hourglass.Application;
using Hourglass.Linux.Services;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class CliTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0);

    [Fact]
    public async Task TuiLauncherPropagatesExitWithoutOpeningRuntime()
    {
        Factory factory = new(null);
        int launches = 0;
        using StringWriter output = new();
        using StringWriter error = new();
        CliApplication cli = new(factory, output, error, _ => { launches++; return Task.FromResult(7); });
        Assert.Equal(7, await cli.RunAsync(["tui"]));
        Assert.Equal(1, launches);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(2, await cli.RunAsync(["tui", "--json"]));
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task GuiLauncherUsesInjectedProcessWithoutOpeningRuntime()
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        CliApplication cli = new(factory, output, error, launchGui: _ => Task.FromResult(0));
        Assert.Equal(0, await cli.RunAsync(["gui", "--json"]));
        Assert.Equal(0, factory.Opens);
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.True(result.RootElement.GetProperty("result").GetProperty("launched").GetBoolean());
    }

    [Fact]
    public async Task CatalogConfigAndUpdateUseSharedRuntime()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        using StringWriter output = new();
        using StringWriter error = new();
        CliApplication cli = new(new Factory(runtime), output, error);
        Assert.Equal(0, await cli.RunAsync(["config", "set", "loop-timer", "true", "--json"]));
        Assert.True(Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(await runtime.GetSettingsAsync()).Value.LoopTimer);
        Assert.Equal(0, await cli.RunAsync(["saved", "add", "0 seconds", "--title", "Tea", "--name", "Break", "--set", "loop-timer=false", "--json"]));
        var timers = Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success>(await runtime.ListSavedTimersAsync()).Value;
        Assert.False(Assert.Single(timers).Options.LoopTimer);
        Assert.Equal(0, await cli.RunAsync(["saved", "list", "--json"]));
        Assert.Equal(0, await cli.RunAsync(["saved", "run", "Break", "--json"]));
        Assert.Equal(0, await cli.RunAsync(["saved", "run", "--all", "--json"]));
        await runtime.CreateSessionAsync(new("edit", "25 minutes", "Focus", new(), new()));
        Assert.Equal(0, await cli.RunAsync(["update", "edit", "--title", "Revised", "--set", "show-time-elapsed=true", "--json"]));
        Assert.True(Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("edit")).Value.Options.ShowTimeElapsed);
        Assert.Equal(0, await cli.RunAsync(["saved", "remove", "Break", "--json"]));
        Assert.Empty(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<SavedTimerDefinition>>.Success>(await runtime.ListSavedTimersAsync()).Value);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task DiagnosticsAndAboutDoNotOpenRuntimeAndInvalidOptionsDoNotMutate()
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        CliApplication cli = new(factory, output, error);
        Assert.Equal(0, await cli.RunAsync(["doctor", "--json"]));
        Assert.Equal(0, await cli.RunAsync(["about", "--json"]));
        Assert.Equal(0, factory.Opens);
        await using HourglassRuntime runtime = Runtime();
        CliApplication mutable = new(new Factory(runtime), output, error);
        Assert.Equal(2, await mutable.RunAsync(["config", "set", "loop-timer", "not-a-boolean", "--json"]));
        Assert.False(Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(await runtime.GetSettingsAsync()).Value.LoopTimer);
    }

    [Fact]
    public async Task RepeatedOptionOverridesApplyToOneForegroundSession()
    {
        await using HourglassRuntime runtime = Runtime();
        using StringWriter output = new();
        using StringWriter error = new();
        CliApplication cli = new(new Factory(runtime), output, error);
        Assert.Equal(0, await cli.RunAsync(["start", "0 seconds", "--set", "notifications-enabled=false",
            "--set", "show-time-elapsed=true", "--json"]));
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        JsonElement session = result.RootElement.GetProperty("result").GetProperty("sessions")[0];
        Assert.False(session.GetProperty("preferences").GetProperty("notificationsEnabled").GetBoolean());
        Assert.True(session.GetProperty("options").GetProperty("showTimeElapsed").GetBoolean());
    }

    [Theory]
    [InlineData("start", "0 seconds")]
    [InlineData("0 seconds")]
    [InlineData("0", "seconds")]
    [InlineData("start", "0 seconds", "--wait")]
    [InlineData("--", "0 seconds")]
    public async Task ExplicitAndShorthandStartsReturnOneJsonResult(params string[] args)
    {
        await using HourglassRuntime runtime = Runtime();
        Factory factory = new(runtime);
        using StringWriter output = new();
        using StringWriter error = new();
        int exit = await new CliApplication(factory, output, error).RunAsync(["--json", .. args]);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("start", document.RootElement.GetProperty("command").GetString());
        JsonElement result = document.RootElement.GetProperty("result");
        Assert.Equal("expired", result.GetProperty("outcome").GetString());
        Assert.Equal("0 seconds", result.GetProperty("sessions")[0].GetProperty("input").GetString());
        Assert.Equal(ExclusiveRuntimePurpose.Sessions, factory.Purpose);
        Assert.Equal(1, factory.Disposals);
    }

    [Theory]
    [InlineData("--title")]
    [InlineData("-t")]
    public async Task TitleAliasesPreserveUnicodeAndControlCharacters(string option)
    {
        await using HourglassRuntime runtime = Runtime();
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, await new CliApplication(new Factory(runtime), output, error).RunAsync([option, "Tea ☕\n\t\u001b", "0 seconds", "--plain"]));
        Assert.Contains("Tea ☕\\n\\t\\u001b", output.ToString());
        Assert.DoesNotContain('\u001b', output.ToString());
        Assert.DoesNotContain("Tea ☕\n", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("version")]
    public async Task HelpAndVersionNeverAcquireAuthority(string argument)
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, await new CliApplication(factory, output, error).RunAsync([argument]));
        Assert.NotEmpty(output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task NoArgumentsPrintsHelpWithoutRuntime()
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, await new CliApplication(factory, output, error).RunAsync([]));
        Assert.Contains("start", output.ToString());
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("start")]
    [InlineData("--unknown")]
    [InlineData("saved")]
    public async Task UsageErrorsProduceStructuredStderrWithoutOpeningRuntime(string argument)
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(2, await new CliApplication(factory, output, error).RunAsync([argument, "--json"]));
        Assert.Equal(string.Empty, output.ToString());
        using JsonDocument failure = JsonDocument.Parse(error.ToString());
        Assert.Equal(2, failure.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task RepeatedTitleAndMissingTitleValuesAreUsageErrors()
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        var cli = new CliApplication(factory, output, error);
        Assert.Equal(2, await cli.RunAsync(["start", "0 seconds", "-t", "a", "--title", "b", "--json"]));
        Assert.Equal(2, await cli.RunAsync(["start", "0 seconds", "--title"]));
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 2)]
    public async Task DetachedModeIsRejectedBeforeRuntime(bool wait, int expected)
    {
        Factory factory = new(null);
        using StringWriter output = new();
        using StringWriter error = new();
        string[] args = wait ? ["start", "25m", "--detach", "--wait", "--json"] : ["start", "25m", "--detach", "--json"];
        Assert.Equal(expected, await new CliApplication(factory, output, error).RunAsync(args));
        Assert.Equal(0, factory.Opens);
        Assert.Equal(string.Empty, output.ToString());
        using JsonDocument document = JsonDocument.Parse(error.ToString());
        Assert.Equal(expected, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task OutputFlagsAreExclusiveAndTitleValuesAreNotGlobalOptions()
    {
        await using HourglassRuntime runtime = Runtime();
        Factory factory = new(runtime);
        using StringWriter output = new();
        using StringWriter error = new();
        var cli = new CliApplication(factory, output, error);
        Assert.Equal(2, await cli.RunAsync(["list", "--plain", "--json"]));
        Assert.Equal(0, factory.Opens);
        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        Assert.Equal(0, await cli.RunAsync(["--title", "--json", "0 seconds"]));
        Assert.Contains("--json", output.ToString());
        Assert.False(output.ToString().StartsWith('{'));
    }

    [Theory]
    [InlineData(ApplicationErrorCode.InternalFailure, 1)]
    [InlineData(ApplicationErrorCode.Validation, 2)]
    [InlineData(ApplicationErrorCode.NotFound, 3)]
    [InlineData(ApplicationErrorCode.RuntimeUnavailable, 4)]
    [InlineData(ApplicationErrorCode.TransportFailure, 5)]
    [InlineData(ApplicationErrorCode.Unsupported, 6)]
    [InlineData(ApplicationErrorCode.PersistenceFailure, 7)]
    [InlineData(ApplicationErrorCode.Locked, 8)]
    [InlineData(ApplicationErrorCode.Conflict, 8)]
    [InlineData(ApplicationErrorCode.InvalidTransition, 8)]
    public async Task ApplicationFailuresHaveStableExitCodes(ApplicationErrorCode code, int expected)
    {
        Factory factory = new(null) { Failure = new(code, "failure\nmessage") };
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(expected, await new CliApplication(factory, output, error).RunAsync(["list", "--json"]));
        Assert.Equal(string.Empty, output.ToString());
        using JsonDocument document = JsonDocument.Parse(error.ToString());
        Assert.Equal(expected, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task LifecycleHandlersControlTheInjectedSharedRuntime()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.CreateSessionAsync(new("id", "25m", "Focus", new(), new()));
        await runtime.ExecuteAsync(new SessionCommand.Start("id"));
        using StringWriter output = new();
        using StringWriter error = new();
        var cli = new CliApplication(new Factory(runtime), output, error);
        foreach (var (command, state) in new[] { ("pause", TimerState.Paused), ("resume", TimerState.Running), ("restart", TimerState.Running), ("stop", TimerState.Stopped) })
        {
            Assert.Equal(0, await cli.RunAsync([command, "id", "--json"]));
            Assert.Equal(state, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("id")).Value.Countdown.State);
        }
        Assert.Equal(0, await cli.RunAsync(["status", "id"]));
        Assert.Equal(0, await cli.RunAsync(["dismiss", "id"]));
        Assert.Equal(3, await cli.RunAsync(["status", "id"]));
    }

    [Fact]
    public async Task BulkHandlersRejectMixedStatesWithoutChangingEligibleSession()
    {
        await using HourglassRuntime runtime = Runtime();
        foreach (string id in new[] { "a", "b" }) { await runtime.CreateSessionAsync(new(id, "25m", "", new(), new())); }
        await runtime.ExecuteAsync(new SessionCommand.Start("a"));
        using StringWriter output = new();
        using StringWriter error = new();
        var cli = new CliApplication(new Factory(runtime), output, error);
        Assert.Equal(8, await cli.RunAsync(["pause", "all"]));
        Assert.Equal(TimerState.Running, Assert.IsType<ApplicationResult<TimerSessionSnapshot>.Success>(await runtime.GetSessionAsync("a")).Value.Countdown.State);
        await runtime.ExecuteAsync(new SessionCommand.Start("b"));
        Assert.Equal(0, await cli.RunAsync(["pause", "all"]));
        Assert.Equal(0, await cli.RunAsync(["resume", "all"]));
        Assert.Equal(0, await cli.RunAsync(["stop", "all"]));
    }

    [Fact]
    public async Task JsonTimingIsInvariantUnderNonEnglishCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await using HourglassRuntime runtime = Runtime();
            await runtime.CreateSessionAsync(new("id", "1 second", "", new(), new()));
            await runtime.ExecuteAsync(new SessionCommand.Start("id"));
            using StringWriter output = new();
            using StringWriter error = new();
            Assert.Equal(0, await new CliApplication(new Factory(runtime), output, error).RunAsync(["list", "--json"]));
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1000, document.RootElement.GetProperty("result").GetProperty("sessions")[0].GetProperty("remainingMilliseconds").GetDouble());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task CancellationBeforeInvocationProducesOnlyInterruptedError()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(130, await new CliApplication(new Factory(null), output, error).RunAsync(["list", "--json"], cancellation.Token));
        Assert.Equal(string.Empty, output.ToString());
        using JsonDocument document = JsonDocument.Parse(error.ToString());
        Assert.Equal(130, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ContendedAuthorityDoesNotConstructRuntimeAndReleasesItsHandle()
    {
        Authority authority = new() { Acquired = false };
        int constructed = 0;
        var factory = new ExclusiveRuntimeFactory(() => authority, () => { constructed++; return Runtime(); });
        var result = Assert.IsType<ApplicationResult<ExclusiveRuntimeLease>.Failure>(await factory.OpenAsync(ExclusiveRuntimePurpose.Sessions, CancellationToken.None));
        Assert.Equal(ApplicationErrorCode.RuntimeUnavailable, result.Error.Code);
        Assert.Equal(0, constructed);
        Assert.True(authority.Disposed);
    }

    [Fact]
    public async Task CorruptRecoveryFileIsPreservedBeforeConstructingForegroundRuntime()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hourglass-cli-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string recovery = Path.Combine(directory, "active-sessions.json");
        File.WriteAllText(recovery, "{ corrupt");
        Authority authority = new();
        int constructed = 0;
        var paths = new Paths(directory);
        var factory = new ExclusiveRuntimeFactory(() => authority, () => { constructed++; return Runtime(); }, paths);
        try
        {
            var failed = Assert.IsType<ApplicationResult<ExclusiveRuntimeLease>.Failure>(await factory.OpenAsync(ExclusiveRuntimePurpose.Sessions, CancellationToken.None));
            Assert.Equal(ApplicationErrorCode.RuntimeUnavailable, failed.Error.Code);
            Assert.Equal(0, constructed);
            Assert.True(authority.Disposed);
            Assert.Equal("{ corrupt", File.ReadAllText(recovery));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task QueryLeaseHasNoSessionsAndDisposesRuntimeBeforeReleasingAuthority()
    {
        Authority authority = new();
        var factory = new ExclusiveRuntimeFactory(() => authority, Runtime);
        ExclusiveRuntimeLease lease = Assert.IsType<ApplicationResult<ExclusiveRuntimeLease>.Success>(await factory.OpenAsync(ExclusiveRuntimePurpose.Query, CancellationToken.None)).Value;
        Assert.Empty(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Success>(await lease.Client.ListSessionsAsync()).Value);
        Assert.False(authority.Disposed);
        await lease.DisposeAsync();
        Assert.True(authority.Disposed);
        Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Failure>(await lease.Client.ListSessionsAsync());
    }

    [Fact]
    public void ResolvedCliContainsNoPresentationToolkitAndVersionMatchesGui()
    {
        string root = FindRoot();
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/Hourglass.Cli/obj/project.assets.json")));
        foreach (JsonProperty dependency in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            Assert.DoesNotContain("Avalonia", dependency.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Terminal.Gui", dependency.Name, StringComparison.OrdinalIgnoreCase);
        }
        string? expected = XDocument.Load(Path.Combine(root, "src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj")).Descendants("Version").Single().Value;
        Assert.Equal(expected, typeof(CliApplication).Assembly.GetName().Version?.ToString(3));
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hourglass.Linux.sln"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException();
    }
    private static HourglassRuntime Runtime() => new(clock: new Clock(), wallClockNow: () => Now);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }

    private sealed class Factory(HourglassRuntime? runtime) : IExclusiveRuntimeFactory
    {
        public ApplicationError? Failure { get; init; }
        public int Opens { get; private set; }
        public int Disposals { get; private set; }
        public ExclusiveRuntimePurpose Purpose { get; private set; }
        public Task<ApplicationResult<ExclusiveRuntimeLease>> OpenAsync(ExclusiveRuntimePurpose purpose, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Opens++;
            this.Purpose = purpose;
            ApplicationResult<ExclusiveRuntimeLease> result = this.Failure != null ? new ApplicationResult<ExclusiveRuntimeLease>.Failure(this.Failure)
                : new ApplicationResult<ExclusiveRuntimeLease>.Success(new(runtime ?? throw new InvalidOperationException("Unexpected runtime acquisition."), () => { this.Disposals++; return ValueTask.CompletedTask; }));
            return Task.FromResult(result);
        }
    }
    private sealed class Authority : ISingleInstanceService
    {
        public bool Acquired { get; init; } = true;
        public bool Disposed { get; private set; }
        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default) => Task.FromResult(this.Acquired);
        public void Dispose() => this.Disposed = true;
        public Task SendLaunchRequestAsync(SingleInstanceLaunchRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("CLI must not send launcher IPC.");
        public Task StartRequestListenerAsync(Func<SingleInstanceLaunchRequest, CancellationToken, Task> handler, CancellationToken cancellationToken = default) => throw new InvalidOperationException("CLI must not implement legacy launcher control.");
    }

    private sealed class Paths(string directory) : ISettingsPathService
    {
        public string GetSettingsDirectory() => directory;
    }
}
