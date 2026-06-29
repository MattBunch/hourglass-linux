namespace Hourglass.Linux.Avalonia;

using Hourglass.Platform;
using Hourglass.Timing;

internal readonly record struct DesktopProgressRequest(DesktopProgressState State, double Fraction)
{
    public bool IsHidden => this.State == DesktopProgressState.Hidden;
}

internal static class DesktopProgressProjection
{
    public static DesktopProgressRequest FromViewState(TimerViewState viewState, bool showProgressInTaskbar)
    {
        if (!showProgressInTaskbar || viewState.State == TimerState.Stopped)
        {
            return new DesktopProgressRequest(DesktopProgressState.Hidden, 0);
        }

        double fraction = Math.Clamp(viewState.ProgressPercent / 100, 0, 1);
        DesktopProgressState state = viewState.State switch
        {
            TimerState.Running => DesktopProgressState.Normal,
            TimerState.Paused => DesktopProgressState.Paused,
            TimerState.Expired => DesktopProgressState.Error,
            _ => DesktopProgressState.Hidden
        };

        return state switch
        {
            DesktopProgressState.Hidden => new DesktopProgressRequest(state, 0),
            DesktopProgressState.Error => new DesktopProgressRequest(state, 1),
            _ => new DesktopProgressRequest(state, fraction)
        };
    }
}
