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
}
