namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Controls;
using Xunit;

public sealed class WindowFullScreenControllerTests
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void ToggleRestoresPreviousNonFullScreenState(WindowState initialState)
    {
        var target = new RecordingTarget(initialState);
        var controller = new WindowFullScreenController(target);

        controller.RecordWindowState(initialState);
        controller.Toggle();
        Assert.Equal(WindowState.FullScreen, target.WindowState);
        Assert.True(controller.IsFullScreen);

        controller.Toggle();
        Assert.Equal(initialState, target.WindowState);
        Assert.False(controller.IsFullScreen);
    }

    [Fact]
    public void EscapeExitOnlyHandlesFullScreenState()
    {
        var target = new RecordingTarget(WindowState.Normal);
        var controller = new WindowFullScreenController(target);

        Assert.False(controller.TryExit());
        controller.Toggle();
        Assert.True(controller.TryExit());
        Assert.Equal(WindowState.Normal, target.WindowState);
    }

    [Fact]
    public void StateFailuresAreIsolated()
    {
        var target = new RecordingTarget(WindowState.Normal) { ThrowOnAccess = true };
        var controller = new WindowFullScreenController(target);

        Exception? exception = Record.Exception(() =>
        {
            controller.Toggle();
            controller.TryExit();
        });

        Assert.Null(exception);
    }

    private sealed class RecordingTarget(WindowState windowState) : IFullScreenWindowTarget
    {
        private WindowState windowState = windowState;

        public bool ThrowOnAccess { get; init; }

        public WindowState WindowState
        {
            get => this.ThrowOnAccess ? throw new InvalidOperationException() : this.windowState;
            set
            {
                if (this.ThrowOnAccess)
                {
                    throw new InvalidOperationException();
                }

                this.windowState = value;
            }
        }
    }
}
