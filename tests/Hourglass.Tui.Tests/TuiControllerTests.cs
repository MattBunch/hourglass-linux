namespace Hourglass.Tui.Tests;

using System.Text.Json;
using Hourglass.Application;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class TuiControllerTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0);

    [Fact]
    public async Task CreationSelectionAndQuitAffectOnlyOwnedSessions()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.CreateSessionAsync(new("external", "5 minutes", "GUI", new(), new()));
        TuiController tui = new(runtime);
        await tui.RefreshAsync();
        foreach (string title in new[] { "Tea", "Break" })
        {
            tui.OpenNew();
            tui.SetDraft("25 minutes", title);
            await tui.SubmitDraftAsync();
            Assert.Null(tui.State.Error);
            Assert.Equal(TuiMode.Dashboard, tui.State.Mode);
        }
        Assert.Equal(3, tui.State.Sessions.Length);
        string? previous = tui.State.SelectedId;
        tui.SelectNext(1);
        Assert.NotEqual(previous, tui.State.SelectedId);
        Assert.False(await tui.RequestQuitAsync());
        Assert.Equal(TuiMode.ConfirmQuit, tui.State.Mode);
        Assert.True(await tui.RequestQuitAsync());
        Assert.Equal("external", Assert.Single(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Success>(
            await runtime.ListSessionsAsync()).Value).SessionId);
    }

    [Fact]
    public async Task InvalidCreationRetainsDraftAndDoesNotLeaveSession()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        TuiController tui = new(runtime);
        tui.OpenNew();
        tui.SetDraft("invalid expression", "Tea");
        await tui.SubmitDraftAsync();
        Assert.Equal(TuiMode.New, tui.State.Mode);
        Assert.NotNull(tui.State.Error);
        Assert.Equal("invalid expression", tui.State.Draft?.TimerInput);
        Assert.Empty(Assert.IsType<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>.Success>(
            await runtime.ListSessionsAsync()).Value);
    }

    [Fact]
    public async Task RemovedSelectionMovesToNextNeighbour()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        foreach (string id in new[] { "a", "b", "c" })
        {
            await runtime.CreateSessionAsync(new(id, "5 minutes", id, new(), new()));
        }
        TuiController tui = new(runtime);
        await tui.RefreshAsync();
        tui.SelectNext(1);
        Assert.Equal("b", tui.State.SelectedId);
        await runtime.CloseSessionAsync("b");
        await tui.RefreshAsync();
        Assert.Equal("c", tui.State.SelectedId);
    }

    [Fact]
    public async Task TitleOnlyEditDoesNotRestartAndConflictKeepsDraft()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.CreateSessionAsync(new("shared", "25 minutes", "Initial", new(), new()));
        await runtime.ExecuteAsync(new SessionCommand.Start("shared"));
        TuiController tui = new(runtime);
        await tui.RefreshAsync();
        long revision = tui.State.Selected!.Revision;
        tui.OpenEdit();
        tui.SetDraft("25 minutes", "Updated");
        await tui.SubmitDraftAsync();
        Assert.Equal(TimerState.Running, tui.State.Selected!.Countdown.State);
        Assert.Equal("Updated", tui.State.Selected.TimerTitle);
        tui.OpenEdit();
        await runtime.ExecuteAsync(new SessionCommand.Update("shared", tui.State.Draft!.Revision, TimerTitle: "Other"));
        tui.SetDraft("25 minutes", "My draft");
        await tui.SubmitDraftAsync();
        Assert.Equal(TuiMode.Conflict, tui.State.Mode);
        Assert.Equal("My draft", tui.State.Draft?.TimerTitle);
        await tui.RefreshAsync();
        tui.ResolveConflict(reload: true);
        Assert.Equal("Other", tui.State.Draft?.TimerTitle);
        Assert.True(tui.State.Draft?.Revision > revision);
    }

    [Fact]
    public async Task ExpressionEditAndLifecycleCommandsUseSharedRuntime()
    {
        await using HourglassRuntime runtime = Runtime();
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.CreateSessionAsync(new("owned", "25 minutes", "Focus", new(), new()));
        await runtime.ExecuteAsync(new SessionCommand.Start("owned"));
        TuiController tui = new(runtime);
        await tui.RefreshAsync();
        tui.OpenEdit();
        tui.SetDraft("10 minutes", "Short focus");
        await tui.SubmitDraftAsync();
        Assert.Equal("10 minutes", tui.State.Selected?.TimerInput);
        Assert.Equal(TimerState.Running, tui.State.Selected?.Countdown.State);
        await tui.ActAsync("toggle");
        Assert.Equal(TimerState.Paused, tui.State.Selected?.Countdown.State);
        await tui.ActAsync("toggle");
        Assert.Equal(TimerState.Running, tui.State.Selected?.Countdown.State);
        await tui.ActAsync("restart");
        Assert.Equal(TimerState.Running, tui.State.Selected?.Countdown.State);
        await tui.ActAsync("stop");
        await tui.ActAsync("dismiss");
        Assert.Empty(tui.State.Sessions);
    }

    [Fact]
    public void ResolvedTuiDependenciesDoNotContainAvalonia()
    {
        string root = FindRoot();
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/Hourglass.Tui/obj/project.assets.json")));
        foreach (JsonProperty dependency in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            Assert.DoesNotContain("Avalonia", dependency.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.CommandLine", dependency.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HourglassRuntime Runtime() => new(clock: new Clock(), wallClockNow: () => Now);
    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hourglass.Linux.sln"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException();
    }
}
