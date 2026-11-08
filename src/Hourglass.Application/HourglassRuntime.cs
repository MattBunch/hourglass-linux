namespace Hourglass.Application;

using System.Threading.Channels;
using Hourglass.Platform;
using Hourglass.Timing;

/// <summary>
/// Serializes short session mutations. Platform effects and frontend dispatch happen after the queue releases.
/// SessionRegistration is an internal migration bridge until the public client owns all session metadata.
/// </summary>
public sealed partial class HourglassRuntime : IHourglassClient, IDisposable, IAsyncDisposable
{
    private const int MaximumQueuedCommands = 256;
    private const int TickIntervalMilliseconds = 250;

    private readonly Channel<Action> commands = Channel.CreateBounded<Action>(new BoundedChannelOptions(MaximumQueuedCommands)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly TimerSessionManager sessions = new();
    private readonly CancellationTokenSource schedulerCancellation = new();
    private readonly object lifetimeGate = new();
    private readonly IDiagnosticSink diagnostics;
    private readonly TimeProvider timeProvider;
    private readonly IMonotonicClock clock;
    private readonly SessionRuntimeServices services;
    private readonly Func<DateTime> wallClockNow;
    private readonly List<Task> retiredEffects = [];
    private static readonly TimeSpan EffectDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly Task worker;
    private Task scheduler = Task.CompletedTask;
    private Task? shutdown;
    public Task EffectsCompletion { get; private set; } = Task.CompletedTask;
    internal bool IsStopping => Volatile.Read(ref this.stopping);
    private bool schedulerStarted;
    private bool stopping;
    private int executingThreadId;

    public HourglassRuntime(IDiagnosticSink? diagnostics = null, TimeProvider? timeProvider = null, IMonotonicClock? clock = null, Func<DateTime>? wallClockNow = null, SessionRuntimeServices? services = null, ISettingsStore? settingsStore = null)
    {
        this.diagnostics = diagnostics ?? NoOpDiagnosticSink.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        SessionRuntimeServices supplied = services ?? SessionRuntimeServices.Unsupported;
        this.sharedInhibitor = supplied.Inhibitor as CoordinatedSessionInhibitor ?? new CoordinatedSessionInhibitor(supplied.Inhibitor);
        this.services = supplied with { Inhibitor = this.sharedInhibitor };
        this.Data = new ApplicationData(settingsStore ?? new MemorySettingsStore(), this.diagnostics);
        this.clock = clock ?? new SystemMonotonicClock();
        this.wallClockNow = wallClockNow ?? (() => DateTime.Now);
        this.worker = Task.Run(this.ProcessCommandsAsync);
        this.Data.SettingsChanged += this.OnSettingsChanged;
    }

    internal TimerSession Register(string id, CountdownEngine engine, Action<SessionTick> publish)
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
            this.sessions.Add(id, new SessionRegistration(session, publish));
            return session;
        });
    }

    internal SessionEffects AttachEffects(string id, INotificationService notifications, IAudioAlertService audio,
        ISessionInhibitor inhibitor, ISystemPowerService power) => this.Invoke(() =>
    {
        SessionRegistration registration = this.sessions[id];
        SessionEffects effects = new(this, registration.Session, notifications, audio, inhibitor, power, this.diagnostics);
        this.sessions[id] = registration with { Effects = effects };
        return effects;
    });

    public void Remove(string id) => _ = this.RemoveAsync(id);

    public async Task RemoveAsync(string id)
    {
        Task cleanup;
        try
        {
            cleanup = this.Invoke(() =>
            {
                if (!this.sessions.Remove(id, out SessionRegistration? registration))
                {
                    return Task.CompletedTask;
                }

                if (registration.Authoritative) { this.Publish(registration, CountdownEffects.None, removed: true); }
                registration.Session.Dispose();
                Task effects = registration.Effects?.Close() ?? Task.CompletedTask;
                this.retiredEffects.RemoveAll(task => task.IsCompleted);
                this.retiredEffects.Add(effects);
                return effects;
            });
        }
        catch (ObjectDisposedException)
        {
            await this.DisposeAsync().ConfigureAwait(false);
            return;
        }

        await this.DrainAsync(cleanup, "remove-session").ConfigureAwait(false);
    }

    public async Task<bool> DrainAsync(Task cleanup, string operation)
    {
        try
        {
            await cleanup.WaitAsync(EffectDrainTimeout, this.timeProvider).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException exception)
        {
            this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort,
                "runtime", operation, "session-effects", "Effect drain timed out; cleanup remains active for late completions.", exception));
            _ = this.ObserveLateCleanupAsync(cleanup, operation);
            return false;
        }
    }

    private async Task ObserveLateCleanupAsync(Task cleanup, string operation)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort,
                "runtime", operation, "session-effects", "Late effect cleanup failed.", exception));
        }
    }

    public void SuspendTicks(string id) => this.Invoke(() =>
    {
        if (this.sessions.TryGetValue(id, out SessionRegistration? registration))
        {
            this.sessions[id] = registration with { TickEnabled = false };
        }

        return true;
    });

    internal T Invoke<T>(Func<T> command) => Volatile.Read(ref this.executingThreadId) == Environment.CurrentManagedThreadId
        ? command() : this.InvokeAsync(command).GetAwaiter().GetResult();

    internal async Task<T> InvokeAsync<T>(Func<T> command, CancellationToken cancellationToken = default)
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
                    T result = command();
                    foreach (SessionRegistration registration in this.sessions.Values)
                    {
                        registration.Effects?.SynchronizeRevision();
                    }
                    completion.TrySetResult(result);
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
        (SessionRegistration SessionRegistration, SessionTick Tick)[] updates = await this.InvokeAsync(() =>
        {
            List<(SessionRegistration, SessionTick)> updates = new(this.sessions.Count);
            foreach (SessionRegistration registration in this.sessions.Values)
            {
                if (!registration.TickEnabled)
                {
                    continue;
                }

                CountdownTransition transition = registration.Session.Tick();
                if (registration.Authoritative)
                {
                    this.ProcessTransition(registration, transition);
                    this.Publish(registration, transition.Effects);
                }
                updates.Add((registration, new SessionTick(transition, registration.Session.Revision)));
            }

            return updates.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        foreach ((SessionRegistration registration, SessionTick tick) in updates)
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

    public void Dispose() => _ = this.ObserveLateCleanupAsync(this.DisposeAsync().AsTask(), "dispose");

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
            Volatile.Write(ref this.executingThreadId, Environment.CurrentManagedThreadId);
            try { command(); }
            finally { Volatile.Write(ref this.executingThreadId, 0); }
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
        try
        {
            await this.schedulerCancellation.CancelAsync().ConfigureAwait(false);
            await this.scheduler.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.BestEffort,
                "runtime", "stop-scheduler", "session", "Runtime scheduler failed during shutdown.", exception));
        }
        finally
        {
            this.commands.Writer.TryComplete();
            await this.worker.ConfigureAwait(false);
            // The worker has stopped; no ownership decision can now overlap cleanup.
            foreach (SessionRegistration registration in this.sessions.Values)
            {
                foreach (SessionSubscription subscription in registration.Subscriptions) { subscription.Dispose(); }
                registration.Session.Dispose();
                if (registration.Effects != null)
                {
                    this.retiredEffects.Add(registration.Effects.Close());
                }
            }

            this.sessions.Clear();
            this.EffectsCompletion = Task.WhenAll(this.DisposeSharedInhibitorAsync(Task.WhenAll(this.retiredEffects)), this.DrainDataAsync(), this.DisposeWakeAlarmsAsync());
            await this.DrainAsync(this.EffectsCompletion, "shutdown").ConfigureAwait(false);
            this.schedulerCancellation.Dispose();
        }
    }

    private async Task DisposeSharedInhibitorAsync(Task effects)
    {
        await effects.ConfigureAwait(false);
        await this.sharedInhibitor.DisposeAsync().ConfigureAwait(false);
    }


}

internal sealed record SessionTick(CountdownTransition Transition, long Revision);
