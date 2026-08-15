namespace Hourglass.ReleaseTool.Tests;

using Xunit;

public sealed class ReleaseToolApplicationTests
{
    [Fact]
    public async Task VersionWritesOnlyTheEvaluatedVersion()
    {
        ApplicationResult result = await RunAsync(["version", "--project", "project with spaces.csproj"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"0.2.0-beta.1{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ValidateVersionWritesSuccessMessage()
    {
        ApplicationResult result = await RunAsync(["validate-version", "--project", "project.csproj", "--metainfo", "metadata with spaces.xml", "--tag", "v0.2.0-beta.1"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Release version validation passed: 0.2.0-beta.1{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpReturnsSuccess(string helpArgument)
    {
        ApplicationResult result = await RunAsync([helpArgument]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hourglass.ReleaseTool version", result.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task CommandHelpReturnsSuccess()
    {
        ApplicationResult result = await RunAsync(["version", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Hourglass.ReleaseTool version [--project <project-file>]{Environment.NewLine}", result.StandardOutput);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("version", "--unknown", "value")]
    [InlineData("validate-version", "--tag")]
    public async Task UsageErrorsReturnExitCodeTwo(params string[] arguments)
    {
        ApplicationResult result = await RunAsync(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Usage:", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationFailureWritesErrorToStandardError()
    {
        ApplicationResult result = await RunAsync(
            ["validate-version", "--project", "project.csproj", "--metainfo", "metadata.xml", "--tag", "v9.9.9"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("does not match expected tag", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<ApplicationResult> RunAsync(string[] arguments)
    {
        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        var application = new ReleaseToolApplication(
            new StubProjectVersionReader(ProjectVersionReadResult.Success("0.2.0-beta.1")),
            new StubAppStreamMetadataReader(AppStreamMetadataReadResult.Success("0.2.0-beta.1")));

        int exitCode = await application.RunAsync(arguments, standardOutput, standardError);
        return new ApplicationResult(exitCode, standardOutput.ToString(), standardError.ToString());
    }

    private sealed class StubProjectVersionReader(ProjectVersionReadResult result) : IProjectVersionReader
    {
        public Task<ProjectVersionReadResult> ReadAsync(string projectPath) => Task.FromResult(result);
    }

    private sealed class StubAppStreamMetadataReader(AppStreamMetadataReadResult result) : IAppStreamMetadataReader
    {
        public AppStreamMetadataReadResult Read(string metainfoPath) => result;
    }

    private sealed record ApplicationResult(int ExitCode, string StandardOutput, string StandardError);
}
