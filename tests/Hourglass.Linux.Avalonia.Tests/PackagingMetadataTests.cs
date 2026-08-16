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
        string projectVersion = ReadAvaloniaProjectVersion();
        XElement component = Assert.IsType<XElement>(document.Root);

        Assert.Equal("component", component.Name.LocalName);
        Assert.Equal("desktop-application", component.Attribute("type")?.Value);
        Assert.Equal(AppId, component.Element("id")?.Value);
        Assert.Equal($"{AppId}.desktop", component.Element("launchable")?.Value);
        Assert.Equal(Command, component.Element("provides")?.Element("binary")?.Value);
        Assert.Equal("https://mattbunch.dev", component.Elements("url").Single(element => element.Attribute("type")?.Value == "homepage").Value);
        Assert.Equal(
            "https://github.com/MattBunch/hourglass-linux",
            component.Elements("url").Single(element => element.Attribute("type")?.Value == "vcs-browser").Value);
        Assert.Equal(
            "https://github.com/MattBunch/hourglass-linux/issues",
            component.Elements("url").Single(element => element.Attribute("type")?.Value == "bugtracker").Value);

        XElement release = Assert.IsType<XElement>(component.Element("releases")?.Elements("release").FirstOrDefault());
        Assert.Equal(projectVersion, release.Attribute("version")?.Value);
        Assert.Equal("2026-08-16", release.Attribute("date")?.Value);
    }

    [Fact]
    public void FlatpakManifestInstallsCompletePublishOutputAndLauncher()
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
        Assert.Contains("--socket=x11", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("--socket=wayland", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("--socket=fallback-x11", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("--talk-name=org.freedesktop.portal.", manifest, StringComparison.Ordinal);
        Assert.Equal(["BeepLoud.wav", "BeepNormal.wav", "BeepQuiet.wav"], sounds);
        Assert.Contains("install -d /app/bin /app/lib/hourglass-linux", manifest, StringComparison.Ordinal);
        Assert.Contains("cp -a publish/. /app/lib/hourglass-linux/", manifest, StringComparison.Ordinal);
        Assert.Contains("cat > /app/bin/hourglass-linux <<'EOF'", manifest, StringComparison.Ordinal);
        Assert.Contains("exec /app/lib/hourglass-linux/hourglass-linux \"$@\"", manifest, StringComparison.Ordinal);
        Assert.Contains("chmod +x /app/bin/hourglass-linux", manifest, StringComparison.Ordinal);
        foreach (string sound in sounds)
        {
            Assert.DoesNotContain($"publish/Assets/Sounds/{sound}", manifest, StringComparison.Ordinal);
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
        Assert.Contains("git -C \"$repo_root\" rev-parse --is-inside-work-tree", publishScript, StringComparison.Ordinal);
        Assert.Contains("git -C \"$repo_root\" status --porcelain", publishScript, StringComparison.Ordinal);
        Assert.Contains("Cannot publish a release with uncommitted changes.", publishScript, StringComparison.Ordinal);
        Assert.Contains("source_revision=$(git -C \"$repo_root\" rev-parse --verify HEAD", publishScript, StringComparison.Ordinal);
        Assert.Contains("dotnet publish \"${publish_args[@]}\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("-p:SourceRevisionId=$source_revision", publishScript, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -a -- \"$build_output/.\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("require_text \"$flatpak_manifest\" '      - cp -a publish/. /app/lib/hourglass-linux/'", validateScript, StringComparison.Ordinal);
        Assert.Contains("require_text \"$flatpak_manifest\" '        exec /app/lib/hourglass-linux/hourglass-linux \"$@\"'", validateScript, StringComparison.Ordinal);
        Assert.Contains("Flatpak manifest should not request Wayland while this Avalonia build requires X11.", validateScript, StringComparison.Ordinal);
        Assert.Contains("Flatpak manifest should not request portal talk-name access; portals are allowed by default.", validateScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Flatpak manifest does not install sound asset", validateScript, StringComparison.Ordinal);
        Assert.Contains("desktop-file-validate", validateScript, StringComparison.Ordinal);
        Assert.Contains("appstreamcli validate --no-net", validateScript, StringComparison.Ordinal);
        Assert.Contains("HOURGLASS_REQUIRE_PACKAGE_VALIDATORS", validateScript, StringComparison.Ordinal);
        Assert.Contains("sudo apt-get install --yes appstream desktop-file-utils", workflow, StringComparison.Ordinal);
        Assert.Contains("HOURGLASS_REQUIRE_PACKAGE_VALIDATORS: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Build Linux package artifacts", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/validate-linux-packaging.sh /tmp/hourglass-linux-publish /tmp/hourglass-linux.AppDir", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact v4.6.2", workflow, StringComparison.Ordinal);
        Assert.Contains("ea165f8d65b6e75b540449e92b4886f43607fa02", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowValidatesTagsAndPublishesChecksummedTarballs()
    {
        string workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/release.yml"));

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("- \"v*\"", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("validate-version", workflow, StringComparison.Ordinal);
        Assert.Contains("--tag \"$GITHUB_REF_NAME\"", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/publish-linux-release.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/validate-linux-packaging.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("hourglass-linux-${RELEASE_VERSION}-linux-x64.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget locals global-packages --list", workflow, StringComparison.Ordinal);
        Assert.Contains("find \"$archive_directory\" -type f -name '*.pdb' -delete", workflow, StringComparison.Ordinal);
        Assert.Contains("Release archive staging still contains portable debug symbols.", workflow, StringComparison.Ordinal);
        Assert.Contains("cp LICENSE.md \"$notice_directory/LICENSE.md\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App.Runtime.linux-x64/LICENSE.TXT", workflow, StringComparison.Ordinal);
        Assert.Contains("harfbuzzsharp.nativeassets.linux/LICENSE.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("skiasharp.nativeassets.linux/LICENSE.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", workflow, StringComparison.Ordinal);
        Assert.Contains("sha256sum --check SHA256SUMS", workflow, StringComparison.Ordinal);
        Assert.Contains("git ls-remote origin \"refs/tags/$GITHUB_REF_NAME^{}\"", workflow, StringComparison.Ordinal);
        Assert.Equal(3, workflow.Split("verify_tag_target", StringSplitOptions.None).Length - 1);
        Assert.Contains("remote_tag_target\" != \"$GITHUB_SHA", workflow, StringComparison.Ordinal);
        Assert.Contains("this workflow built commit", workflow, StringComparison.Ordinal);
        Assert.Contains("--json isDraft --jq '.isDraft'", workflow, StringComparison.Ordinal);
        Assert.Contains("A published GitHub Release already exists", workflow, StringComparison.Ordinal);
        Assert.Contains("release upload \"$GITHUB_REF_NAME\" \"${release_assets[@]}\" --clobber", workflow, StringComparison.Ordinal);
        Assert.Contains("release edit \"$GITHUB_REF_NAME\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--draft=false", workflow, StringComparison.Ordinal);
        Assert.Contains("release create \"$GITHUB_REF_NAME\"", workflow, StringComparison.Ordinal);
        Assert.Contains("create_args=(", workflow, StringComparison.Ordinal);
        Assert.Contains("--draft", workflow, StringComparison.Ordinal);
        Assert.Equal(2, workflow.Split("--verify-tag", StringSplitOptions.None).Length - 1);
        Assert.Contains("release_args+=(--prerelease)", workflow, StringComparison.Ordinal);
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

    private static string ReadAvaloniaProjectVersion()
    {
        XDocument project = XDocument.Load(
            FindRepositoryFile("src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj"));
        string? version = project
            .Descendants("Version")
            .SingleOrDefault()
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(version));
        return version;
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
