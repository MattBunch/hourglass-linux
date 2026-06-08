namespace Hourglass.Core.Tests.Parsing;

using System.Globalization;
using Hourglass.Parsing;
using Xunit;

public sealed class DateTimeTokenTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void ParseWithNullInputThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DateTimeToken.Parser.Instance.Parse(null!, EnUs));
    }

    [Fact]
    public void ParseDayOfWeekReturnsDayOfWeekToken()
    {
        var token = Assert.IsType<DateTimeToken>(DateTimeToken.Parser.Instance.Parse("next Monday", EnUs));
        var date = Assert.IsType<DayOfWeekDateToken>(token.DateToken);

        Assert.Equal(DayOfWeek.Monday, date.DayOfWeek);
        Assert.Equal(DayOfWeekRelation.Next, date.DayOfWeekRelation);
        Assert.IsType<EmptyTimeToken>(token.TimeToken);
    }

    [Fact]
    public void ParseSpecialDateReturnsSpecialDateToken()
    {
        var token = Assert.IsType<DateTimeToken>(DateTimeToken.Parser.Instance.Parse("christmas", EnUs));
        var date = Assert.IsType<SpecialDateToken>(token.DateToken);

        Assert.Equal(SpecialDate.ChristmasDay, date.SpecialDate);
    }

    [Fact]
    public void ParseDayFirstCultureKeepsDayFirstNumericalDate()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var token = Assert.IsType<DateTimeToken>(DateTimeToken.Parser.Instance.Parse("31/12/2026", culture));
        var date = Assert.IsType<NormalDateToken>(token.DateToken);

        Assert.Equal(31, date.Day);
        Assert.Equal(12, date.Month);
        Assert.Equal(2026, date.Year);
    }

    [Fact]
    public void TimerStartParserPrefersTimeSpanForAmbiguousInput()
    {
        Assert.IsType<TimeSpanToken>(TimerStartToken.FromString("5", EnUs));
    }
}
