namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Linux.Avalonia;
using Hourglass.Timing;
using Xunit;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void StartWithValidInputRunsTimer()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "90 seconds";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("Running", viewModel.StatusText);
        Assert.Equal("00:01:30", viewModel.RemainingTime);
        Assert.False(viewModel.IsInputEnabled);
        Assert.True(viewModel.IsRunning);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.True(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void StartWithInvalidInputKeepsTimerStopped()
    {
        var viewModel = CreateViewModel(new ManualMonotonicClock());

        viewModel.TimerInput = "not a timer";
        viewModel.StartCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Enter a valid current timer.", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTime()
    {
        var clock = new ManualMonotonicClock();
        DateTime now = new(2026, 6, 8, 10, 0, 0);
        var viewModel = CreateViewModel(clock, () => now);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.Tick();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal("00:00:07", viewModel.RemainingTime);
        Assert.Equal("Resume", viewModel.PauseResumeText);

        now = now.AddSeconds(8);
        viewModel.PauseResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Running, viewModel.State);
        Assert.Equal("00:00:05", viewModel.RemainingTime);
        Assert.Equal("Pause", viewModel.PauseResumeText);
    }

    [Fact]
    public void ResetReturnsTimerToReadyState()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "10 seconds";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3));
        viewModel.Tick();

        viewModel.ResetCommand.Execute(null);

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.True(viewModel.IsInputEnabled);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.PauseResumeCommand.CanExecute(null));
        Assert.False(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void TickTransitionsExpiredTimerToCompleteDisplay()
    {
        var clock = new ManualMonotonicClock();
        var viewModel = CreateViewModel(clock);

        viewModel.TimerInput = "1 second";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        viewModel.Tick();

        Assert.Equal(TimerState.Expired, viewModel.State);
        Assert.Equal("Timer complete", viewModel.StatusText);
        Assert.Equal("00:00:00", viewModel.RemainingTime);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsInputEnabled);
    }

    [Theory]
    [InlineData(TimerState.Stopped, "Ready", "Pause", true, false, "00:00:00")]
    [InlineData(TimerState.Running, "Running", "Pause", false, true, "00:01:05")]
    [InlineData(TimerState.Paused, "Paused", "Resume", false, false, "00:01:05")]
    [InlineData(TimerState.Expired, "Timer complete", "Pause", false, false, "00:00:00")]
    public void TimerViewStateProjectsDomainState(
        TimerState state,
        string statusText,
        string pauseResumeText,
        bool isInputEnabled,
        bool isRunning,
        string remainingTime)
    {
        CountdownState countdownState = CreateCountdownState(state);

        TimerViewState viewState = TimerViewState.FromTimerState("65 seconds", countdownState);

        Assert.Equal(remainingTime, viewState.RemainingTime);
        Assert.Equal(statusText, viewState.StatusText);
        Assert.Equal(pauseResumeText, viewState.PauseResumeText);
        Assert.Equal(isInputEnabled, viewState.IsInputEnabled);
        Assert.Equal(isRunning, viewState.IsRunning);
        Assert.Equal(state, viewState.State);
    }

    private static CountdownState CreateCountdownState(TimerState state)
    {
        DateTime start = new(2026, 6, 8, 10, 0, 0);
        CountdownState running = CountdownTransitions.StartDuration(
            CountdownState.Stopped,
            TimeSpan.FromSeconds(65),
            start,
            TimeSpan.Zero).State;

        return state switch
        {
            TimerState.Stopped => CountdownState.Stopped,
            TimerState.Running => running,
            TimerState.Paused => CountdownTransitions.Pause(running, TimeSpan.Zero).State,
            TimerState.Expired => CountdownTransitions.Tick(running, TimeSpan.FromSeconds(65)).State,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static MainWindowViewModel CreateViewModel(
        ManualMonotonicClock clock,
        Func<DateTime>? wallClockNow = null)
    {
        return new MainWindowViewModel(
            new CountdownEngine(clock),
            wallClockNow ?? (() => new DateTime(2026, 6, 8, 10, 0, 0)));
    }

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan elapsed)
        {
            this.Elapsed += elapsed;
        }
    }
}
