namespace Hourglass.Application;

using System.Threading.Channels;
using Hourglass.Platform;
using Hourglass.Timing;

public sealed record SessionNotification(TimerSessionSnapshot Snapshot, long Sequence, CountdownEffects Effects, bool Removed = false, bool RestoredExpiry = false);

/// <summary>An atomic initial snapshot and ordered, isolated notification delivery.</summary>
public sealed class SessionSubscription : IDisposable
{
    private readonly Channel<SessionNotification> updates = Channel.CreateUnbounded<SessionNotification>(new() { SingleReader = true });
    private readonly Func<SessionNotification, Task> publish;
    private readonly IDiagnosticSink diagnostics;
    private int disposed;
    private readonly object gate = new();
    private Task pendingDelivery = Task.CompletedTask;
    private readonly Dictionary<long, TaskCompletionSource> deliveries = [];
    public Task PendingDelivery { get { lock (this.gate) { return this.pendingDelivery; } } }
    internal bool IsDisposed => Volatile.Read(ref this.disposed) != 0;
    internal void Complete() => this.updates.Writer.TryComplete();

    internal SessionSubscription(TimerSessionSnapshot initialSnapshot, long sequence, Func<SessionNotification, Task> publish, IDiagnosticSink diagnostics)
    {
        this.InitialSnapshot = initialSnapshot;
        this.InitialSequence = sequence;
        this.publish = publish;
        this.diagnostics = diagnostics;
        this.Completion = Task.Run(this.DeliverAsync);
    }

    public Task Completion { get; }
    public TimerSessionSnapshot InitialSnapshot { get; }
    public long InitialSequence { get; }
    internal void Publish(SessionNotification notification)
    {
        lock (this.gate)
        {
            if (this.IsDisposed) { return; }
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.deliveries.Add(notification.Sequence, completion);
            this.pendingDelivery = completion.Task;
            if (!this.updates.Writer.TryWrite(notification)) { completion.TrySetResult(); this.deliveries.Remove(notification.Sequence); }
        }
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref this.disposed, 1);
        this.updates.Writer.TryComplete();
        lock (this.gate) { foreach (TaskCompletionSource completion in this.deliveries.Values) { completion.TrySetResult(); } this.deliveries.Clear(); }
    }
    private async Task DeliverAsync()
    {
        await foreach (SessionNotification notification in this.updates.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Volatile.Read(ref this.disposed) != 0) { break; }
            try { await this.publish(notification).ConfigureAwait(false); }
            catch (Exception exception)
            {
                this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort,
                    "runtime", "publish", "session", "Session subscriber failed.", exception));
            }
            finally
            {
                lock (this.gate) { if (this.deliveries.Remove(notification.Sequence, out TaskCompletionSource? completion)) { completion.TrySetResult(); } }
            }
        }
    }
}
