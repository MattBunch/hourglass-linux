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
        var settings = new LinuxAppSettings(["10 seconds"], notificationsEnabled: false, audioAlertsEnabled: false);

        await store.SaveAsync("app", settings);
        LinuxAppSettings? loaded = await store.LoadAsync<LinuxAppSettings>("app");

        Assert.NotNull(loaded);
        Assert.Equal(["10 seconds"], loaded.RecentTimerInputs);
        Assert.False(loaded.NotificationsEnabled);
        Assert.False(loaded.AudioAlertsEnabled);
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
