namespace Hourglass.ReleaseTool.Tests;

using Xunit;

public sealed class AppStreamMetadataTests
{
    [Theory]
    [InlineData("<component><releases><release version=\"0.2.0\" date=\"2026-08-14\" /></releases></component>")]
    [InlineData("<component><releases><release date=\"2026-08-14\" version=\"0.2.0\" /></releases></component>")]
    [InlineData("<component xmlns=\"https://www.freedesktop.org/software/appstream/metainfo\"><releases><release version=\"0.2.0\" /></releases></component>")]
    public void ReadReturnsFirstReleaseVersion(string xml)
    {
        using TemporaryFile fixture = new(xml);

        AppStreamMetadataReadResult result = new AppStreamMetadata().Read(fixture.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal("0.2.0", result.Version);
    }

    [Fact]
    public void ReadReturnsTheFirstReleaseInDocumentOrder()
    {
        using TemporaryFile fixture = new("<component><releases><release version=\"0.2.0\" /><release version=\"0.1.0\" /></releases></component>");

        AppStreamMetadataReadResult result = new AppStreamMetadata().Read(fixture.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal("0.2.0", result.Version);
    }

    [Theory]
    [InlineData("<component><releases /></component>")]
    [InlineData("<component><releases><release date=\"2026-08-14\" /></releases></component>")]
    [InlineData("<component><releases><release")]
    public void ReadRejectsMissingOrInvalidReleaseMetadata(string xml)
    {
        using TemporaryFile fixture = new(xml);

        AppStreamMetadataReadResult result = new AppStreamMetadata().Read(fixture.Path);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ReadRejectsMissingFile()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hourglass-missing-{Guid.NewGuid():N}.xml");

        AppStreamMetadataReadResult result = new AppStreamMetadata().Read(path);

        Assert.False(result.IsSuccess);
        Assert.Contains("file not found", result.Error, StringComparison.Ordinal);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string contents)
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hourglass-appstream-{Guid.NewGuid():N}.xml");
            File.WriteAllText(this.Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(this.Path);
        }
    }
}
