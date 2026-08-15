namespace Hourglass.ReleaseTool;

internal sealed record ReleaseVersionValidationResult(bool IsSuccess, string? Error)
{
    public static ReleaseVersionValidationResult Success() => new(true, null);

    public static ReleaseVersionValidationResult Failure(string error) => new(false, error);
}

internal static class ReleaseVersionValidator
{
    public static ReleaseVersionValidationResult Validate(string projectVersion, string appStreamVersion, string? tag)
    {
        if (!SupportedReleaseVersion.TryParse(projectVersion, out _))
        {
            return ReleaseVersionValidationResult.Failure($"Project version is not a supported release version: {projectVersion}");
        }

        if (!SupportedReleaseVersion.TryParse(appStreamVersion, out _))
        {
            return ReleaseVersionValidationResult.Failure($"Latest AppStream release is not a supported release version: {appStreamVersion}");
        }

        if (!StringComparer.Ordinal.Equals(projectVersion, appStreamVersion))
        {
            return ReleaseVersionValidationResult.Failure($"Project version {projectVersion} does not match latest AppStream release {appStreamVersion}");
        }

        if (!string.IsNullOrEmpty(tag) && !StringComparer.Ordinal.Equals(tag, $"v{projectVersion}"))
        {
            return ReleaseVersionValidationResult.Failure($"Release tag {tag} does not match expected tag v{projectVersion}");
        }

        return ReleaseVersionValidationResult.Success();
    }
}
