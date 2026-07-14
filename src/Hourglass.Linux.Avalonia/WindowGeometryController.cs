using Avalonia;
using Avalonia.Controls;
using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

internal interface IWindowGeometryTarget
{
    PixelPoint Position { get; set; }

    double Width { get; set; }

    double Height { get; set; }

    WindowState WindowState { get; set; }
}

internal interface IWindowGeometryDebounceTimer : IDisposable
{
    void Schedule(TimeSpan delay, Action callback);
}

internal sealed class WindowGeometryController
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IWindowGeometryTarget target;
    private readonly Action geometryChanged;
    private readonly Func<IReadOnlyList<WindowWorkArea>> getWorkAreas;
    private readonly IWindowGeometryDebounceTimer saveTimer;
    private WindowGeometrySnapshot? lastNormalGeometry;
    private WindowGeometryState lastRestorableState = WindowGeometryState.Normal;
    private bool applyingGeometry;
    private bool disposed;

    public WindowGeometryController(
        IWindowGeometryTarget target,
        Action geometryChanged,
        Func<IReadOnlyList<WindowWorkArea>> getWorkAreas,
        IWindowGeometryDebounceTimer? saveTimer = null)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.geometryChanged = geometryChanged ?? throw new ArgumentNullException(nameof(geometryChanged));
        this.getWorkAreas = getWorkAreas ?? throw new ArgumentNullException(nameof(getWorkAreas));
        this.saveTimer = saveTimer ?? new ThreadingWindowGeometryDebounceTimer();
    }

    public WindowGeometrySnapshot? CurrentGeometry
    {
        get
        {
            this.Capture();
            return this.GetSnapshot();
        }
    }

    public void Apply(WindowGeometrySnapshot? geometry)
    {
        if (geometry == null)
        {
            this.Capture();
            return;
        }

        WindowGeometrySnapshot validated = WindowGeometryValidator.Validate(geometry, this.getWorkAreas());
        this.applyingGeometry = true;
        try
        {
            this.target.Position = new PixelPoint(
                (int)Math.Round(validated.X),
                (int)Math.Round(validated.Y));
            this.target.Width = validated.Width;
            this.target.Height = validated.Height;
            this.target.WindowState = validated.State == WindowGeometryState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }
        finally
        {
            this.applyingGeometry = false;
        }

        this.lastNormalGeometry = validated with { State = WindowGeometryState.Normal };
        this.lastRestorableState = validated.State;
    }

    public void RecordChange()
    {
        if (this.applyingGeometry || this.disposed)
        {
            return;
        }

        WindowGeometrySnapshot? previous = this.GetSnapshot();
        this.Capture();
        if (previous == this.GetSnapshot())
        {
            return;
        }

        this.saveTimer.Schedule(SaveDelay, this.PublishPendingChange);
    }

    public void Dispose()
    {
        this.disposed = true;
        this.saveTimer.Dispose();
    }

    private void Capture()
    {
        WindowState state = this.target.WindowState;
        if (state == WindowState.FullScreen)
        {
            return;
        }

        if (state == WindowState.Maximized)
        {
            this.lastRestorableState = WindowGeometryState.Maximized;
            return;
        }

        if (state == WindowState.Minimized)
        {
            return;
        }

        this.lastRestorableState = WindowGeometryState.Normal;
        this.lastNormalGeometry = new WindowGeometrySnapshot(
            this.target.Position.X,
            this.target.Position.Y,
            this.target.Width,
            this.target.Height,
            WindowGeometryState.Normal);
    }

    private WindowGeometrySnapshot? GetSnapshot()
    {
        if (this.lastNormalGeometry == null)
        {
            return null;
        }

        return this.lastNormalGeometry with { State = this.lastRestorableState };
    }

    private void PublishPendingChange()
    {
        if (!this.disposed)
        {
            this.geometryChanged();
        }
    }

    private sealed class ThreadingWindowGeometryDebounceTimer : IWindowGeometryDebounceTimer
    {
        private readonly Timer timer;
        private Action? callback;

        public ThreadingWindowGeometryDebounceTimer()
        {
            this.timer = new Timer(this.PublishPendingChange, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Schedule(TimeSpan delay, Action callback)
        {
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            this.timer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            this.timer.Dispose();
        }

        private void PublishPendingChange(object? state)
        {
            this.callback?.Invoke();
        }
    }
}
