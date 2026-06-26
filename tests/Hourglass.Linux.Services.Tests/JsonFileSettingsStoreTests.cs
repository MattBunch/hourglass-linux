namespace Hourglass.Linux.Services.Tests;

using Hourglass.Platform;
using Hourglass.Settings;
using Xunit;

public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string settingsDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadRoundTripsJsonSettings()
    {
        var store = this.CreateStore();
        var settings = new LinuxAppSettings(
            ["10 seconds"],
            notificationsEnabled: false,
            audioAlertsEnabled: false,
            alwaysOnTop: true,
            popUpWhenExpired: false,
            promptOnExit: false,
            reverseProgressBar: true,
            showTimeElapsed: true,
            loopTimer: true,
            loopSound: true,
            closeWhenExpired: true,
            lockInterface: true,
            doNotKeepComputerAwake: true,
            shutDownWhenExpired: true);

        await store.SaveAsync("app", settings);
        LinuxAppSettings? loaded = await store.LoadAsync<LinuxAppSettings>("app");

        Assert.NotNull(loaded);
        Assert.Equal(["10 seconds"], loaded.RecentTimerInputs);
        Assert.False(loaded.NotificationsEnabled);
        Assert.False(loaded.AudioAlertsEnabled);
        Assert.True(loaded.AlwaysOnTop);
        Assert.False(loaded.PopUpWhenExpired);
        Assert.False(loaded.PromptOnExit);
        Assert.True(loaded.ReverseProgressBar);
        Assert.True(loaded.ShowTimeElapsed);
        Assert.True(loaded.LoopTimer);
        Assert.True(loaded.LoopSound);
        Assert.True(loaded.CloseWhenExpired);
        Assert.True(loaded.LockInterface);
        Assert.True(loaded.DoNotKeepComputerAwake);
        Assert.True(loaded.ShutDownWhenExpired);
    }

    [Fact]
    public async Task LoadMissingSettingsReturnsNull()
    {
        var store = this.CreateStore();

        LinuxAppSettings? loaded = await store.LoadAsync<LinuxAppSettings>("missing");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadMalformedSettingsReturnsNull()
    {
        Directory.CreateDirectory(this.settingsDirectory);
        await File.WriteAllTextAsync(Path.Combine(this.settingsDirectory, "app.json"), "{not json");
        var store = this.CreateStore();

        LinuxAppSettings? loaded = await store.LoadAsync<LinuxAppSettings>("app");

        Assert.Null(loaded);
    }

    [Fact]
    public void SettingsKeysRejectPathTraversal()
    {
        Assert.Throws<ArgumentException>(() => JsonFileSettingsStore.GetSettingsFileName("../app"));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.settingsDirectory))
        {
            Directory.Delete(this.settingsDirectory, recursive: true);
        }
    }

    private JsonFileSettingsStore CreateStore()
    {
        return new JsonFileSettingsStore(new FixedSettingsPathService(this.settingsDirectory));
    }

    private sealed class FixedSettingsPathService(string settingsDirectory) : ISettingsPathService
    {
        public string GetSettingsDirectory()
        {
            return settingsDirectory;
        }
    }
}
