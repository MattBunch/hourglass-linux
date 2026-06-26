namespace Hourglass.Platform;

public interface INotificationService
{
    Task ShowTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default);
}

public interface ITrayService
{
    bool IsAvailable { get; }
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
