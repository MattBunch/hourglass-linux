namespace Hourglass.Application;

using Hourglass.Serialization;
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
