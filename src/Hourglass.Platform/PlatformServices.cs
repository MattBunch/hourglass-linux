namespace Hourglass.Platform;

public interface INotificationService
{
    Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default);
}

public enum StatusIconAction
{
    ShowWindow,
    HideWindow,
    PauseResume,
    Stop,
    Restart,
    Exit
}

public sealed class StatusIconActionRequestedEventArgs(StatusIconAction action) : EventArgs
{
    public StatusIconAction Action { get; } = action;
}

public sealed record StatusIconMenuState(
    string ToolTipText,
    bool IsVisible,
    string PauseResumeText,
    bool CanPauseResume,
    bool CanStop,
    bool CanRestart,
    bool CanHideWindow,
    bool CanExit);

public interface IStatusIconService : IAsyncDisposable
{
    bool IsSupported { get; }

    bool CanRecoverHiddenWindow { get; }

    event EventHandler<StatusIconActionRequestedEventArgs>? ActionRequested;

    Task UpdateAsync(StatusIconMenuState state, CancellationToken cancellationToken = default);
}

public interface ISessionInhibitor
{
    ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default);
}

public interface IAudioAlertService
{
    Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default);

    Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default);
}

public enum DesktopProgressState
{
    Hidden,
    Normal,
    Paused,
    Error
}

public interface IDesktopProgressService
{
    bool IsSupported { get; }

    Task SetProgressAsync(double fraction, DesktopProgressState state, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public static class AudioAlertSoundIds
{
    public const string NormalBeep = "resource:Normal beep";
}

public interface ISettingsPathService
{
    string GetSettingsDirectory();
}

public interface ISettingsStore
{
    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}

public interface ISingleInstanceService : IDisposable
{
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);
}

public interface IStartupIntegrationService
{
    Task SetLaunchAtLoginAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IWakeAlarmService
{
    Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(DateTimeOffset wakeAt, CancellationToken cancellationToken = default);
}

public readonly record struct WakeAlarmScheduleResult(bool Supported, string Message);

public interface ISystemPowerService
{
    bool IsShutdownSupported { get; }

    Task RequestShutdownAsync(CancellationToken cancellationToken = default);
}
