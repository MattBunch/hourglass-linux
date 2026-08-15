namespace Hourglass.ReleaseTool.Tests;

using Xunit;

public sealed class SupportedReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("1.2.3")]
    [InlineData("10.20.30")]
    [InlineData("999999999999999999999.2.3")]
    public void TryParseAcceptsStableVersions(string value)
    {
        Assert.True(SupportedReleaseVersion.TryParse(value, out _));
    }

    [Theory]
    [InlineData("0.2.0-beta.1")]
    [InlineData("1.0.0-beta.12")]
    public void TryParseAcceptsSupportedBetaVersions(string value)
    {
        Assert.True(SupportedReleaseVersion.TryParse(value, out _));
    }

    [Theory]
    [InlineData("0.2.0-beta.0")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("0.2.0-rc.1")]
    [InlineData("0.2.0-alpha.1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta")]
    public void TryParseRejectsUnsupportedVersions(string value)
    {
        Assert.False(SupportedReleaseVersion.TryParse(value, out _));
    }
}
