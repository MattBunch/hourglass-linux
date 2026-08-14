namespace Hourglass.Linux.Avalonia.Tests;

using System.Diagnostics;
using Xunit;

public sealed class ReleaseVersionScriptsTests
{
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.2.0-beta.1")]
    public void ValidatorAcceptsMatchingSupportedProjectAndAppStreamVersions(string version)
    {
        using ReleaseVersionFixture fixture = new(version, version);

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath,
            "--tag", $"v{version}");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Release version validation passed: {version}{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void ValidatorRejectsMismatchedAppStreamVersion()
    {
        using ReleaseVersionFixture fixture = new("0.1.0", "0.1.1");

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not match latest AppStream release", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorRejectsUnsupportedProjectVersion()
    {
        using ReleaseVersionFixture fixture = new("0.2.0-rc.1", "0.2.0-rc.1");

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a supported release version", result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-beta.01")]
    public void ValidatorRejectsVersionsWithLeadingZeroNumericIdentifiers(string version)
    {
        using ReleaseVersionFixture fixture = new(version, version);

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a supported release version", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorRejectsTagThatDoesNotMatchCanonicalVersion()
    {
        using ReleaseVersionFixture fixture = new("0.1.0", "0.1.0");

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath,
            "--tag", "v0.1.1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not match expected tag", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionReaderPrintsOnlyCanonicalProjectVersion()
    {
        using ReleaseVersionFixture fixture = new("0.2.0-beta.1", "0.2.0-beta.1");

        ScriptResult result = RunScript("scripts/read-release-version.sh", "--project", fixture.ProjectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"0.2.0-beta.1{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void VersionReaderAcceptsVersionElementWithMsBuildAttributes()
    {
        using ReleaseVersionFixture fixture = new("0.2.0", "0.2.0");
        File.WriteAllText(
            fixture.ProjectPath,
            "<Project><PropertyGroup><Version Condition=\"'$(Version)' == ''\">0.2.0</Version></PropertyGroup></Project>");

        ScriptResult result = RunScript("scripts/read-release-version.sh", "--project", fixture.ProjectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"0.2.0{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void VersionReaderIgnoresCommentedVersionElements()
    {
        using ReleaseVersionFixture fixture = new("0.2.0", "0.2.0");
        File.WriteAllText(
            fixture.ProjectPath,
            "<Project><!-- <Version>9.9.9</Version> --><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>");

        ScriptResult result = RunScript("scripts/read-release-version.sh", "--project", fixture.ProjectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"0.2.0{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void ValidatorAcceptsAppStreamVersionAttributeRegardlessOfOrder()
    {
        using ReleaseVersionFixture fixture = new("0.2.0", "0.2.0");
        File.WriteAllText(
            fixture.MetainfoPath,
            "<component><releases><release date=\"2026-08-14\" version=\"0.2.0\" /></releases></component>");

        ScriptResult result = RunScript(
            "scripts/validate-release-version.sh",
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Release version validation passed: 0.2.0{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void ValidatorRunsFromScriptDirectoryContainingWhitespace()
    {
        using ReleaseVersionFixture fixture = new("0.1.0", "0.1.0");
        string scriptDirectory = Path.Combine(fixture.DirectoryPath, "release version scripts");
        Directory.CreateDirectory(scriptDirectory);
        string readerScript = Path.Combine(scriptDirectory, "read-release-version.sh");
        string validatorScript = Path.Combine(scriptDirectory, "validate-release-version.sh");
        File.Copy(FindRepositoryFile("scripts/read-release-version.sh"), readerScript);
        File.Copy(FindRepositoryFile("scripts/validate-release-version.sh"), validatorScript);

        ScriptResult result = RunScriptPath(
            validatorScript,
            "--project", fixture.ProjectPath,
            "--metainfo", fixture.MetainfoPath,
            "--tag", "v0.1.0");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Release version validation passed: 0.1.0{Environment.NewLine}", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    private static ScriptResult RunScript(string relativeScriptPath, params string[] arguments)
    {
        return RunScriptPath(FindRepositoryFile(relativeScriptPath), arguments);
    }

    private static ScriptResult RunScriptPath(string scriptPath, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("/bin/bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the version script.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            string candidate = Path.Combine(directory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private sealed class ReleaseVersionFixture : IDisposable
    {
        private readonly string directory;

        public ReleaseVersionFixture(string projectVersion, string appStreamVersion)
        {
            this.directory = Path.Combine(Path.GetTempPath(), $"hourglass-release-version-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.directory);
            this.ProjectPath = Path.Combine(this.directory, "Hourglass.Linux.Avalonia.csproj");
            this.MetainfoPath = Path.Combine(this.directory, "io.github.MattBunch.Hourglass.metainfo.xml");
            File.WriteAllText(this.ProjectPath, $"<Project><PropertyGroup><Version>{projectVersion}</Version></PropertyGroup></Project>");
            File.WriteAllText(this.MetainfoPath, $"<component><releases><release version=\"{appStreamVersion}\" date=\"2026-08-13\" /></releases></component>");
        }

        public string MetainfoPath { get; }

        public string ProjectPath { get; }

        public string DirectoryPath => this.directory;

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
