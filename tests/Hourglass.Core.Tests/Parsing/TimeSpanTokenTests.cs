namespace Hourglass.Core.Tests.Parsing;

using System.Globalization;
using Hourglass.Parsing;
using Xunit;

public sealed class TimeSpanTokenTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void ParseWithGarbageInputThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => TimeSpanToken.Parser.Instance.Parse("garbage", EnUs));
    }

    [Theory]
    [InlineData("0", 0, 0, 0)]
    [InlineData("5", 0, 5, 0)]
    [InlineData("15:30", 0, 15, 30)]
    [InlineData("72:15:30", 72, 15, 30)]
    public void ParseShortFormsPreservesLegacyMeaning(string input, double hours, double minutes, double seconds)
    {
        var token = Assert.IsType<TimeSpanToken>(TimeSpanToken.Parser.Instance.Parse(input, EnUs));

        Assert.Equal(hours, token.Hours);
        Assert.Equal(minutes, token.Minutes);
        Assert.Equal(seconds, token.Seconds);
    }

    [Fact]
    public void ParseLongFormAggregatesUnits()
    {
        var token = Assert.IsType<TimeSpanToken>(TimeSpanToken.Parser.Instance.Parse(
            "1 year 2 months 3 weeks 4 days 5 hours 6 minutes 7 seconds",
            EnUs));

        Assert.Equal(1, token.Years);
        Assert.Equal(2, token.Months);
        Assert.Equal(3, token.Weeks);
        Assert.Equal(4, token.Days);
        Assert.Equal(5, token.Hours);
        Assert.Equal(6, token.Minutes);
        Assert.Equal(7, token.Seconds);
    }
}
