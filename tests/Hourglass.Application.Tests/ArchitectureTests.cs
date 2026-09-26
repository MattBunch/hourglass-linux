namespace Hourglass.Application.Tests;

using System.Text.Json;
using System.Xml.Linq;
using Xunit;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationReferencesOnlyCoreAndPlatform()
    {
        string project = FindProject();
        string[] references = XDocument.Load(project).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((reference.Attribute("Include")?.Value
                ?? throw new InvalidDataException("Project reference has no Include.")).Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["Hourglass.Core", "Hourglass.Platform"], references);
    }

    [Fact]
    public void ResolvedApplicationDependenciesContainNoFrontendOrLinuxInfrastructure()
    {
        string directory = Path.GetDirectoryName(FindProject()) ?? throw new DirectoryNotFoundException();
        string assets = Path.Combine(directory, "obj", "project.assets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assets));
        foreach (JsonProperty dependency in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            string name = dependency.Name.Split('/')[0];
            Assert.DoesNotContain("Avalonia", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Terminal.Gui", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.CommandLine", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hourglass.Linux", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DBus", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindProject()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "Hourglass.Application", "Hourglass.Application.csproj");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Application project was not found.");
    }
}
