namespace Hourglass.Application;

using System.Threading.Channels;
using Hourglass.Platform;
using Hourglass.Timing;

/// <summary>
/// Serializes short session mutations. Platform effects and frontend dispatch happen after the queue releases.
/// Registration is an internal migration bridge until the public client owns all session metadata.
/// </summary>
internal sealed class HourglassRuntime : IDisposable, IAsyncDisposable
{
    private const int MaximumQueuedCommands = 256;
    private const int TickIntervalMilliseconds = 250;

    private readonly Channel<Action> commands = Channel.CreateBounded<Action>(new BoundedChannelOptions(MaximumQueuedCommands)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Dictionary<string, Registration> sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource schedulerCancellation = new();
    private readonly object lifetimeGate = new();
    private readonly IDiagnosticSink diagnostics;
    private readonly Task worker;
    private Task scheduler = Task.CompletedTask;
    private Task? shutdown;
    private bool schedulerStarted;
    private bool stopping;

    public HourglassRuntime(IDiagnosticSink? diagnostics = null)
    {
        this.diagnostics = diagnostics ?? NoOpDiagnosticSink.Instance;
        this.worker = Task.Run(this.ProcessCommandsAsync);
    }

    public TimerSession Register(string id, CountdownEngine engine, Action<SessionTick> publish)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(publish);
        return this.Invoke(() =>
        {
            if (this.sessions.ContainsKey(id))
            {
                throw new InvalidOperationException($"Session '{id}' is already registered.");
            }

            TimerSession session = new(engine);
            this.sessions.Add(id, new Registration(session, publish));
            return session;
        });
    }

    public void Remove(string id)
    {
        if (Volatile.Read(ref this.stopping))
        {
            return;
        }

        this.Invoke(() =>
        {
            if (this.sessions.Remove(id, out Registration? registration))
            {
                registration.Session.Dispose();
            }

            return true;
        });
    }

    public void SuspendTicks(string id) => this.Invoke(() =>
    {
        if (this.sessions.TryGetValue(id, out Registration? registration))
        {
            this.sessions[id] = registration with { TickEnabled = false };
        }

        return true;
    });

    public T Invoke<T>(Func<T> command) => this.InvokeAsync(command).GetAwaiter().GetResult();

    public async Task<T> InvokeAsync<T>(Func<T> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.stopping), this);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await this.commands.Writer.WriteAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(command());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new ObjectDisposedException(nameof(HourglassRuntime));
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public void StartScheduler()
    {
        lock (this.lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(this.stopping, this);
            if (!this.schedulerStarted)
            {
                this.schedulerStarted = true;
                this.scheduler = Task.Run(() => this.RunSchedulerAsync(this.schedulerCancellation.Token));
            }
        }
    }

    // Tests and recording advance injected clocks and call this seam without real delays.
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        (Registration Registration, SessionTick Tick)[] updates = await this.InvokeAsync(() =>
        {
            List<(Registration, SessionTick)> updates = new(this.sessions.Count);
            foreach (Registration registration in this.sessions.Values)
            {
                if (!registration.TickEnabled)
                {
                    continue;
                }

                CountdownTransition transition = registration.Session.Tick();
                updates.Add((registration, new SessionTick(transition, registration.Session.Revision)));
            }

            return updates.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        foreach ((Registration registration, SessionTick tick) in updates)
        {
            if (!registration.Session.IsCurrent(tick.Revision))
            {
                continue;
            }

            try
            {
                registration.Publish(tick);
            }
            catch (Exception exception)
            {
                this.diagnostics.TryRecord(new DiagnosticEvent(DiagnosticSeverity.Warning,
                    DiagnosticFailureClass.BestEffort, "runtime", "publish", "session",
                    "Session snapshot publication failed.", exception));
            }
        }
    }

    public void Dispose() => this.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (this.lifetimeGate)
        {
            if (this.shutdown == null)
            {
                Volatile.Write(ref this.stopping, true);
                this.shutdown = this.ShutdownAsync();
            }

            return new ValueTask(this.shutdown);
        }
    }

    private async Task ProcessCommandsAsync()
    {
        await foreach (Action command in this.commands.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            command();
        }
    }

    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(TickIntervalMilliseconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await this.TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref this.stopping))
        {
        }
    }

    private async Task ShutdownAsync()
    {
        await this.schedulerCancellation.CancelAsync().ConfigureAwait(false);
        await this.scheduler.ConfigureAwait(false);
        this.commands.Writer.TryComplete();
        await this.worker.ConfigureAwait(false);
        foreach (Registration registration in this.sessions.Values)
        {
            registration.Session.Dispose();
        }

        this.sessions.Clear();
        this.schedulerCancellation.Dispose();
    }

    private sealed record Registration(TimerSession Session, Action<SessionTick> Publish, bool TickEnabled = true);
}

internal sealed record SessionTick(CountdownTransition Transition, long Revision);
