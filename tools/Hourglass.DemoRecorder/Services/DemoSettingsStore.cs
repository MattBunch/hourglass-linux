using Hourglass.Platform;
using Hourglass.Settings;

namespace Hourglass.DemoRecorder.Services;

public sealed class DemoSettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> documents = new(StringComparer.Ordinal);

    public bool Accessed { get; private set; }

    public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        this.Accessed = true;

        if (this.documents.TryGetValue(key, out object? value))
        {
            return Task.FromResult((T?)value);
        }

        object? fallback = key switch
        {
            "app" => LinuxAppSettings.Default with
            {
                ThemePreference = LinuxThemePreference.Dark,
                NotificationsEnabled = true,
                AudioAlertsEnabled = true,
                AudioAlertSoundId = AudioAlertSoundIds.NormalBeep,
                AlwaysOnTop = false,
                ShowProgressInTaskbar = true,
                PopUpWhenExpired = true,
                RestoreActiveSessionOnStartup = false,
                OpenSavedTimersOnStartup = false
            },
            "saved-timers" => SavedTimersDocument.Empty,
            "custom-themes" => CustomThemesDocument.Empty,
            "active-session" => null,
            "active-sessions" => ActiveTimerSessionsDocument.Empty,
            _ => null
        };

        return Task.FromResult((T?)fallback);
    }

    public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        this.Accessed = true;

        if (value != null)
        {
            this.documents[key] = value;
        }

        return Task.CompletedTask;
    }
}
