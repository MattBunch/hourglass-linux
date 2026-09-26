namespace Hourglass.Application;

using Hourglass.Timing;

public static class TimerInputValidation
{
    public static ApplicationResult<TimerStart> Parse(string input, DateTime wallClockNow)
    {
        ArgumentNullException.ThrowIfNull(input);
        TimerStart? start = TimerStart.FromString(input);
        return start != null && start.IsValid && start.TryGetEndTime(wallClockNow, out DateTime end) && end >= wallClockNow
            ? new ApplicationResult<TimerStart>.Success(start)
            : new ApplicationResult<TimerStart>.Failure(new ApplicationError(ApplicationErrorCode.Validation, "Enter a valid current timer."));
    }
}
