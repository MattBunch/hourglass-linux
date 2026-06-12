namespace Hourglass.Core.Tests.Serialization;

using System.Xml.Serialization;
using Hourglass.Parsing;
using Hourglass.Serialization;
using Hourglass.Timing;
using Xunit;

public sealed class TimerSerializationTests
{
    [Fact]
    public void TimerStartInfoRoundTripsTimeSpanTokenThroughXml()
    {
        var info = new TimerStartInfo
        {
            TimerStartToken = TimerStartToken.FromString("15 minutes")!
        };

        TimerStartInfo roundTripped = RoundTrip(info);
        var token = Assert.IsType<TimeSpanToken>(roundTripped.TimerStartToken);

        Assert.Equal(15, token.Minutes);
    }

    [Fact]
    public void TimerInfoTickProxyPreservesNullableTimeSpans()
    {
        var info = new TimerInfo
        {
            State = TimerState.Running,
            TimeElapsed = TimeSpan.FromSeconds(3),
            TimeLeft = TimeSpan.FromSeconds(7),
            TimeExpired = TimeSpan.Zero,
            TotalTime = TimeSpan.FromSeconds(10)
        };

        TimerInfo roundTripped = RoundTrip(info);

        Assert.Equal(TimeSpan.FromSeconds(3), roundTripped.TimeElapsed);
        Assert.Equal(TimeSpan.FromSeconds(7), roundTripped.TimeLeft);
        Assert.Equal(TimeSpan.Zero, roundTripped.TimeExpired);
        Assert.Equal(TimeSpan.FromSeconds(10), roundTripped.TotalTime);
    }

    private static T RoundTrip<T>(T value)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, value);
        stream.Position = 0;
        return (T)serializer.Deserialize(stream)!;
    }
}
