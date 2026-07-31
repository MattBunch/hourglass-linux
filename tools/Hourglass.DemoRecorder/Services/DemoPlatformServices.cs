using Hourglass.Linux.Avalonia;
using Hourglass.Platform;

namespace Hourglass.DemoRecorder.Services;

public sealed class DemoPlatformServices
{
    public DemoPlatformServices()
    {
        this.Clock = new DemoClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local));
        this.SettingsStore = new DemoSettingsStore();
        this.NotificationService = new DemoNotificationService();
        this.SoundService = new DemoSoundService();
        this.SessionInhibitor = new DemoSessionInhibitor();
        this.DesktopProgressService = new DemoDesktopProgressService();
        this.StatusIconService = new DemoStatusIconService();
        this.ExternalUriLauncher = new DemoExternalUriLauncher();
        this.WindowAttentionService = new DemoWindowAttentionService();
        this.SystemPowerService = new DemoSystemPowerService();
    }

    public DemoClock Clock { get; }

    public DemoSettingsStore SettingsStore { get; }

    public DemoNotificationService NotificationService { get; }

    public DemoSoundService SoundService { get; }

    public DemoSessionInhibitor SessionInhibitor { get; }

    public DemoDesktopProgressService DesktopProgressService { get; }

    public DemoStatusIconService StatusIconService { get; }

    public DemoExternalUriLauncher ExternalUriLauncher { get; }

    public DemoWindowAttentionService WindowAttentionService { get; }

    public DemoSystemPowerService SystemPowerService { get; }
}

public sealed class DemoSessionInhibitor : ISessionInhibitor
{
    public int AcquireCount { get; private set; }

    public int ReleaseCount { get; private set; }

    public ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.AcquireCount++;
        return ValueTask.FromResult<IAsyncDisposable?>(new Lease(this));
    }

    private sealed class Lease(DemoSessionInhibitor owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class DemoDesktopProgressService : IDesktopProgressService
{
    public bool IsSupported => true;

    public int SetCount { get; private set; }

    public int ClearCount { get; private set; }

    public Task SetProgressAsync(double fraction, DesktopProgressState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.SetCount++;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.ClearCount++;
        return Task.CompletedTask;
    }
}

public sealed class DemoStatusIconService : IStatusIconService
{
    public bool IsSupported => false;

    public bool CanRecoverHiddenWindow => false;

    public int UpdateCount { get; private set; }

    public event EventHandler<StatusIconActionRequestedEventArgs>? ActionRequested
    {
        add { }
        remove { }
    }

    public Task UpdateAsync(StatusIconMenuState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.UpdateCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class DemoExternalUriLauncher : IExternalUriLauncher
{
    public int OpenCount { get; private set; }

    public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        this.OpenCount++;
        return Task.FromResult(false);
    }
}

public sealed class DemoWindowAttentionService : IWindowAttentionService
{
    public int RequestCount { get; private set; }

    public void RecordWindowState(Avalonia.Controls.WindowState windowState)
    {
    }

    public void RequestAttention()
    {
        this.RequestCount++;
    }
}

public sealed class DemoSystemPowerService : ISystemPowerService
{
    public bool IsShutdownSupported => false;

    public int RequestCount { get; private set; }

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.RequestCount++;
        return Task.CompletedTask;
    }
}
