namespace Hourglass.Linux.Avalonia.Tests;

using System.Reflection;
using System.Reflection.Emit;
using Hourglass.Linux.Avalonia;
using Xunit;

public sealed class ApplicationInfoProviderTests
{
    [Fact]
    public void InformationalVersionIsPreferred()
    {
        Assembly assembly = CreateAssembly(
            assemblyVersion: new Version(1, 2, 3, 4),
            informationalVersion: "0.1.0+165927d618ea",
            fileVersion: "9.9.9.9");
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal("0.1.0+165927d618ea", info.InformationalVersion);
        Assert.Equal("0.1.0", info.Version);
    }

    [Fact]
    public void AssemblyVersionIsUsedAsFallback()
    {
        Assembly assembly = CreateAssembly(assemblyVersion: new Version(2, 3, 4, 5));
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal("2.3.4.5", info.InformationalVersion);
        Assert.Equal("2.3.4.5", info.Version);
    }

    [Fact]
    public void FileVersionIsUsedBeforeAssemblyVersionFallback()
    {
        Assembly assembly = CreateAssembly(
            assemblyVersion: new Version(2, 3, 4, 5),
            fileVersion: "3.4.5.6");
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal("3.4.5.6", info.InformationalVersion);
        Assert.Equal("3.4.5.6", info.Version);
    }

    [Fact]
    public void EmptyRevisionDisplaysLocalBuild()
    {
        Assembly assembly = CreateAssembly(assemblyVersion: new Version(1, 0));
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal("Local build", info.DisplaySourceRevision);
    }

    [Fact]
    public void LongRevisionIsShortenedSafely()
    {
        Assembly assembly = CreateAssembly(
            assemblyVersion: new Version(1, 0),
            metadata: [new("SourceRevision", "165927d618eafedcba9876543210")]);
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal("165927d618ea", info.SourceRevision);
        Assert.Equal("165927d618ea", info.DisplaySourceRevision);
    }

    [Fact]
    public void RuntimeAndArchitectureFieldsAreNonEmpty()
    {
        var provider = new ApplicationInfoProvider(typeof(ApplicationInfoProvider).Assembly);

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.False(string.IsNullOrWhiteSpace(info.RuntimeDescription));
        Assert.False(string.IsNullOrWhiteSpace(info.OperatingSystemDescription));
        Assert.False(string.IsNullOrWhiteSpace(info.ProcessArchitecture));
    }

    [Fact]
    public void RuntimeDescriptionUsesApplicationStringInsteadOfAssemblyDescription()
    {
        Assembly assembly = CreateAssembly(
            assemblyVersion: new Version(1, 0),
            description: "Packaging description");
        var provider = new ApplicationInfoProvider(assembly, () => ".NET", () => "Linux", () => "X64");

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.Equal(ApplicationStrings.ApplicationDescription, info.Description);
        Assert.NotEqual("Packaging description", info.Description);
    }

    [Fact]
    public void RepositoryAndWebsiteUrisAreAbsoluteHttpsUrls()
    {
        var provider = new ApplicationInfoProvider(typeof(ApplicationInfoProvider).Assembly);

        ApplicationInfo info = provider.GetApplicationInfo();

        Assert.True(info.RepositoryUri.IsAbsoluteUri);
        Assert.True(info.DeveloperWebsiteUri.IsAbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, info.RepositoryUri.Scheme);
        Assert.Equal(Uri.UriSchemeHttps, info.DeveloperWebsiteUri.Scheme);
    }

    private static Assembly CreateAssembly(
        Version assemblyVersion,
        string? informationalVersion = null,
        string? fileVersion = null,
        string? description = null,
        KeyValuePair<string, string>[]? metadata = null)
    {
        var assemblyName = new AssemblyName($"HourglassTestAssembly{Guid.NewGuid():N}")
        {
            Version = assemblyVersion
        };
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            builder.SetCustomAttribute(CreateAttribute<AssemblyInformationalVersionAttribute>(informationalVersion));
        }

        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            builder.SetCustomAttribute(CreateAttribute<AssemblyFileVersionAttribute>(fileVersion));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.SetCustomAttribute(CreateAttribute<AssemblyDescriptionAttribute>(description));
        }

        foreach (KeyValuePair<string, string> item in metadata ?? [])
        {
            ConstructorInfo constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])
                ?? throw new InvalidOperationException("AssemblyMetadataAttribute constructor was not found.");
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [item.Key, item.Value]));
        }

        return builder;
    }

    private static CustomAttributeBuilder CreateAttribute<TAttribute>(string value)
        where TAttribute : Attribute
    {
        ConstructorInfo constructor = typeof(TAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException($"{typeof(TAttribute).Name} constructor was not found.");
        return new CustomAttributeBuilder(constructor, [value]);
    }
}
