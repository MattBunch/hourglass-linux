using Hourglass.Timing;

namespace Hourglass.DemoRecorder.Services;

public sealed class DemoClock : IMonotonicClock
{
    public DemoClock(DateTime wallClockNow)
    {
        this.WallClockNow = wallClockNow;
    }

    public TimeSpan Elapsed { get; private set; }

    public DateTime WallClockNow { get; private set; }

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Demo clock cannot move backwards.");
        }

        this.Elapsed += duration;
        this.WallClockNow += duration;
    }
}
