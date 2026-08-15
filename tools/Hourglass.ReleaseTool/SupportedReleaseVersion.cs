using System.Text.RegularExpressions;

namespace Hourglass.ReleaseTool;

internal readonly record struct SupportedReleaseVersion(string Value)
{
    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-beta\\.([1-9][0-9]*))?$",
        RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out SupportedReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        Match match = VersionPattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        version = new SupportedReleaseVersion(value);
        return true;
    }
}
