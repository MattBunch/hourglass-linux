namespace Hourglass.Linux.Avalonia.Tests;

using System.Xml.Linq;
using Xunit;

public sealed class PackagingMetadataTests
{
    private const string AppId = "io.github.MattBunch.Hourglass";
    private const string Command = "hourglass-linux";

    [Fact]
    public void DesktopFileUsesExpectedApplicationMetadata()
    {
        string desktop = File.ReadAllText(FindRepositoryFile($"packaging/linux/{AppId}.desktop"));

        Assert.Contains("Type=Application", desktop, StringComparison.Ordinal);
        Assert.Contains("Name=Hourglass", desktop, StringComparison.Ordinal);
        Assert.Contains($"Exec={Command}", desktop, StringComparison.Ordinal);
        Assert.Contains("Icon=hourglass", desktop, StringComparison.Ordinal);
        Assert.Contains("Categories=Utility;", desktop, StringComparison.Ordinal);
        Assert.Contains("StartupWMClass=hourglass", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void AppStreamMetadataMatchesDesktopApplication()
    {
        XDocument document = XDocument.Load(FindRepositoryFile($"packaging/linux/{AppId}.metainfo.xml"));
        XElement component = Assert.IsType<XElement>(document.Root);

        Assert.Equal("component", component.Name.LocalName);
        Assert.Equal("desktop-application", component.Attribute("type")?.Value);
        Assert.Equal(AppId, component.Element("id")?.Value);
        Assert.Equal($"{AppId}.desktop", component.Element("launchable")?.Value);
        Assert.Equal(Command, component.Element("provides")?.Element("binary")?.Value);

        XElement release = Assert.Single(component.Element("releases")?.Elements("release") ?? []);
        Assert.Equal("0.1.0", release.Attribute("version")?.Value);
        Assert.Equal("2026-07-18", release.Attribute("date")?.Value);
    }

    [Fact]
    public void FlatpakManifestInstallsEveryBundledSound()
    {
        string manifest = File.ReadAllText(FindRepositoryFile($"packaging/flatpak/{AppId}.yml"));
        string[] sounds = Directory.GetFiles(
                FindRepositoryDirectory("src/Hourglass.Linux.Avalonia/Assets/Sounds"),
                "*.wav")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        Assert.Contains($"app-id: {AppId}", manifest, StringComparison.Ordinal);
        Assert.Contains($"command: {Command}", manifest, StringComparison.Ordinal);
        Assert.Equal(["BeepLoud.wav", "BeepNormal.wav", "BeepQuiet.wav"], sounds);
        foreach (string sound in sounds)
        {
            Assert.Contains($"publish/Assets/Sounds/{sound}", manifest, StringComparison.Ordinal);
            Assert.Contains($"/app/bin/Assets/Sounds/{sound}", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppImageScriptCreatesExpectedAppDirRootEntries()
    {
        string script = File.ReadAllText(FindRepositoryFile("packaging/appimage/build-appdir.sh"));

        Assert.Contains("repo_root=$(cd -- \"$script_dir/../..\" && pwd)", script, StringComparison.Ordinal);
        Assert.Contains("cat > \"$appdir/AppRun\"", script, StringComparison.Ordinal);
        Assert.Contains("chmod +x \"$appdir/AppRun\"", script, StringComparison.Ordinal);
        Assert.Contains("ln -s \"usr/share/applications/$app_id.desktop\" \"$appdir/$app_id.desktop\"", script, StringComparison.Ordinal);
        Assert.Contains("ln -s \"hourglass.png\" \"$appdir/.DirIcon\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScriptsArePartOfPackagingWorkflow()
    {
        string publishScript = File.ReadAllText(FindRepositoryFile("scripts/publish-linux-release.sh"));
        string validateScript = File.ReadAllText(FindRepositoryFile("scripts/validate-linux-packaging.sh"));
        string workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/tests.yml"));

        Assert.Contains("--runtime linux-x64", publishScript, StringComparison.Ordinal);
        Assert.Contains("dotnet publish \"$project\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -a -- \"$build_output/.\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("desktop-file-validate", validateScript, StringComparison.Ordinal);
        Assert.Contains("appstreamcli validate --no-net", validateScript, StringComparison.Ordinal);
        Assert.Contains("HOURGLASS_REQUIRE_PACKAGE_VALIDATORS", validateScript, StringComparison.Ordinal);
        Assert.Contains("sudo apt-get install --yes appstream desktop-file-utils", workflow, StringComparison.Ordinal);
        Assert.Contains("HOURGLASS_REQUIRE_PACKAGE_VALIDATORS: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Build Linux package artifacts", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/validate-linux-packaging.sh /tmp/hourglass-linux-publish /tmp/hourglass-linux.AppDir", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
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

    private static string FindRepositoryDirectory(string relativePath)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            string candidate = Path.Combine(directory, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
