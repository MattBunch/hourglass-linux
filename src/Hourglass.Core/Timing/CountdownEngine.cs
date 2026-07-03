#nullable enable

namespace Hourglass.Timing;

using Hourglass.Serialization;

public sealed class CountdownEngine
{
    private readonly IMonotonicClock clock;
    private CountdownState state;

    public CountdownEngine(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.state = CountdownState.Stopped;
    }

    public CountdownEngine(IMonotonicClock clock, TimerInfo timerInfo)
        : this(clock)
    {
        this.state = CountdownState.FromTimerInfo(timerInfo, this.clock.Elapsed);
    }

    public event EventHandler? Started;

    public event EventHandler? Paused;

    public event EventHandler? Resumed;

    public event EventHandler? Stopped;

    public event EventHandler? Expired;

    public event EventHandler? Tick;

    public CountdownState Snapshot => this.state;

    public TimerState State => this.state.State;

    public DateTime? StartTime => this.state.StartTime;

    public DateTime? EndTime => this.state.EndTime;

    public TimeSpan? TimeElapsed => this.state.TimeElapsed;

    public TimeSpan? TimeLeft => this.state.TimeLeft;

    public TimeSpan? TimeExpired => this.state.TimeExpired;

    public TimeSpan? TotalTime => this.state.TotalTime;

    public TimerStart? TimerStart => this.state.TimerStart;

    public bool SupportsRestart => this.state.SupportsRestart;

    public bool Start(TimerStart? newTimerStart, DateTime wallClockStart)
    {
        CountdownTransition transition = CountdownTransitions.Start(
            this.state,
            newTimerStart,
            wallClockStart,
            this.clock.Elapsed);

        if (!transition.Succeeded)
        {
            return false;
        }

        this.Apply(transition);
        return true;
    }

    public void Start(TimeSpan duration, DateTime wallClockStart)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        this.Apply(CountdownTransitions.StartDuration(this.state, duration, wallClockStart, this.clock.Elapsed));
    }

    public void Start(DateTime wallClockStart, DateTime wallClockEnd)
    {
        if (wallClockEnd < wallClockStart)
        {
            throw new ArgumentOutOfRangeException(nameof(wallClockEnd));
        }

        this.Apply(CountdownTransitions.StartAbsolute(this.state, wallClockStart, wallClockEnd, this.clock.Elapsed));
    }

    public bool Restart(DateTime wallClockStart)
    {
        CountdownTransition transition = CountdownTransitions.Restart(this.state, wallClockStart, this.clock.Elapsed);
        if (!transition.Succeeded)
        {
            return false;
        }

        this.Apply(transition);
        return true;
    }

    public void Pause()
    {
        this.Apply(CountdownTransitions.Pause(this.state, this.clock.Elapsed));
    }

    public void Resume(DateTime wallClockNow)
    {
        this.Apply(CountdownTransitions.Resume(this.state, wallClockNow, this.clock.Elapsed));
    }

    public void Stop()
    {
        this.Apply(CountdownTransitions.Stop(this.state));
    }

    public void Update()
    {
        this.Apply(CountdownTransitions.Tick(this.state, this.clock.Elapsed));
    }

    public TimerInfo ToTimerInfo()
    {
        return this.state.ToTimerInfo();
    }

    public void Restore(TimerInfo timerInfo)
    {
        ArgumentNullException.ThrowIfNull(timerInfo);

        this.state = CountdownState.FromTimerInfo(timerInfo, this.clock.Elapsed);
    }

    private void Apply(CountdownTransition transition)
    {
        this.state = transition.State;
        this.Publish(transition.Effects.First);
        this.Publish(transition.Effects.Second);
        this.Publish(transition.Effects.Third);
        this.Publish(transition.Effects.Fourth);
    }

    private void Publish(CountdownEffect effect)
    {
        switch (effect)
        {
            case CountdownEffect.Started:
                this.Started?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.Paused:
                this.Paused?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.Resumed:
                this.Resumed?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.Stopped:
                this.Stopped?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.Expired:
                this.Expired?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.Ticked:
                this.Tick?.Invoke(this, EventArgs.Empty);
                break;
            case CountdownEffect.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
        }
    }
}
