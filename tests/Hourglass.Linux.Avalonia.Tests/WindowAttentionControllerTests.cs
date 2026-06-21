namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Controls;
using Xunit;

public sealed class WindowAttentionControllerTests
{
    [Fact]
    public void RequestAttentionShowsRestoresAndActivatesHiddenMinimizedWindow()
    {
        var target = new RecordingWindowTarget
        {
            IsVisible = false
        };
        target.SetInitialState(WindowState.Maximized);
        var controller = new WindowAttentionController(target);
        controller.RecordWindowState(target.WindowState);
        target.SetInitialState(WindowState.Minimized);

        controller.RequestAttention();

        Assert.Equal(1, target.ShowCount);
        Assert.Equal(WindowState.Maximized, target.WindowState);
        Assert.Equal(1, target.ActivateCount);
    }

    [Fact]
    public void RequestAttentionFallsBackToNormalWhenNoPriorStateWasRecorded()
    {
        var target = new RecordingWindowTarget
        {
            IsVisible = true
        };
        target.SetInitialState(WindowState.Minimized);
        var controller = new WindowAttentionController(target);

        controller.RequestAttention();

        Assert.Equal(0, target.ShowCount);
        Assert.Equal(WindowState.Normal, target.WindowState);
        Assert.Equal(1, target.ActivateCount);
    }

    [Fact]
    public void RequestAttentionIsBestEffortWhenWindowOperationsFail()
    {
        var target = new RecordingWindowTarget
        {
            IsVisible = false,
            ThrowOnShow = true,
            ThrowOnRestore = true,
            ThrowOnActivate = true
        };
        target.SetInitialState(WindowState.Minimized);
        var controller = new WindowAttentionController(target);

        Exception? exception = Record.Exception(controller.RequestAttention);

        Assert.Null(exception);
        Assert.Equal(1, target.ShowCount);
        Assert.Equal(1, target.RestoreCount);
        Assert.Equal(1, target.ActivateCount);
    }

    private sealed class RecordingWindowTarget : IWindowAttentionTarget
    {
        private WindowState windowState;

        public bool IsVisible { get; set; }

        public WindowState WindowState
        {
            get => this.windowState;
            set
            {
                this.RestoreCount++;

                if (this.ThrowOnRestore)
                {
                    throw new InvalidOperationException("Restore failed.");
                }

                this.windowState = value;
            }
        }

        public bool ThrowOnShow { get; init; }

        public bool ThrowOnRestore { get; init; }

        public bool ThrowOnActivate { get; init; }

        public int ShowCount { get; private set; }

        public int RestoreCount { get; private set; }

        public int ActivateCount { get; private set; }

        public void Show()
        {
            this.ShowCount++;

            if (this.ThrowOnShow)
            {
                throw new InvalidOperationException("Show failed.");
            }

            this.IsVisible = true;
        }

        public void Activate()
        {
            this.ActivateCount++;

            if (this.ThrowOnActivate)
            {
                throw new InvalidOperationException("Activate failed.");
            }
        }

        public void SetInitialState(WindowState state)
        {
            this.windowState = state;
        }
    }
}
