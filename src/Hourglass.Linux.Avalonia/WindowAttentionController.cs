using Avalonia.Controls;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

internal interface IWindowAttentionTarget
{
    bool IsVisible { get; }

    WindowState WindowState { get; set; }

    void Hide();

    void Show();

    void Activate();
}

internal interface IWindowAttentionService
{
    void RecordWindowState(WindowState state);

    void RequestAttention();
}

internal sealed class WindowAttentionController : IWindowAttentionService
{
    private readonly IDiagnosticSink diagnosticSink;
    private readonly IWindowAttentionTarget target;
    private WindowState restoreWindowState = WindowState.Normal;

    public WindowAttentionController(IWindowAttentionTarget target, IDiagnosticSink? diagnosticSink = null)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.diagnosticSink = diagnosticSink ?? NoOpDiagnosticSink.Instance;
    }

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
        catch (Exception exception)
        {
            this.RecordFailure("read-visible", exception);
            return true;
        }
    }

    private WindowState? TryGetWindowState()
    {
        try
        {
            return this.target.WindowState;
        }
        catch (Exception exception)
        {
            this.RecordFailure("read-state", exception);
            return null;
        }
    }

    private void TryShow()
    {
        try
        {
            this.target.Show();
        }
        catch (Exception exception)
        {
            this.RecordFailure("show", exception);
        }
    }

    private void TryHide()
    {
        try
        {
            this.target.Hide();
        }
        catch (Exception exception)
        {
            this.RecordFailure("hide", exception);
        }
    }

    private void TryRestore()
    {
        try
        {
            this.target.WindowState = this.restoreWindowState;
        }
        catch (Exception exception)
        {
            this.RecordFailure("restore", exception);
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
        catch (Exception exception)
        {
            this.RecordFailure("activate", exception);
        }
    }

    private void RecordFailure(string operation, Exception exception)
    {
        this.diagnosticSink.TryRecord(new DiagnosticEvent(
            DiagnosticSeverity.Warning,
            DiagnosticFailureClass.BestEffort,
            "window-attention",
            operation,
            "avalonia-window",
            "Window attention request failed.",
            exception));
    }
}
