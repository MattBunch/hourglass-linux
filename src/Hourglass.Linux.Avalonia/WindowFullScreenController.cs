using Avalonia.Controls;

namespace Hourglass.Linux.Avalonia;

internal interface IFullScreenWindowTarget
{
    WindowState WindowState { get; set; }
}

internal sealed class WindowFullScreenController(IFullScreenWindowTarget target)
{
    private readonly IFullScreenWindowTarget target = target ?? throw new ArgumentNullException(nameof(target));
    private WindowState restoreWindowState = WindowState.Normal;

    public bool IsFullScreen => this.TryGetWindowState() == WindowState.FullScreen;

    public WindowState RestoreWindowState => this.restoreWindowState;

    public void RecordWindowState(WindowState state)
    {
        if (state is WindowState.Normal or WindowState.Maximized)
        {
            this.restoreWindowState = state;
        }
    }

    public void Toggle()
    {
        WindowState? currentState = this.TryGetWindowState();

        if (currentState == WindowState.FullScreen)
        {
            this.TrySetWindowState(this.restoreWindowState);
            return;
        }

        if (currentState is WindowState.Normal or WindowState.Maximized)
        {
            this.restoreWindowState = currentState.Value;
        }

        this.TrySetWindowState(WindowState.FullScreen);
    }

    public bool TryExit()
    {
        if (!this.IsFullScreen)
        {
            return false;
        }

        this.TrySetWindowState(this.restoreWindowState);
        return true;
    }

    private WindowState? TryGetWindowState()
    {
        try
        {
            return this.target.WindowState;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void TrySetWindowState(WindowState state)
    {
        try
        {
            this.target.WindowState = state;
        }
        catch (Exception)
        {
        }
    }
}
