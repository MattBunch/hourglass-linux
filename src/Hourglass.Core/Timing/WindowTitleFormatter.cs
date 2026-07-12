#nullable enable

namespace Hourglass.Timing;

public static class WindowTitleFormatter
{
    public static string Format(
        WindowTitleMode mode,
        string applicationName,
        string? timerTitle,
        string? timeLeft,
        string? timeElapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        string? resolvedTitle = string.IsNullOrWhiteSpace(timerTitle) ? null : timerTitle;

        return mode switch
        {
            WindowTitleMode.TimeLeft => timeLeft ?? resolvedTitle ?? applicationName,
            WindowTitleMode.TimeElapsed => timeElapsed ?? resolvedTitle ?? applicationName,
            WindowTitleMode.TimerTitle => resolvedTitle ?? applicationName,
            WindowTitleMode.TimeLeftPlusTimerTitle => Combine(timeLeft, resolvedTitle, applicationName),
            WindowTitleMode.TimeElapsedPlusTimerTitle => Combine(timeElapsed, resolvedTitle, applicationName),
            WindowTitleMode.TimerTitlePlusTimeLeft => Combine(resolvedTitle, timeLeft, applicationName),
            WindowTitleMode.TimerTitlePlusTimeElapsed => Combine(resolvedTitle, timeElapsed, applicationName),
            _ => applicationName
        };
    }

    private static string Combine(string? first, string? second, string fallback)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return string.IsNullOrWhiteSpace(second) ? fallback : second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return string.Concat(first, " - ", second);
    }
}
