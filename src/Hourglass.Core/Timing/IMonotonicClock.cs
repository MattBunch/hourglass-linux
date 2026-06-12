namespace Hourglass.Timing;

public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }
}
