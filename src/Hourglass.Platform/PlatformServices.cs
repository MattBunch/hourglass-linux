using Hourglass.Settings;

namespace Hourglass.Platform;

public interface INotificationService
{
    Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default);
}

public interface IExternalUriLauncher
{
    Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

public enum StatusIconAction
{
    NewTimer,
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
    bool IsSupported { get; }

    bool IsSoundAvailable(string soundId);

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
    public const string None = BuiltInAudioAlertSounds.None;
    public const string LoudBeep = BuiltInAudioAlertSounds.LoudBeep;
    public const string NormalBeep = BuiltInAudioAlertSounds.NormalBeep;
    public const string QuietBeep = BuiltInAudioAlertSounds.QuietBeep;
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

    Task SendLaunchRequestAsync(SingleInstanceLaunchRequest request, CancellationToken cancellationToken = default);

    Task StartRequestListenerAsync(
        Func<SingleInstanceLaunchRequest, CancellationToken, Task> handleRequestAsync,
        CancellationToken cancellationToken = default);
}

public enum SingleInstanceLaunchRequestKind
{
    Activate,
    StartTimer
}

public sealed record SingleInstanceLaunchRequest(
    SingleInstanceLaunchRequestKind Kind,
    IReadOnlyList<string>? Arguments,
    string? TimerInput = null,
    string? TimerTitle = null)
{
    public IReadOnlyList<string> Arguments { get; } = Array.AsReadOnly(Arguments?.ToArray() ?? []);

    public string? TimerInput { get; } = TimerInput;

    public string? TimerTitle { get; } = TimerTitle;
}

public interface IStartupIntegrationService
{
    Task SetLaunchAtLoginAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IWakeAlarmService
{
    Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
        WakeAlarmRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WakeAlarmRequest(DateTimeOffset WakeAt, string Reason);

public readonly record struct WakeAlarmScheduleResult(
    bool Supported,
    bool Scheduled,
    string Message,
    IWakeAlarmLease? Lease);

public interface IWakeAlarmLease : IAsyncDisposable;

public interface ISystemPowerService
{
    bool IsShutdownSupported { get; }

    Task RequestShutdownAsync(CancellationToken cancellationToken = default);
}

public interface IDiagnosticSink
{
    void Info(string category, string message);

    void Warning(string category, string message, Exception? exception = null);

    void Error(string category, string message, Exception? exception = null);
}
