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

    [Fact]
    public void FormatUsesApplicationNameWhenTimerTitleIsBlank()
    {
        string title = WindowTitleFormatter.Format(
            WindowTitleMode.TimerTitlePlusTimeLeft,
            "Hourglass",
            "   ",
            "00:04:30",
            "00:00:30");

        Assert.Equal("Hourglass - 00:04:30", title);
    }
}
