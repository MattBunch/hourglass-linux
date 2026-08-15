namespace Hourglass.ReleaseTool.Tests;

using Xunit;

public sealed class ProjectVersionReaderTests
{
    [Theory]
    [InlineData("<Project><PropertyGroup><Version>0.2.0-beta.1</Version></PropertyGroup></Project>", "0.2.0-beta.1")]
    [InlineData("<Project><PropertyGroup><Version Condition=\"'$(Version)' == ''\">0.2.0</Version></PropertyGroup></Project>", "0.2.0")]
    [InlineData("<Project><!-- <Version>9.9.9</Version> --><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>", "0.2.0")]
    [InlineData("<Project><PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><Version>9.9.9</Version></PropertyGroup><PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><Version>0.2.0</Version></PropertyGroup></Project>", "0.2.0")]
    [InlineData("<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup><ItemGroup><PackageReference Include=\"Example\"><Version>9.9.9</Version></PackageReference></ItemGroup></Project>", "0.2.0")]
    public async Task ReadAsyncUsesEvaluatedReleaseMsBuildVersion(string projectXml, string expectedVersion)
    {
        using ProjectFixture fixture = new(projectXml);

        ProjectVersionReadResult result = await new ProjectVersionReader().ReadAsync(fixture.ProjectPath);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expectedVersion, result.Version);
    }

    [Fact]
    public async Task ReadAsyncSupportsProjectPathsContainingWhitespace()
    {
        using ProjectFixture fixture = new("<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>", "release project path");

        ProjectVersionReadResult result = await new ProjectVersionReader().ReadAsync(fixture.ProjectPath);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("0.2.0", result.Version);
    }

    private sealed class ProjectFixture : IDisposable
    {
        private readonly string directory;

        public ProjectFixture(string contents, string directoryName = "release-project")
        {
            this.directory = Path.Combine(Path.GetTempPath(), $"hourglass-{directoryName}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.directory);
            this.ProjectPath = Path.Combine(this.directory, "Hourglass.Linux.Avalonia.csproj");
            File.WriteAllText(this.ProjectPath, contents);
        }

        public string ProjectPath { get; }

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }
}
