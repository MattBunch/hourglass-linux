namespace Hourglass.Linux.Avalonia;

internal sealed record ApplicationInfo(
    string ProductName,
    string Description,
    string Version,
    string InformationalVersion,
    string BuildConfiguration,
    string? SourceRevision,
    string RuntimeDescription,
    string OperatingSystemDescription,
    string ProcessArchitecture,
    string DeveloperName,
    Uri RepositoryUri,
    Uri DeveloperWebsiteUri,
    Uri OriginalProjectUri,
    string LicenseName)
{
    public string DisplaySourceRevision => string.IsNullOrWhiteSpace(this.SourceRevision)
        ? ApplicationStrings.LocalBuild
        : this.SourceRevision;
}
