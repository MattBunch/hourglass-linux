namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia;
using global::Avalonia.Controls;
using Hourglass.Settings;
using Xunit;

public sealed class WindowGeometryControllerTests
{
    [Fact]
    public void CurrentGeometryCapturesNormalBounds()
    {
        var target = new RecordingTarget
        {
            Position = new PixelPoint(12, 34),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () => { });

        WindowGeometrySnapshot? geometry = controller.Controller.CurrentGeometry;

        Assert.NotNull(geometry);
        Assert.Equal(12, geometry.X);
        Assert.Equal(34, geometry.Y);
        Assert.Equal(420, geometry.Width);
        Assert.Equal(240, geometry.Height);
        Assert.Equal(WindowGeometryState.Normal, geometry.State);
    }

    [Fact]
    public void CurrentGeometryCapturesPositionInDeviceIndependentUnits()
    {
        var target = new RecordingTarget
        {
            Position = new PixelPoint(200, 100),
            Width = 420,
            Height = 240,
            RenderScaling = 2
        };
        using var controller = new DisposableGeometryController(target, () => { });

        WindowGeometrySnapshot? geometry = controller.Controller.CurrentGeometry;

        Assert.NotNull(geometry);
        Assert.Equal(100, geometry.X);
        Assert.Equal(50, geometry.Y);
        Assert.Equal(420, geometry.Width);
        Assert.Equal(240, geometry.Height);
        Assert.Equal(WindowGeometryState.Normal, geometry.State);
    }

    [Fact]
    public void MinimizedStateKeepsLastNormalGeometry()
    {
        var target = new RecordingTarget
        {
            Position = new PixelPoint(12, 34),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () => { });
        _ = controller.Controller.CurrentGeometry;

        target.WindowState = WindowState.Minimized;
        target.Position = new PixelPoint(2000, 2000);
        target.Width = 800;
        target.Height = 600;

        WindowGeometrySnapshot? geometry = controller.Controller.CurrentGeometry;

        Assert.NotNull(geometry);
        Assert.Equal(12, geometry.X);
        Assert.Equal(34, geometry.Y);
        Assert.Equal(420, geometry.Width);
        Assert.Equal(240, geometry.Height);
        Assert.Equal(WindowGeometryState.Normal, geometry.State);
    }

    [Fact]
    public void FullScreenStateIsNotPersistedAsStartupGeometry()
    {
        var target = new RecordingTarget
        {
            Position = new PixelPoint(12, 34),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () => { });
        _ = controller.Controller.CurrentGeometry;

        target.WindowState = WindowState.FullScreen;
        target.Position = new PixelPoint(2000, 2000);
        target.Width = 800;
        target.Height = 600;

        WindowGeometrySnapshot? geometry = controller.Controller.CurrentGeometry;

        Assert.NotNull(geometry);
        Assert.Equal(12, geometry.X);
        Assert.Equal(34, geometry.Y);
        Assert.Equal(420, geometry.Width);
        Assert.Equal(240, geometry.Height);
        Assert.Equal(WindowGeometryState.Normal, geometry.State);
    }

    [Fact]
    public void MaximizedStatePersistsWithLastNormalBounds()
    {
        var target = new RecordingTarget
        {
            Position = new PixelPoint(12, 34),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () => { });
        _ = controller.Controller.CurrentGeometry;

        target.WindowState = WindowState.Maximized;

        WindowGeometrySnapshot? geometry = controller.Controller.CurrentGeometry;

        Assert.NotNull(geometry);
        Assert.Equal(12, geometry.X);
        Assert.Equal(34, geometry.Y);
        Assert.Equal(420, geometry.Width);
        Assert.Equal(240, geometry.Height);
        Assert.Equal(WindowGeometryState.Maximized, geometry.State);
    }

    [Fact]
    public void ApplyRestoresValidatedGeometry()
    {
        var target = new RecordingTarget();
        using var controller = new DisposableGeometryController(target, () => { });

        controller.Controller.Apply(new WindowGeometrySnapshot(25, 35, 500, 300, WindowGeometryState.Maximized));

        Assert.Equal(new PixelPoint(25, 35), target.Position);
        Assert.Equal(500, target.Width);
        Assert.Equal(300, target.Height);
        Assert.Equal(WindowState.Maximized, target.WindowState);
    }

    [Fact]
    public void ApplyRestoresPositionInPhysicalPixels()
    {
        var target = new RecordingTarget
        {
            RenderScaling = 2
        };
        using var controller = new DisposableGeometryController(target, () => { });

        controller.Controller.Apply(new WindowGeometrySnapshot(25, 35, 500, 300));

        Assert.Equal(new PixelPoint(50, 70), target.Position);
        Assert.Equal(500, target.Width);
        Assert.Equal(300, target.Height);
        Assert.Equal(WindowState.Normal, target.WindowState);
    }

    [Fact]
    public void RecordChangeDebouncesNotifications()
    {
        int count = 0;
        var debounceTimer = new FakeDebounceTimer();
        var target = new RecordingTarget
        {
            Position = new PixelPoint(10, 10),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () =>
        {
            Interlocked.Increment(ref count);
        }, debounceTimer);
        _ = controller.Controller.CurrentGeometry;

        target.Width = 430;
        controller.Controller.RecordChange();
        target.Width = 440;
        controller.Controller.RecordChange();

        Assert.NotNull(debounceTimer.PendingCallback);
        debounceTimer.Trigger();
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void ValidatorMovesOffscreenGeometryIntoWorkArea()
    {
        var geometry = new WindowGeometrySnapshot(4000, 3000, 500, 300);

        WindowGeometrySnapshot validated = WindowGeometryValidator.Validate(
            geometry,
            [new WindowWorkArea(0, 0, 1920, 1080)]);

        Assert.Equal(1420, validated.X);
        Assert.Equal(780, validated.Y);
        Assert.Equal(500, validated.Width);
        Assert.Equal(300, validated.Height);
    }

    [Fact]
    public void ValidatorFitsOversizedOffscreenGeometryToSelectedWorkArea()
    {
        var geometry = new WindowGeometrySnapshot(6400, 5000, 2500, 2500);

        WindowGeometrySnapshot validated = WindowGeometryValidator.Validate(
            geometry,
            [
                new WindowWorkArea(0, 0, 3000, 400),
                new WindowWorkArea(5000, 0, 800, 3000)
            ]);

        Assert.Equal(5000, validated.X);
        Assert.Equal(500, validated.Y);
        Assert.Equal(800, validated.Width);
        Assert.Equal(2500, validated.Height);
    }

    [Fact]
    public void ValidatorLeavesIntersectingGeometryInPlace()
    {
        var geometry = new WindowGeometrySnapshot(1800, 900, 500, 300);

        WindowGeometrySnapshot validated = WindowGeometryValidator.Validate(
            geometry,
            [new WindowWorkArea(0, 0, 1920, 1080)]);

        Assert.Equal(geometry, validated);
    }

    [Fact]
    public void WorkAreaConversionUsesDeviceIndependentUnits()
    {
        WindowWorkArea converted = WindowGeometryController.ToDeviceIndependentWorkArea(
            new WindowWorkArea(100, 50, 3840, 2160),
            renderScaling: 2);

        Assert.Equal(50, converted.X);
        Assert.Equal(25, converted.Y);
        Assert.Equal(1920, converted.Width);
        Assert.Equal(1080, converted.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRenderScalingFallsBackToOne(double renderScaling)
    {
        Assert.Equal(1, WindowGeometryController.NormalizeRenderScaling(renderScaling));
    }

    private sealed class DisposableGeometryController : IDisposable
    {
        public DisposableGeometryController(
            RecordingTarget target,
            Action changed,
            IWindowGeometryDebounceTimer? debounceTimer = null)
        {
            this.Controller = new WindowGeometryController(
                target,
                changed,
                () => [new WindowWorkArea(0, 0, 1920, 1080)],
                debounceTimer);
        }

        public WindowGeometryController Controller { get; }

        public void Dispose()
        {
            this.Controller.Dispose();
        }
    }

    private sealed class RecordingTarget : IWindowGeometryTarget
    {
        public PixelPoint Position { get; set; }

        public double Width { get; set; } = 350;

        public double Height { get; set; } = 150;

        public double RenderScaling { get; set; } = 1;

        public WindowState WindowState { get; set; }
    }

    private sealed class FakeDebounceTimer : IWindowGeometryDebounceTimer
    {
        public Action? PendingCallback { get; private set; }

        public void Schedule(TimeSpan delay, Action callback)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(500), delay);
            this.PendingCallback = callback;
        }

        public void Trigger()
        {
            Action? callback = this.PendingCallback;
            this.PendingCallback = null;
            callback?.Invoke();
        }

        public void Dispose()
        {
        }
    }
}
