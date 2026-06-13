using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly CountdownEngine engine;
    private readonly Func<DateTime> wallClockNow;
    private string timerInput = "5 minutes";
    private string remainingTime = "00:00:00";
    private string statusText = "Ready";

    public MainWindowViewModel()
        : this(new CountdownEngine(new SystemMonotonicClock()), () => DateTime.Now)
    {
    }

    public MainWindowViewModel(CountdownEngine engine, Func<DateTime> wallClockNow)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.wallClockNow = wallClockNow ?? throw new ArgumentNullException(nameof(wallClockNow));
        this.engine.Expired += (_, _) => this.RefreshDisplay("Timer complete");

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
        get => this.timerInput;
        set
        {
            value ??= string.Empty;

            if (this.SetField(ref this.timerInput, value))
            {
                this.RefreshDisplay(this.engine.State == TimerState.Stopped ? "Ready" : this.statusText);
            }
        }
    }

    public string RemainingTime
    {
        get => this.remainingTime;
        private set => this.SetField(ref this.remainingTime, value);
    }

    public string StatusText
    {
        get => this.statusText;
        private set => this.SetField(ref this.statusText, value);
    }

    public string PauseResumeText => this.engine.State == TimerState.Paused ? "Resume" : "Pause";

    public bool IsInputEnabled => this.engine.State == TimerState.Stopped;

    public bool IsRunning => this.engine.State == TimerState.Running;

    public TimerState State => this.engine.State;

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
            this.RefreshDisplay("Enter a valid current timer.");
            return;
        }

        if (!this.engine.Start(timerStart, now))
        {
            this.RefreshDisplay("Enter a valid current timer.");
            return;
        }

        this.RefreshDisplay("Running");
    }

    private void PauseOrResume()
    {
        if (this.engine.State == TimerState.Running)
        {
            this.engine.Pause();
            this.RefreshDisplay("Paused");
            return;
        }

        if (this.engine.State == TimerState.Paused)
        {
            this.engine.Resume(this.wallClockNow());
            this.RefreshDisplay("Running");
        }
    }

    private void Reset()
    {
        this.engine.Stop();
        this.RefreshDisplay("Ready");
    }

    private void RefreshDisplay(string? explicitStatus = null)
    {
        this.RemainingTime = FormatRemainingTime(this.engine.TimeLeft ?? TimeSpan.Zero);
        this.StatusText = explicitStatus ?? this.GetStatusText();

        this.OnPropertyChanged(nameof(this.PauseResumeText));
        this.OnPropertyChanged(nameof(this.IsInputEnabled));
        this.OnPropertyChanged(nameof(this.IsRunning));
        this.OnPropertyChanged(nameof(this.State));
        this.StartCommand.RaiseCanExecuteChanged();
        this.PauseResumeCommand.RaiseCanExecuteChanged();
        this.ResetCommand.RaiseCanExecuteChanged();
    }

    private string GetStatusText()
    {
        return this.engine.State switch
        {
            TimerState.Running => "Running",
            TimerState.Paused => "Paused",
            TimerState.Expired => "Timer complete",
            _ => "Ready"
        };
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        int hours = (int)Math.Floor(remaining.TotalHours);
        return FormattableString.Invariant($"{hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}");
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
