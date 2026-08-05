using Hourglass.DemoRecorder.Services;
using Hourglass.Timing;
using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class DemoClockTests
{
    [Fact]
    public void StartsAtKnownInstantAndAdvancesExactly()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var clock = new DemoClock(start);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromMilliseconds(1250), clock.Elapsed);
        Assert.Equal(start + TimeSpan.FromMilliseconds(1250), clock.WallClockNow);
    }

    [Fact]
    public void RejectsBackwardMovement()
    {
        var clock = new DemoClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void DrivesCountdownCompletionWithoutRealDelay()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var clock = new DemoClock(start);
        var engine = new CountdownEngine(clock);

        engine.Start(TimeSpan.FromSeconds(10), clock.WallClockNow);
        clock.Advance(TimeSpan.FromSeconds(10));
        engine.Update();

        Assert.Equal(TimerState.Expired, engine.State);
        Assert.Equal(TimeSpan.Zero, engine.TimeLeft);
    }
}
