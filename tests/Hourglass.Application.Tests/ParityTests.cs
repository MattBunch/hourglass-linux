namespace Hourglass.Application.Tests;

using System.Collections.Immutable;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class ParityTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0);

    [Fact]
    public void SavedSelectionPrefersIdAndRejectsAmbiguousNames()
    {
        ImmutableArray<SavedTimerDefinition> timers =
        [
            new("first", "5 minutes", "Tea", "Break"),
            new("second", "10 minutes", "Focus", "Break"),
            new("Break", "15 minutes", "By ID", "Different")
        ];
        var exact = Assert.IsType<ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success>(
            SavedTimerSelector.Resolve(timers, new SavedTimerSelection.ByNameOrId("Break")));
        Assert.Equal("Break", Assert.Single(exact.Value).Id);
        var ambiguous = Assert.IsType<ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure>(
            SavedTimerSelector.Resolve(timers.RemoveAt(2), new SavedTimerSelection.ByNameOrId("Break")));
        Assert.Equal(ApplicationErrorCode.Conflict, ambiguous.Error.Code);
        Assert.Contains("first", ambiguous.Error.Message);
        Assert.Contains("second", ambiguous.Error.Message);
    }

    [Fact]
    public void SharedOptionsNormalizeMutualExclusionAndPreserveGuiFields()
    {
        LinuxAppSettings existing = LinuxAppSettings.Default with { AlwaysOnTop = true, LoopTimer = true };
        var closed = Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(SharedOptionRegistry.Apply(existing,
            [new("close-when-expired", "true")])).Value;
        Assert.True(closed.CloseWhenExpired);
        Assert.False(closed.LoopTimer);
        Assert.True(closed.AlwaysOnTop);
        Assert.Equal(ApplicationErrorCode.Validation, Assert.IsType<ApplicationResult<LinuxAppSettings>.Failure>(
            SharedOptionRegistry.Apply(existing, [new("loop-sound", "true"), new("close-when-expired", "true")])).Error.Code);
        Assert.Equal(ApplicationErrorCode.Validation, Assert.IsType<ApplicationResult<LinuxAppSettings>.Failure>(
            SharedOptionRegistry.Apply(existing, [new("audio-alert-sound-id", "invalid")])).Error.Code);
    }

    [Fact]
    public async Task SavedForegroundBatchReturnsCatalogOrderAndLeavesNoSessions()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("a", "0 seconds", "Tea")));
        await runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("b", "0 seconds", "Break")));
        var result = Assert.IsType<ApplicationResult<ImmutableArray<ForegroundOutcome>>.Success>(
            await runtime.RunSavedForegroundAsync(new SavedTimerSelection.All()));
        Assert.Equal(2, result.Value.Length);
        Assert.Equal("Break", result.Value[0].Session.TimerTitle);
        Assert.Equal("Tea", result.Value[1].Session.TimerTitle);
        Assert.All(result.Value, item => Assert.Equal(ForegroundCompletion.Expired, item.Completion));
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
    }

    [Fact]
    public async Task InvalidSavedBatchStartsNothingAndSoundPreviewReportsUnavailable()
    {
        await using HourglassRuntime runtime = new(clock: new Clock(), wallClockNow: () => Now);
        await runtime.PrepareForegroundRuntimeAsync();
        await runtime.ChangeSavedTimersAsync(new SavedTimerChange.Add(new("future", "10 minutes")));
        var missing = Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure>(
            await runtime.StartSavedSessionsAsync(new SavedTimerSelection.ByNameOrId("missing")));
        Assert.Equal(ApplicationErrorCode.NotFound, missing.Error.Code);
        Assert.Empty(Assert.IsType<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success>(await runtime.ListSessionsAsync()).Value);
        Assert.Equal(ApplicationErrorCode.Unsupported, Assert.IsType<ApplicationResult<bool>.Failure>(
            await runtime.PreviewSoundAsync(BuiltInAudioAlertSounds.NormalBeep)).Error.Code);
    }

    [Fact]
    public void DiagnosticJournalRetainsImmutableBoundedMessages()
    {
        DiagnosticJournal journal = new();
        for (int index = 0; index < 130; index++)
        {
            journal.Record(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort, "audio", "play", "test", index.ToString()));
        }
        ImmutableArray<ApplicationDiagnostic> snapshot = journal.Snapshot();
        Assert.Equal(128, snapshot.Length);
        Assert.Equal("2", snapshot[0].Message);
        journal.Record(new(DiagnosticSeverity.Error, DiagnosticFailureClass.UserRequested, "store", "save", "test", "later"));
        Assert.Equal("2", snapshot[0].Message);
        Assert.Equal("later", journal.Snapshot()[^1].Message);
    }

    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
}
