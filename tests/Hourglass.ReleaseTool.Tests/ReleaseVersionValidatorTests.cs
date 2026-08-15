namespace Hourglass.ReleaseTool.Tests;

using Xunit;

public sealed class ReleaseVersionValidatorTests
{
    [Fact]
    public void ValidateAcceptsMatchingMetadataAndTag()
    {
        ReleaseVersionValidationResult result = ReleaseVersionValidator.Validate("0.2.0-beta.1", "0.2.0-beta.1", "v0.2.0-beta.1");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("0.2.0-rc.1", "0.2.0", "Project version")]
    [InlineData("0.2.0", "0.2.0-rc.1", "Latest AppStream")]
    [InlineData("0.2.0", "0.2.1", "does not match latest AppStream")]
    [InlineData("0.2.0", "0.2.0", "Release tag v0.2.1")]
    public void ValidateRejectsInvalidOrMismatchingMetadata(string projectVersion, string appStreamVersion, string expectedError)
    {
        string? tag = StringComparer.Ordinal.Equals(expectedError, "Release tag v0.2.1") ? "v0.2.1" : null;

        ReleaseVersionValidationResult result = ReleaseVersionValidator.Validate(projectVersion, appStreamVersion, tag);

        Assert.False(result.IsSuccess);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAcceptsOmittedTag()
    {
        ReleaseVersionValidationResult result = ReleaseVersionValidator.Validate("0.1.0", "0.1.0", null);

        Assert.True(result.IsSuccess);
    }
}
