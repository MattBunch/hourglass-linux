namespace Hourglass.Application.Tests;

using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;
using Xunit;

public sealed class RuntimeCoordinationTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public async Task MultipleSessionsShareBackendInhibitionUntilLastSessionPauses()
    {
        var inhibitor = new Inhibitor();
        await using var runtime = new HourglassRuntime(clock: new Clock(), wallClockNow: () => Now,
            services: SessionRuntimeServices.Unsupported with { Inhibitor = inhibitor });
        await runtime.CreateSessionAsync(new("a", "5m", "", new(), new()));
        await runtime.CreateSessionAsync(new("b", "10m", "", new(), new()));
        await Task.WhenAll(runtime.ExecuteAsync(new SessionCommand.Start("a")), runtime.ExecuteAsync(new SessionCommand.Start("b")));
        await runtime.WaitForSessionEffectsAsync("a");
        await runtime.WaitForSessionEffectsAsync("b");
        Assert.Equal(1, inhibitor.Acquisitions);
        await runtime.ExecuteAsync(new SessionCommand.Pause("a"));
        await runtime.WaitForSessionEffectsAsync("a");
        Assert.Equal(0, inhibitor.Lease.Disposals);
        await runtime.ExecuteAsync(new SessionCommand.Pause("b"));
        await runtime.WaitForSessionEffectsAsync("b");
        Assert.Equal(1, inhibitor.Lease.Disposals);
    }

    [Fact]
    public async Task WakeSchedulingUsesLogicalSessionsAndSharedSettingsWithoutWindows()
    {
        var wake = new Wake();
        await using var runtime = new HourglassRuntime(clock: new Clock(), wallClockNow: () => Now, settingsStore: new Store());
        runtime.ConfigureWakeAlarms(wake);
        await runtime.InitializeSessionsAsync("10m");
        await runtime.WaitForServicesAsync();
        Assert.Equal(new DateTimeOffset(Now.AddMinutes(10).AddSeconds(-15)), Assert.Single(wake.Requests).WakeAt);
        await runtime.CreateSessionAsync(new("short", "5m", "", new(), new()));
        await runtime.ExecuteAsync(new SessionCommand.Start("short"));
        await runtime.WaitForServicesAsync();
        Assert.Equal(new DateTimeOffset(Now.AddMinutes(5).AddSeconds(-15)), wake.Requests[^1].WakeAt);
        Assert.Equal(1, wake.Leases[0].Disposals);
        await runtime.ExecuteAsync(new SessionCommand.Pause("short"));
        await runtime.WaitForServicesAsync();
        Assert.Equal(new DateTimeOffset(Now.AddMinutes(10).AddSeconds(-15)), wake.Requests[^1].WakeAt);
        LinuxAppSettings settings = Assert.IsType<ApplicationResult<LinuxAppSettings>.Success>(await runtime.GetSettingsAsync()).Value;
        await runtime.ChangeSettingsAsync(settings, settings with { WakeFromSuspendEnabled = false });
        await runtime.WaitForServicesAsync();
        Assert.Equal(1, wake.Leases[^1].Disposals);
    }

    [Fact]
    public async Task SupersededWakeAcquisitionDisposesLateLease()
    {
        var wake = new Wake { Hold = true };
        await using var controller = new WakeAlarmController(wake, () => new DateTimeOffset(Now));
        Task first = controller.ApplyAsync([new(TimerState.Running, Now.AddMinutes(10))], true);
        await wake.Entered.Task;
        Task second = controller.ApplyAsync([], false);
        var late = new WakeLease();
        wake.Release.SetResult(late);
        await Task.WhenAll(first, second);
        Assert.Equal(1, late.Disposals);
    }

    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
    private sealed class Inhibitor : ISessionInhibitor
    {
        public int Acquisitions { get; private set; }
        public Lease Lease { get; } = new();
        public ValueTask<IAsyncDisposable?> InhibitAsync(string reason, bool inhibitSuspend, bool inhibitIdle, CancellationToken cancellationToken = default)
        {
            this.Acquisitions++;
            return ValueTask.FromResult<IAsyncDisposable?>(this.Lease);
        }
    }
    private sealed class Lease : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { this.Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class WakeLease : IWakeAlarmLease
    {
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { this.Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Wake : IWakeAlarmService
    {
        public List<WakeAlarmRequest> Requests { get; } = [];
        public List<WakeLease> Leases { get; } = [];
        public bool Hold { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WakeLease> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(WakeAlarmRequest request, CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            this.Entered.TrySetResult();
            WakeLease lease = this.Hold ? await this.Release.Task : new();
            this.Leases.Add(lease);
            return new(true, true, "", lease);
        }
    }
    private sealed class Store : ISettingsStore
    {
        private readonly Dictionary<string, object> values = new() { ["app"] = LinuxAppSettings.Default with { WakeFromSuspendEnabled = true } };
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (this.values) { return Task.FromResult(this.values.TryGetValue(key, out object? value) ? (T)value : default); }
        }
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            lock (this.values) { if (value != null) { this.values[key] = value; } }
            return Task.CompletedTask;
        }
    }
}
