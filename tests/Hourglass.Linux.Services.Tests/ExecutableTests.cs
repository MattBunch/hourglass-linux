namespace Hourglass.Linux.Services.Tests;

using Xunit;

public sealed class ExecutableTests
{
    [Fact]
    public void PackagedPayloadDiscoveryWorksWithoutPathAndWithSpaces()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hourglass executable " + Guid.NewGuid().ToString("N"));
        string client = Path.Combine(directory, "cli");
        string host = Path.Combine(directory, "host", "hourglass-host");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);
        File.WriteAllText(host, "fixture");
        try
        {
            Assert.Equal(host, FrontendExecutable.Resolve(client, "hourglass-host", "Hourglass.Host", "host"));
            File.Delete(host);
            Assert.Equal("hourglass-host", FrontendExecutable.Resolve(client, "hourglass-host", "Hourglass.Host", "host"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void VersionOutputStripsBuildMetadata()
    {
        Assert.Equal("0.2.0", ApplicationVersion.Display("0.2.0+revision"));
    }

    [Fact]
    public void VersionOutputRetainsPrerelease() => Assert.Equal("0.2.0-beta.1", ApplicationVersion.Display("0.2.0-beta.1+revision"));
}
