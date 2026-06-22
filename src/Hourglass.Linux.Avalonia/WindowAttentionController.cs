using Avalonia.Controls;

namespace Hourglass.Linux.Avalonia;

internal interface IWindowAttentionTarget
{
    bool IsVisible { get; }

    WindowState WindowState { get; set; }

    void Hide();

    void Show();

    void Activate();
}

internal sealed class WindowAttentionController(IWindowAttentionTarget target)
{
    private readonly IWindowAttentionTarget target = target ?? throw new ArgumentNullException(nameof(target));
    private WindowState restoreWindowState = WindowState.Normal;

    public WindowState RestoreWindowState => this.restoreWindowState;

    public void RecordWindowState(WindowState state)
    {
        if (state == WindowState.Normal || state == WindowState.Maximized)
        {
            this.restoreWindowState = state;
        }
    }

    public void RequestAttention()
    {
        WindowState? currentState = this.TryGetWindowState();

        if (!this.TryGetIsVisible())
        {
            this.TryShow();
        }

        if (currentState == WindowState.Minimized)
        {
            this.TryRestore();

            if (this.TryGetWindowState() == WindowState.Minimized)
            {
                this.TryRemapAndRestore();
            }
        }

        this.TryActivate();
    }

    private bool TryGetIsVisible()
    {
        try
        {
            return this.target.IsVisible;
        }
        catch (Exception)
        {
            return true;
        }
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

    private void TryShow()
    {
        try
        {
            this.target.Show();
        }
        catch (Exception)
        {
        }
    }

    private void TryHide()
    {
        try
        {
            this.target.Hide();
        }
        catch (Exception)
        {
        }
    }

    private void TryRestore()
    {
        try
        {
            this.target.WindowState = this.restoreWindowState;
        }
        catch (Exception)
        {
        }
    }

    private void TryRemapAndRestore()
    {
        this.TryHide();
        this.TryRestore();
        this.TryShow();
        this.TryRestore();
    }

    private void TryActivate()
    {
        try
        {
            this.target.Activate();
        }
        catch (Exception)
        {
        }
    }
}
