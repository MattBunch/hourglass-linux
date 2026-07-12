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
    public void RecordChangeDebouncesNotifications()
    {
        using var changed = new ManualResetEventSlim();
        int count = 0;
        var target = new RecordingTarget
        {
            Position = new PixelPoint(10, 10),
            Width = 420,
            Height = 240
        };
        using var controller = new DisposableGeometryController(target, () =>
        {
            Interlocked.Increment(ref count);
            changed.Set();
        });
        _ = controller.Controller.CurrentGeometry;

        target.Width = 430;
        controller.Controller.RecordChange();
        target.Width = 440;
        controller.Controller.RecordChange();

        Assert.True(changed.Wait(TimeSpan.FromSeconds(2)));
        Thread.Sleep(100);
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
    public void ValidatorLeavesIntersectingGeometryInPlace()
    {
        var geometry = new WindowGeometrySnapshot(1800, 900, 500, 300);

        WindowGeometrySnapshot validated = WindowGeometryValidator.Validate(
            geometry,
            [new WindowWorkArea(0, 0, 1920, 1080)]);

        Assert.Equal(geometry, validated);
    }

    private sealed class DisposableGeometryController : IDisposable
    {
        public DisposableGeometryController(RecordingTarget target, Action changed)
        {
            this.Controller = new WindowGeometryController(
                target,
                changed,
                () => [new WindowWorkArea(0, 0, 1920, 1080)]);
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

        public WindowState WindowState { get; set; }
    }
}
