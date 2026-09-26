namespace Hourglass.Application.Tests;

using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class TimerPolicyTests
{
    [Theory]
    [InlineData(true, true, false, true, true, false, true)]
    [InlineData(true, true, false, false, false, true, false)]
    [InlineData(false, true, true, true, false, false, true)]
    [InlineData(false, true, false, true, false, true, false)]
    [InlineData(false, false, false, true, false, false, true)]
    public void ExpiryPreservesLoopCloseAndAttentionPrecedence(
        bool loop, bool close, bool loopSound, bool supportsRestart,
        bool expectedRestart, bool expectedClose, bool expectedAttention)
    {
        TimerDefaults options = new(LoopTimer: loop, CloseWhenExpired: close, LoopSound: loopSound);
        ExpiryDecision decision = ExpiryDecision.FromOptions(options, new ApplicationPreferences(), supportsRestart);

        Assert.Equal(expectedRestart, decision.Restart);
        Assert.Equal(expectedClose, decision.Close);
        Assert.Equal(expectedAttention, decision.RequestAttention);
    }

    [Fact]
    public void DisabledPopupSuppressesAttention()
    {
        Assert.False(ExpiryDecision.FromOptions(new TimerDefaults(), new ApplicationPreferences(PopUpWhenExpired: false), true).RequestAttention);
    }

    [Theory]
    [InlineData("not a timer")]
    [InlineData("-5 minutes")]
    [InlineData("")]
    public void InvalidStartReturnsFailureWithoutThrowing(string input)
    {
        Assert.IsType<ApplicationResult<TimerStart>.Failure>(TimerInputValidation.Parse(input, new DateTime(2026, 6, 8, 12, 30, 0)));
    }

    [Fact]
    public void AbsoluteStartUsesSuppliedWallClock()
    {
        DateTime now = new(2026, 6, 8, 12, 30, 0);
        var result = Assert.IsType<ApplicationResult<TimerStart>.Success>(TimerInputValidation.Parse("1pm", now));
        Assert.True(result.Value.TryGetEndTime(now, out DateTime end));
        Assert.Equal(now.AddMinutes(30), end);
    }

    [Fact]
    public void DisplayRetainsElapsedAndReverseProgressSemantics()
    {
        CountdownState running = CountdownTransitions.StartDuration(CountdownState.Stopped, TimeSpan.FromSeconds(100), new DateTime(2026, 1, 1), TimeSpan.Zero).State;
        CountdownState advanced = CountdownTransitions.Tick(running, TimeSpan.FromSeconds(25)).State;

        Assert.Equal(75, TimerDisplay.GetProgressPercent(advanced));
        Assert.Equal(25, TimerDisplay.GetProgressPercent(advanced, reverseProgressBar: true));
        Assert.Equal("00:00:00", TimerDisplay.FormatTimerTime(TimeSpan.FromSeconds(-1)));
        Assert.Equal("25:00:00", TimerDisplay.FormatTimerTime(TimeSpan.FromHours(25)));
    }
}
