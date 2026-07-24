namespace Hourglass.Platform;

public sealed class UnsupportedNotificationService : INotificationService
{
    public static UnsupportedNotificationService Instance { get; } = new();

    private UnsupportedNotificationService()
    {
    }

    public Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class UnsupportedExternalUriLauncher : IExternalUriLauncher
{
    public static UnsupportedExternalUriLauncher Instance { get; } = new();

    private UnsupportedExternalUriLauncher()
    {
    }

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

public sealed class UnsupportedStatusIconService : IStatusIconService
{
    public static UnsupportedStatusIconService Instance { get; } = new();

    private UnsupportedStatusIconService()
    {
    }

    public bool IsSupported => false;

    public bool CanRecoverHiddenWindow => false;

    public event EventHandler<StatusIconActionRequestedEventArgs>? ActionRequested
    {
        add { }
        remove { }
    }

    public Task UpdateAsync(StatusIconMenuState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class UnsupportedSessionInhibitor : ISessionInhibitor
{
    public static UnsupportedSessionInhibitor Instance { get; } = new();

    private UnsupportedSessionInhibitor()
    {
    }

    public ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable?>(null);
    }
}

public sealed class UnsupportedAudioAlertService : IAudioAlertService
{
    public static UnsupportedAudioAlertService Instance { get; } = new();

    private UnsupportedAudioAlertService()
    {
    }

    public bool IsSupported => false;

    public bool IsSoundAvailable(string soundId)
    {
        return false;
    }

    public Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IAsyncDisposable?>(null);
    }

    public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IAsyncDisposable?>(null);
    }
}

public sealed class UnsupportedDesktopProgressService : IDesktopProgressService
{
    public static UnsupportedDesktopProgressService Instance { get; } = new();

    private UnsupportedDesktopProgressService()
    {
    }

    public bool IsSupported => false;

    public Task SetProgressAsync(
        double fraction,
        DesktopProgressState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class UnsupportedWakeAlarmService : IWakeAlarmService
{
    public static UnsupportedWakeAlarmService Instance { get; } = new();

    private UnsupportedWakeAlarmService()
    {
    }

    public Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
        WakeAlarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new WakeAlarmScheduleResult(
            Supported: false,
            Scheduled: false,
            Message: "Wake alarms are not supported in this environment.",
            Lease: null));
    }
}

public sealed class UnsupportedSystemPowerService : ISystemPowerService
{
    public static UnsupportedSystemPowerService Instance { get; } = new();

    private UnsupportedSystemPowerService()
    {
    }

    public bool IsShutdownSupported => false;

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
