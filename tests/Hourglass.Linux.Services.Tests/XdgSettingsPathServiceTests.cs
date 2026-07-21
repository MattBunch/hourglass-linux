namespace Hourglass.Linux.Services.Tests;

using EnvironmentSpecialFolder = System.Environment.SpecialFolder;
using Xunit;

public sealed class XdgSettingsPathServiceTests
{
    [Fact]
    public void GetSettingsDirectoryUsesXdgConfigHomeWhenSet()
    {
        var service = new XdgSettingsPathService(
            name => name == "XDG_CONFIG_HOME" ? "/tmp/xdg-config" : null,
            _ => "/home/tester");

        Assert.Equal("/tmp/xdg-config/hourglass-linux", service.GetSettingsDirectory());
    }

    [Fact]
    public void GetSettingsDirectoryFallsBackToUserConfigDirectory()
    {
        var service = new XdgSettingsPathService(
            _ => null,
            folder => folder == EnvironmentSpecialFolder.UserProfile ? "/home/tester" : "/fallback");

        Assert.Equal("/home/tester/.config/hourglass-linux", service.GetSettingsDirectory());
    }

    [Theory]
    [InlineData("relative/config")]
    [InlineData("   ")]
    public void GetSettingsDirectoryIgnoresUnsafeXdgConfigHome(string xdgConfigHome)
    {
        var service = new XdgSettingsPathService(
            name => name == "XDG_CONFIG_HOME" ? xdgConfigHome : null,
            folder => folder == EnvironmentSpecialFolder.UserProfile ? "/home/tester" : "/fallback");

        Assert.Equal("/home/tester/.config/hourglass-linux", service.GetSettingsDirectory());
    }

    [Fact]
    public void GetSettingsDirectoryUsesApplicationDataFallbackOnlyWhenAbsolute()
    {
        var service = new XdgSettingsPathService(
            _ => null,
            folder => folder == EnvironmentSpecialFolder.ApplicationData ? "/home/tester/.local/share" : string.Empty);

        Assert.Equal("/home/tester/.local/share/hourglass-linux", service.GetSettingsDirectory());
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-home")]
    public void GetSettingsDirectoryThrowsWhenNoSafeAbsoluteFallbackExists(string homeDirectory)
    {
        var service = new XdgSettingsPathService(
            _ => null,
            folder => folder == EnvironmentSpecialFolder.UserProfile ? homeDirectory : string.Empty);

        Assert.Throws<InvalidOperationException>(service.GetSettingsDirectory);
    }
}
