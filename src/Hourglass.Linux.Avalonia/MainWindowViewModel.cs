using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string InvalidTimerStatusText = "Enter a valid current timer.";

    private readonly CountdownEngine engine;
    private readonly Func<DateTime> wallClockNow;
    private TimerViewState viewState = TimerViewState.Initial;

    public MainWindowViewModel()
        : this(new CountdownEngine(new SystemMonotonicClock()), () => DateTime.Now)
    {
    }

    public MainWindowViewModel(CountdownEngine engine, Func<DateTime> wallClockNow)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
        this.engine.Expired += this.OnEngineExpired;

        this.StartCommand = new RelayCommand(this.Start, () => this.engine.State == TimerState.Stopped);
        this.PauseResumeCommand = new RelayCommand(
            this.PauseOrResume,
            () => this.engine.State is TimerState.Running or TimerState.Paused);
        this.ResetCommand = new RelayCommand(this.Reset, () => this.engine.State != TimerState.Stopped);

        this.RefreshDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public RelayCommand ResetCommand { get; }

    public string TimerInput
    {
        get => this.viewState.TimerInput;
        set
        {
            value ??= string.Empty;

            if (value != this.viewState.TimerInput)
            {
                this.ReplaceViewState(this.viewState with { TimerInput = value });
                this.RefreshDisplay(this.engine.State == TimerState.Stopped ? TimerViewState.ReadyStatusText : this.StatusText);
            }
        }
    }

    public string RemainingTime => this.viewState.RemainingTime;

    public string StatusText => this.viewState.StatusText;

    public string PauseResumeText => this.viewState.PauseResumeText;

    public bool IsInputEnabled => this.viewState.IsInputEnabled;

    public bool IsRunning => this.viewState.IsRunning;

    public TimerState State => this.viewState.State;

    public void Dispose()
    {
        this.engine.Expired -= this.OnEngineExpired;
    }

    public void Tick()
    {
        this.engine.Update();
        this.RefreshDisplay();
    }

    private void Start()
    {
        DateTime now = this.wallClockNow();
        TimerStart? timerStart = TimerStart.FromString(this.TimerInput);

        if (timerStart == null || !timerStart.IsValid || !timerStart.TryGetEndTime(now, out DateTime endTime) || endTime < now)
        {
            this.RefreshDisplay(InvalidTimerStatusText);
            return;
        }

        if (!this.engine.Start(timerStart, now))
        {
            this.RefreshDisplay(InvalidTimerStatusText);
            return;
        }

        this.RefreshDisplay(TimerViewState.RunningStatusText);
    }

    private void PauseOrResume()
    {
        if (this.engine.State == TimerState.Running)
        {
            this.engine.Pause();
            this.RefreshDisplay(TimerViewState.PausedStatusText);
            return;
        }

        if (this.engine.State == TimerState.Paused)
        {
            this.engine.Resume(this.wallClockNow());
            this.RefreshDisplay(TimerViewState.RunningStatusText);
        }
    }

    private void Reset()
    {
        this.engine.Stop();
        this.RefreshDisplay(TimerViewState.ReadyStatusText);
    }

    private void RefreshDisplay(string? explicitStatus = null)
    {
        this.ReplaceViewState(TimerViewState.FromTimerState(this.TimerInput, this.engine.Snapshot, explicitStatus));
        this.OnPropertyChanged(nameof(this.TimerInput));
        this.OnPropertyChanged(nameof(this.RemainingTime));
        this.OnPropertyChanged(nameof(this.StatusText));
        this.OnPropertyChanged(nameof(this.PauseResumeText));
        this.OnPropertyChanged(nameof(this.IsInputEnabled));
        this.OnPropertyChanged(nameof(this.IsRunning));
        this.OnPropertyChanged(nameof(this.State));
        this.StartCommand.RaiseCanExecuteChanged();
        this.PauseResumeCommand.RaiseCanExecuteChanged();
        this.ResetCommand.RaiseCanExecuteChanged();
    }

    private void ReplaceViewState(TimerViewState next, string? changedPropertyName = null)
    {
        if (this.viewState == next)
        {
            return;
        }

        this.viewState = next;
        this.OnPropertyChanged(changedPropertyName);
    }

    private void OnEngineExpired(object? sender, EventArgs e)
    {
        this.RefreshDisplay(TimerViewState.TimerCompleteStatusText);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
