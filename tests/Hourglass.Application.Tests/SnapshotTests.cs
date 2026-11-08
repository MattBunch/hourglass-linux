namespace Hourglass.Application.Tests;

using System.Collections.Immutable;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class SnapshotTests
{
    [Fact]
    public void EarlierSnapshotSurvivesLaterStateAndOptionChanges()
    {
        TimerSessionSnapshot first = new("legacy-session-id", 0, "5m", "Tea", CountdownState.Stopped, new TimerDefaults(), SessionActions.Start);
        TimerSessionSnapshot second = first with
        {
            Revision = 1,
            Countdown = CountdownTransitions.StartDuration(first.Countdown, TimeSpan.FromMinutes(5), new DateTime(2026, 1, 1), TimeSpan.Zero).State,
            Options = first.Options with { LoopTimer = true }
        };

        Assert.Equal(TimerState.Stopped, first.Countdown.State);
        Assert.False(first.Options.LoopTimer);
        Assert.Equal(TimerState.Running, second.Countdown.State);
        Assert.True(second.Options.LoopTimer);
    }

    [Fact]
    public void PublishedSessionCollectionDoesNotShareMutableArrayOwnership()
    {
        TimerSessionSnapshot first = new("legacy-session-id", 0, "5m", "Tea", CountdownState.Stopped, new TimerDefaults(), SessionActions.Start);
        TimerSessionSnapshot[] source = [first];
        ImmutableArray<TimerSessionSnapshot> published = source.ToImmutableArray();
        source[0] = first with { TimerTitle = "Changed" };

        Assert.Equal("Tea", published[0].TimerTitle);
    }
}
