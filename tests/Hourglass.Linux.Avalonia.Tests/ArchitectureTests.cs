namespace Hourglass.Linux.Avalonia.Tests;

using Xunit;

public sealed class ArchitectureTests
{
    [Fact]
    public void ModernProjectsPreserveDependencyDirection()
    {
        string core = File.ReadAllText(FindRepositoryFile("src/Hourglass.Core/Hourglass.Core.csproj"));
        string platform = File.ReadAllText(FindRepositoryFile("src/Hourglass.Platform/Hourglass.Platform.csproj"));
        string services = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Services/Hourglass.Linux.Services.csproj"));
        string avalonia = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj"));

        Assert.DoesNotContain("ProjectReference", core, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", core, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Core\Hourglass.Core.csproj", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", platform, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Core\Hourglass.Core.csproj", services, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Platform\Hourglass.Platform.csproj", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", services, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Core\Hourglass.Core.csproj", avalonia, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Platform\Hourglass.Platform.csproj", avalonia, StringComparison.Ordinal);
        Assert.Contains(@"..\Hourglass.Linux.Services\Hourglass.Linux.Services.csproj", avalonia, StringComparison.Ordinal);
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
}
