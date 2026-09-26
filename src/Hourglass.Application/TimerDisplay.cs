namespace Hourglass.Application;

using Hourglass.Timing;

public static class TimerDisplay
{
    public static string FormatTimerTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        int hours = (int)Math.Floor(time.TotalHours);
        return FormattableString.Invariant($"{hours:00}:{time.Minutes:00}:{time.Seconds:00}");
    }

    public static double GetProgressPercent(CountdownState timerState, bool reverseProgressBar = false)
    {
        ArgumentNullException.ThrowIfNull(timerState);
        if (timerState.State == TimerState.Expired)
        {
            return reverseProgressBar ? 100 : 0;
        }

        TimeSpan total = timerState.TotalTime ?? TimeSpan.Zero;
        if (timerState.State == TimerState.Stopped || total <= TimeSpan.Zero)
        {
            return 0;
        }

        TimeSpan elapsed = timerState.TimeElapsed ?? TimeSpan.Zero;
        TimeSpan progressTime = reverseProgressBar ? elapsed : total - elapsed;
        double progress = progressTime.TotalMilliseconds / total.TotalMilliseconds * 100;
        return double.IsNaN(progress) || double.IsInfinity(progress) ? 0 : Math.Clamp(progress, 0, 100);
    }
}
