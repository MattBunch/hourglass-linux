namespace Hourglass.Timing;

using System.Diagnostics;

public sealed class SystemMonotonicClock : IMonotonicClock
{
    private readonly long startTimestamp = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(this.startTimestamp);
}
