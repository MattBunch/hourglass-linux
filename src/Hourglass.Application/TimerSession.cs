namespace Hourglass.Application;

using Hourglass.Serialization;
using Hourglass.Settings;
using Hourglass.Timing;

/// <summary>
/// Owns one engine and converts its synchronous notifications into explicit transition results.
/// The caller serializes operations. No engine or event subscription is exposed to frontends.
/// </summary>
internal sealed class TimerSession : IDisposable
{
    private readonly CountdownEngine engine;
    private CountdownState countdown;
    private CountdownEffects pendingEffects = CountdownEffects.None;
    private bool disposed;
    private long revision;
    private long commandRevision;
    internal string TimerInput { get; private set; } = string.Empty;
    internal string TimerTitle { get; private set; } = string.Empty;
    internal TimerDefaults Options { get; private set; } = new();
    internal ApplicationPreferences Preferences { get; private set; } = new();
    internal long PublicationSequence { get; set; }
    internal long CommandRevision => this.commandRevision;
    internal void InvalidateLifecycle() => Interlocked.Increment(ref this.revision);

    internal TimerSession(IMonotonicClock clock) : this(new CountdownEngine(clock)) { }

    internal void CommitMetadata(string input, string title, TimerDefaults options, ApplicationPreferences preferences)
    {
        if (this.TimerInput == input && this.TimerTitle == title && this.Options == options && this.Preferences == preferences)
        {
            return;
        }
        this.TimerInput = input;
        this.TimerTitle = title;
        this.Options = options;
        this.Preferences = preferences;
        this.commandRevision++;
    }

    internal TimerSessionSnapshot Snapshot(string id)
    {
        bool locked = this.Options.LockInterface && this.Countdown.State is TimerState.Running or TimerState.Paused;
        SessionActions actions = SessionActions.Rename;
        if (locked) { actions |= SessionActions.Unlock; }
        else
        {
            actions |= SessionActions.Update | SessionActions.Configure;
            actions |= this.Countdown.State switch
            {
                TimerState.Stopped => SessionActions.Start | SessionActions.Dismiss,
                TimerState.Expired => SessionActions.Start | SessionActions.Stop | SessionActions.Dismiss,
                TimerState.Running => SessionActions.Pause | SessionActions.Stop,
                TimerState.Paused => SessionActions.Resume | SessionActions.Stop,
                _ => SessionActions.None
            };
            if (this.Countdown.SupportsRestart) { actions |= SessionActions.Restart; }
        }
        return new(id, this.commandRevision, this.TimerInput, this.TimerTitle, this.Countdown, this.Options, actions, this.Preferences, this.PublicationSequence);
    }

    public TimerSession(CountdownEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        this.engine = engine;
        this.countdown = engine.Snapshot;
        this.engine.Started += this.OnStarted;
        this.engine.Paused += this.OnPaused;
        this.engine.Resumed += this.OnResumed;
        this.engine.Stopped += this.OnStopped;
        this.engine.Expired += this.OnExpired;
        this.engine.Tick += this.OnTicked;
    }

    public CountdownState Countdown => Volatile.Read(ref this.countdown);

    public long Revision => Interlocked.Read(ref this.revision);

    public bool IsCurrent(long expectedRevision) => !Volatile.Read(ref this.disposed) && this.Revision == expectedRevision;

    public CountdownTransition Start(TimerStart? start, DateTime now) => this.Apply(() => this.engine.Start(start, now));

    public CountdownTransition Restart(DateTime now) => this.Apply(() => this.engine.Restart(now));

    public CountdownTransition Pause() => this.Apply(this.engine.Pause);

    public CountdownTransition Resume(DateTime now) => this.Apply(() => this.engine.Resume(now));

    public CountdownTransition Stop() => this.Apply(this.engine.Stop);

    public CountdownTransition Tick() => this.Apply(this.engine.Update);

    public void Restore(TimerInfo timerInfo)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        this.engine.Restore(timerInfo);
        Volatile.Write(ref this.countdown, this.engine.Snapshot);
        Interlocked.Increment(ref this.revision);
        this.commandRevision++;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        Volatile.Write(ref this.disposed, true);
        Interlocked.Increment(ref this.revision);
        this.engine.Started -= this.OnStarted;
        this.engine.Paused -= this.OnPaused;
        this.engine.Resumed -= this.OnResumed;
        this.engine.Stopped -= this.OnStopped;
        this.engine.Expired -= this.OnExpired;
        this.engine.Tick -= this.OnTicked;
    }

    private CountdownTransition Apply(Action action) => this.Apply(() =>
    {
        action();
        return true;
    });

    private CountdownTransition Apply(Func<bool> action)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        this.pendingEffects = CountdownEffects.None;
        bool succeeded = action();
        Volatile.Write(ref this.countdown, this.engine.Snapshot);
        if (HasLifecycleEffect(this.pendingEffects.First) || HasLifecycleEffect(this.pendingEffects.Second)
            || HasLifecycleEffect(this.pendingEffects.Third) || HasLifecycleEffect(this.pendingEffects.Fourth))
        {
            Interlocked.Increment(ref this.revision);
            this.commandRevision++;
        }

        return new CountdownTransition(this.engine.Snapshot, this.pendingEffects, succeeded);
    }

    private static bool HasLifecycleEffect(CountdownEffect effect) => effect is not (CountdownEffect.None or CountdownEffect.Ticked);

    private void OnStarted(object? sender, EventArgs args) => this.Record(CountdownEffect.Started);
    private void OnPaused(object? sender, EventArgs args) => this.Record(CountdownEffect.Paused);
    private void OnResumed(object? sender, EventArgs args) => this.Record(CountdownEffect.Resumed);
    private void OnStopped(object? sender, EventArgs args) => this.Record(CountdownEffect.Stopped);
    private void OnExpired(object? sender, EventArgs args) => this.Record(CountdownEffect.Expired);
    private void OnTicked(object? sender, EventArgs args) => this.Record(CountdownEffect.Ticked);
    private void Record(CountdownEffect effect) => this.pendingEffects = this.pendingEffects.Append(effect);
}
