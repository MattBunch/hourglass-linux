namespace Hourglass.Timing;

using Hourglass.Serialization;

public sealed class CountdownEngine
{
    private readonly IMonotonicClock clock;
    private TimeSpan runStartedAt;
    private TimeSpan elapsedBeforeRun;
    private TimerStart timerStart;
    private TimeSpan? restartDuration;
    private bool canRestart;

    public CountdownEngine(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.State = TimerState.Stopped;
    }

    public CountdownEngine(IMonotonicClock clock, TimerInfo timerInfo)
        : this(clock)
    {
        if (timerInfo == null)
        {
            throw new ArgumentNullException(nameof(timerInfo));
        }

        this.State = timerInfo.State;
        this.StartTime = timerInfo.StartTime;
        this.EndTime = timerInfo.EndTime;
        this.TimeElapsed = timerInfo.TimeElapsed;
        this.TimeLeft = timerInfo.TimeLeft;
        this.TimeExpired = timerInfo.TimeExpired;
        this.TotalTime = timerInfo.TotalTime;
        this.timerStart = TimerStart.FromTimerStartInfo(timerInfo.TimerStart);
        this.canRestart = this.timerStart?.Type == TimerStartType.TimeSpan;

        if (this.State == TimerState.Running || this.State == TimerState.Expired)
        {
            this.elapsedBeforeRun = this.TimeElapsed ?? TimeSpan.Zero;
            this.runStartedAt = this.clock.Elapsed;
            this.Update();
        }
    }

    public event EventHandler Started;

    public event EventHandler Paused;

    public event EventHandler Resumed;

    public event EventHandler Stopped;

    public event EventHandler Expired;

    public event EventHandler Tick;

    public TimerState State { get; private set; }

    public DateTime? StartTime { get; private set; }

    public DateTime? EndTime { get; private set; }

    public TimeSpan? TimeElapsed { get; private set; }

    public TimeSpan? TimeLeft { get; private set; }

    public TimeSpan? TimeExpired { get; private set; }

    public TimeSpan? TotalTime { get; private set; }

    public TimerStart TimerStart => this.timerStart;

    public bool SupportsRestart => this.canRestart && (this.timerStart != null || this.restartDuration.HasValue);

    public bool Start(TimerStart newTimerStart, DateTime wallClockStart)
    {
        if (newTimerStart == null)
        {
            return false;
        }

        if (!newTimerStart.TryGetEndTime(wallClockStart, out DateTime endTime))
        {
            return false;
        }

        this.timerStart = newTimerStart;
        this.canRestart = newTimerStart.Type == TimerStartType.TimeSpan;
        this.restartDuration = this.canRestart ? endTime - wallClockStart : null;
        this.StartCore(wallClockStart, endTime);
        return true;
    }

    public void Start(TimeSpan duration, DateTime wallClockStart)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        this.timerStart = null;
        this.canRestart = true;
        this.restartDuration = duration;
        this.StartCore(wallClockStart, wallClockStart + duration);
    }

    public void Start(DateTime wallClockStart, DateTime wallClockEnd)
    {
        this.timerStart = null;
        this.canRestart = false;
        this.restartDuration = null;
        this.StartCore(wallClockStart, wallClockEnd);
    }

    private void StartCore(DateTime wallClockStart, DateTime wallClockEnd)
    {
        if (wallClockEnd < wallClockStart)
        {
            throw new ArgumentOutOfRangeException(nameof(wallClockEnd));
        }

        this.State = TimerState.Running;
        this.StartTime = wallClockStart;
        this.EndTime = wallClockEnd;
        this.TotalTime = wallClockEnd - wallClockStart;
        this.elapsedBeforeRun = TimeSpan.Zero;
        this.runStartedAt = this.clock.Elapsed;
        this.TimeElapsed = TimeSpan.Zero;
        this.TimeLeft = this.TotalTime;
        this.TimeExpired = TimeSpan.Zero;

        this.Started?.Invoke(this, EventArgs.Empty);
        this.Update();
    }

    public bool Restart(DateTime wallClockStart)
    {
        if (!this.SupportsRestart)
        {
            return false;
        }

        if (this.timerStart != null)
        {
            this.Stop();
            return this.Start(this.timerStart, wallClockStart);
        }

        TimeSpan duration = this.TotalTime ?? TimeSpan.Zero;
        if (this.restartDuration.HasValue)
        {
            duration = this.restartDuration.Value;
        }

        this.Stop();
        this.Start(duration, wallClockStart);
        return true;
    }

    public void Pause()
    {
        if (this.State != TimerState.Running)
        {
            return;
        }

        this.Update();
        this.State = TimerState.Paused;
        this.StartTime = null;
        this.EndTime = null;
        this.elapsedBeforeRun = this.TimeElapsed ?? TimeSpan.Zero;
        this.Paused?.Invoke(this, EventArgs.Empty);
    }

    public void Resume(DateTime wallClockNow)
    {
        if (this.State != TimerState.Paused)
        {
            return;
        }

        TimeSpan remaining = this.TimeLeft ?? TimeSpan.Zero;
        this.State = TimerState.Running;
        this.runStartedAt = this.clock.Elapsed;
        this.StartTime = wallClockNow - this.elapsedBeforeRun;
        this.EndTime = wallClockNow + remaining;
        this.Resumed?.Invoke(this, EventArgs.Empty);
        this.Update();
    }

    public void Stop()
    {
        if (this.State == TimerState.Stopped)
        {
            return;
        }

        this.State = TimerState.Stopped;
        this.StartTime = null;
        this.EndTime = null;
        this.TimeElapsed = null;
        this.TimeLeft = null;
        this.TimeExpired = null;
        this.TotalTime = null;
        this.elapsedBeforeRun = TimeSpan.Zero;
        this.runStartedAt = TimeSpan.Zero;
        this.timerStart = null;
        this.restartDuration = null;
        this.canRestart = false;
        this.Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void Update()
    {
        if (this.State != TimerState.Running && this.State != TimerState.Expired)
        {
            return;
        }

        TimeSpan total = this.TotalTime ?? TimeSpan.Zero;
        TimeSpan elapsed = this.elapsedBeforeRun + (this.clock.Elapsed - this.runStartedAt);
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        this.TimeElapsed = elapsed < total ? elapsed : total;
        this.TimeLeft = elapsed < total ? total - elapsed : TimeSpan.Zero;
        this.TimeExpired = elapsed > total ? elapsed - total : TimeSpan.Zero;

        if (this.State == TimerState.Running && this.TimeLeft == TimeSpan.Zero)
        {
            this.State = TimerState.Expired;
            this.Expired?.Invoke(this, EventArgs.Empty);
        }

        this.Tick?.Invoke(this, EventArgs.Empty);
    }

    public TimerInfo ToTimerInfo()
    {
        return new TimerInfo
        {
            State = this.State,
            StartTime = this.StartTime,
            EndTime = this.EndTime,
            TimeElapsed = this.TimeElapsed,
            TimeLeft = this.TimeLeft,
            TimeExpired = this.TimeExpired,
            TotalTime = this.TotalTime,
            TimerStart = this.timerStart?.ToTimerStartInfo()
        };
    }
}
