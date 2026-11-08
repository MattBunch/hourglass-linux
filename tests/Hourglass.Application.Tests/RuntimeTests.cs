namespace Hourglass.Application.Tests;

using Hourglass.Timing;
using Xunit;

public sealed class RuntimeTests
{
    [Fact]
    public async Task RuntimeTicksSeveralSessionsWithoutWindows()
    {
        using HourglassRuntime runtime = new();
        Clock clock = new();
        List<SessionTick> firstUpdates = [];
        List<SessionTick> secondUpdates = [];
        TimerSession first = runtime.Register("first", new CountdownEngine(clock), firstUpdates.Add);
        TimerSession second = runtime.Register("second", new CountdownEngine(clock), secondUpdates.Add);
        DateTime now = new(2026, 1, 1);
        runtime.Invoke(() => first.Start(TimerStart.FromString("1 second"), now));
        runtime.Invoke(() => second.Start(TimerStart.FromString("2 seconds"), now));
        clock.Elapsed = TimeSpan.FromSeconds(1);

        await runtime.TickAsync();

        Assert.Equal(TimerState.Expired, firstUpdates.Single().Transition.State.State);
        Assert.Equal(TimerState.Running, secondUpdates.Single().Transition.State.State);
        Assert.Equal(TimeSpan.FromSeconds(1), second.Countdown.TimeLeft);
    }

    [Fact]
    public async Task ConcurrentCommandsExecuteSerially()
    {
        using HourglassRuntime runtime = new();
        int counter = 0;
        Task<int>[] commands = Enumerable.Range(0, 100).Select(_ => runtime.InvokeAsync(() => ++counter)).ToArray();
        int[] results = await Task.WhenAll(commands);
        Assert.Equal(Enumerable.Range(1, 100), results);
    }

    [Fact]
    public async Task SubscriberCanIssueCommandWithoutDeadlockingQueue()
    {
        using HourglassRuntime runtime = new();
        TimerSession session = runtime.Register("timer", new CountdownEngine(new Clock()), _ => runtime.Invoke(() => 1));
        await runtime.TickAsync();
        Assert.Equal(TimerState.Stopped, session.Countdown.State);
    }

    [Fact]
    public async Task BrokenSubscriberDoesNotStopOtherSessions()
    {
        using HourglassRuntime runtime = new();
        runtime.Register("broken", new CountdownEngine(new Clock()), _ => throw new InvalidOperationException("broken frontend"));
        int delivered = 0;
        runtime.Register("healthy", new CountdownEngine(new Clock()), _ => delivered++);

        await runtime.TickAsync();
        await runtime.TickAsync();

        Assert.Equal(2, delivered);
    }

    [Fact]
    public async Task CancellationBeforeEnqueueDoesNotMutate()
    {
        using HourglassRuntime runtime = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool invoked = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.InvokeAsync(() => invoked = true, cancellation.Token));
        Assert.False(invoked);
    }

    [Fact]
    public async Task DisposalDrainsAcceptedCommandsAndRejectsNewCommands()
    {
        HourglassRuntime runtime = new();
        Task<int> accepted = runtime.InvokeAsync(() => 42);
        await runtime.DisposeAsync();
        Assert.Equal(42, await accepted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.InvokeAsync(() => 1));
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task RemovedSessionReceivesNoMoreTicks()
    {
        using HourglassRuntime runtime = new();
        int delivered = 0;
        runtime.Register("timer", new CountdownEngine(new Clock()), _ => delivered++);
        runtime.Remove("timer");
        await runtime.TickAsync();
        Assert.Equal(0, delivered);
    }

    private sealed class Clock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; set; }
    }
}
