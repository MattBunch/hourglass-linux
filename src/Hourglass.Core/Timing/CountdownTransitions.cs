#nullable enable

namespace Hourglass.Timing;

public static class CountdownTransitions
{
    public static CountdownTransition Start(
        CountdownState current,
        TimerStart? timerStart,
        DateTime wallClockNow,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (timerStart == null || !timerStart.IsValid || !timerStart.TryGetEndTime(wallClockNow, out DateTime endTime) || endTime < wallClockNow)
        {
            return CountdownTransition.Invalid(current);
        }

        bool canRestart = timerStart.Type == TimerStartType.TimeSpan;
        TimeSpan? restartDuration = canRestart ? endTime - wallClockNow : null;
        return StartCore(current, wallClockNow, endTime, monotonicNow, timerStart, canRestart, restartDuration);
    }

    public static CountdownTransition StartDuration(
        CountdownState current,
        TimeSpan duration,
        DateTime wallClockNow,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (duration < TimeSpan.Zero)
        {
            return CountdownTransition.Invalid(current);
        }

        return StartCore(current, wallClockNow, wallClockNow + duration, monotonicNow, null, true, duration);
    }

    public static CountdownTransition StartAbsolute(
        CountdownState current,
        DateTime wallClockStart,
        DateTime wallClockEnd,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (wallClockEnd < wallClockStart)
        {
            return CountdownTransition.Invalid(current);
        }

        return StartCore(current, wallClockStart, wallClockEnd, monotonicNow, null, false, null);
    }

    public static CountdownTransition Tick(CountdownState current, TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State is not (TimerState.Running or TimerState.Expired))
        {
            return CountdownTransition.Unchanged(current);
        }

        CountdownEffects effects = CountdownEffects.None;
        CountdownState state = TickCore(current, monotonicNow, ref effects);
        return CountdownTransition.Success(state, effects);
    }

    public static CountdownTransition Pause(CountdownState current, TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State != TimerState.Running)
        {
            return CountdownTransition.Unchanged(current);
        }

        CountdownEffects effects = CountdownEffects.None;
        CountdownState updated = TickCore(current, monotonicNow, ref effects);
        if (updated.State != TimerState.Running)
        {
            return CountdownTransition.Success(updated, effects);
        }

        var paused = new CountdownState(
            TimerState.Paused,
            null,
            null,
            updated.TimeElapsed,
            updated.TimeLeft,
            updated.TimeExpired,
            updated.TotalTime,
            updated.TimerStart,
            updated.CanRestart,
            updated.RestartDuration,
            TimeSpan.Zero,
            updated.TimeElapsed ?? TimeSpan.Zero);

        return CountdownTransition.Success(paused, effects.Append(CountdownEffect.Paused));
    }

    public static CountdownTransition Resume(CountdownState current, DateTime wallClockNow, TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State != TimerState.Paused)
        {
            return CountdownTransition.Unchanged(current);
        }

        TimeSpan elapsed = current.TimeElapsed ?? TimeSpan.Zero;
        TimeSpan remaining = current.TimeLeft ?? TimeSpan.Zero;
        var running = new CountdownState(
            TimerState.Running,
            wallClockNow - elapsed,
            wallClockNow + remaining,
            current.TimeElapsed,
            current.TimeLeft,
            current.TimeExpired,
            current.TotalTime,
            current.TimerStart,
            current.CanRestart,
            current.RestartDuration,
            monotonicNow,
            elapsed);

        CountdownEffects effects = CountdownEffects.None.Append(CountdownEffect.Resumed);
        CountdownState updated = TickCore(running, monotonicNow, ref effects);
        return CountdownTransition.Success(updated, effects);
    }

    public static CountdownTransition Stop(CountdownState current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State == TimerState.Stopped)
        {
            return CountdownTransition.Unchanged(current);
        }

        return CountdownTransition.Success(CountdownState.Stopped, CountdownEffects.None.Append(CountdownEffect.Stopped));
    }

    public static CountdownTransition Restart(CountdownState current, DateTime wallClockNow, TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!current.SupportsRestart)
        {
            return CountdownTransition.Invalid(current);
        }

        CountdownTransition stopped = Stop(current);
        if (current.TimerStart != null)
        {
            CountdownTransition started = Start(stopped.State, current.TimerStart, wallClockNow, monotonicNow);
            return Combine(stopped, started);
        }

        TimeSpan duration = current.RestartDuration ?? current.TotalTime ?? TimeSpan.Zero;
        return Combine(stopped, StartDuration(stopped.State, duration, wallClockNow, monotonicNow));
    }

    private static CountdownTransition StartCore(
        CountdownState current,
        DateTime wallClockStart,
        DateTime wallClockEnd,
        TimeSpan monotonicNow,
        TimerStart? timerStart,
        bool canRestart,
        TimeSpan? restartDuration)
    {
        TimeSpan total = wallClockEnd - wallClockStart;
        var running = new CountdownState(
            TimerState.Running,
            wallClockStart,
            wallClockEnd,
            TimeSpan.Zero,
            total,
            TimeSpan.Zero,
            total,
            timerStart,
            canRestart,
            restartDuration,
            monotonicNow,
            TimeSpan.Zero);

        CountdownEffects effects = CountdownEffects.None.Append(CountdownEffect.Started);
        CountdownState updated = TickCore(running, monotonicNow, ref effects);
        return CountdownTransition.Success(updated, effects);
    }

    private static CountdownState TickCore(CountdownState current, TimeSpan monotonicNow, ref CountdownEffects effects)
    {
        TimeSpan total = current.TotalTime ?? TimeSpan.Zero;
        TimeSpan elapsed = current.ElapsedBeforeRun + (monotonicNow - current.RunStartedAt);
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        TimeSpan timeElapsed = elapsed < total ? elapsed : total;
        TimeSpan timeLeft = elapsed < total ? total - elapsed : TimeSpan.Zero;
        TimeSpan timeExpired = elapsed > total ? elapsed - total : TimeSpan.Zero;
        TimerState state = current.State;

        if (state == TimerState.Running && timeLeft == TimeSpan.Zero)
        {
            state = TimerState.Expired;
            effects = effects.Append(CountdownEffect.Expired);
        }

        effects = effects.Append(CountdownEffect.Ticked);
        return new CountdownState(
            state,
            current.StartTime,
            current.EndTime,
            timeElapsed,
            timeLeft,
            timeExpired,
            current.TotalTime,
            current.TimerStart,
            current.CanRestart,
            current.RestartDuration,
            current.RunStartedAt,
            current.ElapsedBeforeRun);
    }

    private static CountdownTransition Combine(CountdownTransition first, CountdownTransition second)
    {
        if (!second.Succeeded)
        {
            return new CountdownTransition(first.State, first.Effects, false);
        }

        return CountdownTransition.Success(second.State, first.Effects.Append(second.Effects));
    }
}
