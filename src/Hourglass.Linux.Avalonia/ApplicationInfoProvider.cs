namespace Hourglass.Linux.Avalonia;

using System.Reflection;
using System.Runtime.InteropServices;

internal sealed class ApplicationInfoProvider
{
    private const int DisplayRevisionLength = 12;
    private const string DefaultProductName = "Hourglass Linux";
    private const string DefaultDescription = "Simple countdown timer for Linux";
    private const string DefaultDeveloperName = "Matt Bunch";
    private const string DefaultLicenseName = "MIT License";
    private const string MetadataBuildConfiguration = "BuildConfiguration";
    private const string MetadataDeveloperWebsite = "DeveloperWebsite";
    private const string MetadataOriginalProject = "OriginalProject";
    private const string MetadataRepositoryUrl = "RepositoryUrl";
    private const string MetadataSourceRevision = "SourceRevision";
    private const string Unknown = "Unknown";

    private static readonly Uri DefaultRepositoryUri = new("https://github.com/MattBunch/hourglass-linux");
    private static readonly Uri DefaultDeveloperWebsiteUri = new("https://mattbunch.dev");
    private static readonly Uri DefaultOriginalProjectUri = new("http://chris.dziemborowicz.com/apps/hourglass/");

    private readonly Assembly assembly;
    private readonly Func<string> getRuntimeDescription;
    private readonly Func<string> getOperatingSystemDescription;
    private readonly Func<string> getProcessArchitecture;

    public ApplicationInfoProvider()
        : this(
            Assembly.GetEntryAssembly() ?? typeof(ApplicationInfoProvider).Assembly,
            () => RuntimeInformation.FrameworkDescription,
            () => RuntimeInformation.OSDescription,
            () => RuntimeInformation.ProcessArchitecture.ToString())
    {
    }

    internal ApplicationInfoProvider(
        Assembly assembly,
        Func<string>? getRuntimeDescription = null,
        Func<string>? getOperatingSystemDescription = null,
        Func<string>? getProcessArchitecture = null)
    {
        this.assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        this.getRuntimeDescription = getRuntimeDescription ?? (() => RuntimeInformation.FrameworkDescription);
        this.getOperatingSystemDescription = getOperatingSystemDescription ?? (() => RuntimeInformation.OSDescription);
        this.getProcessArchitecture = getProcessArchitecture ?? (() => RuntimeInformation.ProcessArchitecture.ToString());
    }

    public ApplicationInfo GetApplicationInfo()
    {
        string informationalVersion = GetInformationalVersion(this.assembly);
        Uri repositoryUri = GetUri(GetMetadataValue(this.assembly, MetadataRepositoryUrl), DefaultRepositoryUri);

        return new ApplicationInfo(
            GetAttributeValue<AssemblyProductAttribute>(this.assembly, attribute => attribute.Product) ?? DefaultProductName,
            GetAttributeValue<AssemblyDescriptionAttribute>(this.assembly, attribute => attribute.Description) ?? DefaultDescription,
            GetVersion(informationalVersion),
            informationalVersion,
            GetMetadataValue(this.assembly, MetadataBuildConfiguration) ?? Unknown,
            ShortenSourceRevision(GetMetadataValue(this.assembly, MetadataSourceRevision)),
            NonEmpty(this.getRuntimeDescription(), Unknown),
            NonEmpty(this.getOperatingSystemDescription(), Unknown),
            NonEmpty(this.getProcessArchitecture(), Unknown),
            GetAttributeValue<AssemblyCompanyAttribute>(this.assembly, attribute => attribute.Company) ?? DefaultDeveloperName,
            repositoryUri,
            GetUri(GetMetadataValue(this.assembly, MetadataDeveloperWebsite), DefaultDeveloperWebsiteUri),
            GetUri(GetMetadataValue(this.assembly, MetadataOriginalProject), DefaultOriginalProjectUri),
            DefaultLicenseName);
    }

    internal static string ShortenSourceRevision(string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourceRevision))
        {
            return string.Empty;
        }

        string trimmed = sourceRevision.Trim();
        return trimmed.Length <= DisplayRevisionLength
            ? trimmed
            : trimmed[..DisplayRevisionLength];
    }

    private static string GetInformationalVersion(Assembly assembly)
    {
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        string? fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        string? assemblyVersion = assembly.GetName().Version?.ToString();
        return NonEmpty(assemblyVersion, Unknown);
    }

    private static string GetVersion(string fallback)
    {
        int metadataSuffix = fallback.IndexOf('+', StringComparison.Ordinal);
        if (metadataSuffix > 0)
        {
            return fallback[..metadataSuffix];
        }

        return fallback;
    }

    private static string? GetMetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
    }

    private static string? GetAttributeValue<TAttribute>(
        Assembly assembly,
        Func<TAttribute, string?> getValue)
        where TAttribute : Attribute
    {
        TAttribute? attribute = assembly.GetCustomAttribute<TAttribute>();
        return attribute == null
            ? null
            : NonEmpty(getValue(attribute), null);
    }

    private static Uri GetUri(string? value, Uri fallback)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? uri
            : fallback;
    }

    private static string NonEmpty(string? value, string? fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback ?? string.Empty
            : value;
    }
}
