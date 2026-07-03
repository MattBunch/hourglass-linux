using Avalonia.Controls;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

internal sealed class AvaloniaStatusIconService : IStatusIconService
{
    private readonly NativeMenuItem exitItem;
    private readonly NativeMenuItem hideItem;
    private readonly NativeMenuItem newTimerItem;
    private readonly NativeMenuItem pauseResumeItem;
    private readonly NativeMenuItem restartItem;
    private readonly NativeMenuItem showItem;
    private readonly NativeMenuItem stopItem;
    private readonly TrayIcon trayIcon;

    public AvaloniaStatusIconService(WindowIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        this.newTimerItem = this.CreateMenuItem("New timer", StatusIconAction.NewTimer);
        this.showItem = this.CreateMenuItem("Show", StatusIconAction.ShowWindow);
        this.hideItem = this.CreateMenuItem("Hide", StatusIconAction.HideWindow);
        this.pauseResumeItem = this.CreateMenuItem("Pause", StatusIconAction.PauseResume);
        this.stopItem = this.CreateMenuItem("Stop", StatusIconAction.Stop);
        this.restartItem = this.CreateMenuItem("Restart", StatusIconAction.Restart);
        this.exitItem = this.CreateMenuItem("Exit", StatusIconAction.Exit);

        var menu = new NativeMenu
        {
            Items =
            {
                this.newTimerItem,
                new NativeMenuItemSeparator(),
                this.showItem,
                this.hideItem,
                new NativeMenuItemSeparator(),
                this.pauseResumeItem,
                this.stopItem,
                this.restartItem,
                new NativeMenuItemSeparator(),
                this.exitItem
            }
        };

        this.trayIcon = new TrayIcon
        {
            IsVisible = false,
            Icon = icon,
            Menu = menu,
            ToolTipText = "Hourglass"
        };
        this.trayIcon.Clicked += this.TrayIconClicked;
    }

    public bool IsSupported => true;

    public bool CanRecoverHiddenWindow => false;

    public event EventHandler<StatusIconActionRequestedEventArgs>? ActionRequested;

    public Task UpdateAsync(StatusIconMenuState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.trayIcon.ToolTipText = state.ToolTipText;
        this.trayIcon.IsVisible = state.IsVisible;
        this.newTimerItem.IsEnabled = state.IsVisible;
        this.showItem.IsEnabled = state.IsVisible;
        this.hideItem.IsEnabled = state.IsVisible && state.CanHideWindow;
        this.pauseResumeItem.Header = state.PauseResumeText;
        this.pauseResumeItem.IsEnabled = state.IsVisible && state.CanPauseResume;
        this.stopItem.IsEnabled = state.IsVisible && state.CanStop;
        this.restartItem.IsEnabled = state.IsVisible && state.CanRestart;
        this.exitItem.IsEnabled = state.CanExit;

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        this.trayIcon.Clicked -= this.TrayIconClicked;
        this.trayIcon.IsVisible = false;
        this.trayIcon.Dispose();
        return ValueTask.CompletedTask;
    }

    private NativeMenuItem CreateMenuItem(string header, StatusIconAction action)
    {
        var item = new NativeMenuItem
        {
            Header = header
        };
        item.Click += (_, _) => this.PublishAction(action);
        return item;
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        this.PublishAction(StatusIconAction.ShowWindow);
    }

    private void PublishAction(StatusIconAction action)
    {
        this.ActionRequested?.Invoke(this, new StatusIconActionRequestedEventArgs(action));
    }
}
