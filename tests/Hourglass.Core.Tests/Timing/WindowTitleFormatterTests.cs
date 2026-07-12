namespace Hourglass.Core.Tests.Timing;

using Hourglass.Timing;
using Xunit;

public sealed class WindowTitleFormatterTests
{
    [Theory]
    [InlineData(WindowTitleMode.None, "Hourglass")]
    [InlineData(WindowTitleMode.ApplicationName, "Hourglass")]
    [InlineData(WindowTitleMode.TimeLeft, "00:04:30")]
    [InlineData(WindowTitleMode.TimeElapsed, "00:00:30")]
    [InlineData(WindowTitleMode.TimerTitle, "Tea")]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle, "00:04:30 - Tea")]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle, "00:00:30 - Tea")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft, "Tea - 00:04:30")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed, "Tea - 00:00:30")]
    public void FormatUsesSelectedWindowTitleMode(WindowTitleMode mode, string expected)
    {
        string title = WindowTitleFormatter.Format(
            mode,
            "Hourglass",
            "Tea",
            "00:04:30",
            "00:00:30");

        Assert.Equal(expected, title);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle, "00:04:30")]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle, "00:00:30")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft, "00:04:30")]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed, "00:00:30")]
    public void FormatOmitsBlankTimerTitleFromCombinedModes(WindowTitleMode mode, string expected)
    {
        string title = WindowTitleFormatter.Format(
            mode,
            "Hourglass",
            "   ",
            "00:04:30",
            "00:00:30");

        Assert.Equal(expected, title);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeft)]
    [InlineData(WindowTitleMode.TimeElapsed)]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed)]
    public void FormatFallsBackToTitleWhenTimeIsUnavailable(WindowTitleMode mode)
    {
        string title = WindowTitleFormatter.Format(
            mode,
            "Hourglass",
            "Tea",
            null,
            null);

        Assert.Equal("Tea", title);
    }

    [Theory]
    [InlineData(WindowTitleMode.TimeLeft)]
    [InlineData(WindowTitleMode.TimeElapsed)]
    [InlineData(WindowTitleMode.TimeLeftPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimeElapsedPlusTimerTitle)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeLeft)]
    [InlineData(WindowTitleMode.TimerTitlePlusTimeElapsed)]
    public void FormatFallsBackToApplicationWhenTimeAndTitleAreUnavailable(WindowTitleMode mode)
    {
        string title = WindowTitleFormatter.Format(
            mode,
            "Hourglass",
            "",
            null,
            null);

        Assert.Equal("Hourglass", title);
    }
}
