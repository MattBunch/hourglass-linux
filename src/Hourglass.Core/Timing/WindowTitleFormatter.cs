#nullable enable

namespace Hourglass.Timing;

public static class WindowTitleFormatter
{
    public static string Format(
        WindowTitleMode mode,
        string applicationName,
        string? timerTitle,
        string timeLeft,
        string timeElapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(timeLeft);
        ArgumentNullException.ThrowIfNull(timeElapsed);

        string resolvedTitle = string.IsNullOrWhiteSpace(timerTitle)
            ? applicationName
            : timerTitle;

        return mode switch
        {
            WindowTitleMode.TimeLeft => timeLeft,
            WindowTitleMode.TimeElapsed => timeElapsed,
            WindowTitleMode.TimerTitle => resolvedTitle,
            WindowTitleMode.TimeLeftPlusTimerTitle => Combine(timeLeft, resolvedTitle),
            WindowTitleMode.TimeElapsedPlusTimerTitle => Combine(timeElapsed, resolvedTitle),
            WindowTitleMode.TimerTitlePlusTimeLeft => Combine(resolvedTitle, timeLeft),
            WindowTitleMode.TimerTitlePlusTimeElapsed => Combine(resolvedTitle, timeElapsed),
            _ => applicationName
        };
    }

    private static string Combine(string first, string second)
    {
        return string.Concat(first, " - ", second);
    }
}
