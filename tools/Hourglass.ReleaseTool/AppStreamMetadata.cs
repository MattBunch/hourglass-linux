using System.Xml;
using System.Xml.Linq;

namespace Hourglass.ReleaseTool;

internal interface IAppStreamMetadataReader
{
    AppStreamMetadataReadResult Read(string metainfoPath);
}

internal sealed record AppStreamMetadataReadResult(string? Version, string? Error)
{
    public bool IsSuccess => Version != null;

    public static AppStreamMetadataReadResult Success(string version) => new(version, null);

    public static AppStreamMetadataReadResult Failure(string error) => new(null, error);
}

internal sealed class AppStreamMetadata : IAppStreamMetadataReader
{
    public AppStreamMetadataReadResult Read(string metainfoPath)
    {
        if (!File.Exists(metainfoPath))
        {
            return AppStreamMetadataReadResult.Failure($"AppStream metadata file not found: {metainfoPath}");
        }

        try
        {
            XDocument document = XDocument.Load(metainfoPath);
            XElement? release = document.Descendants().FirstOrDefault(element =>
                StringComparer.Ordinal.Equals(element.Name.LocalName, "release"));
            if (release == null)
            {
                return AppStreamMetadataReadResult.Failure($"Expected at least one AppStream <release> version in {metainfoPath}");
            }

            XAttribute? versionAttribute = release.Attributes().FirstOrDefault(attribute =>
                StringComparer.Ordinal.Equals(attribute.Name.LocalName, "version"));
            string version = versionAttribute?.Value.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(version)
                ? AppStreamMetadataReadResult.Failure($"Expected at least one AppStream <release> version in {metainfoPath}")
                : AppStreamMetadataReadResult.Success(version);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return AppStreamMetadataReadResult.Failure($"Could not read AppStream metadata {metainfoPath}: {exception.Message}");
        }
    }
}
