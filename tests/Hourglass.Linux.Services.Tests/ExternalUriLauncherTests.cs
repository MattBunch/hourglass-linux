namespace Hourglass.Linux.Services.Tests;

using System.ComponentModel;
using System.Diagnostics;
using Hourglass.Linux.Services;
using Xunit;

public sealed class ExternalUriLauncherTests
{
    [Fact]
    public async Task HttpsUriIsAccepted()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new LinuxExternalUriLauncher((startInfo, _) =>
        {
            capturedStartInfo = startInfo;
            return Task.FromResult(0);
        });

        bool opened = await launcher.OpenAsync(new Uri("https://github.com/MattBunch/hourglass-linux"));

        Assert.True(opened);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal("xdg-open", capturedStartInfo.FileName);
        Assert.False(capturedStartInfo.UseShellExecute);
        Assert.Equal(["https://github.com/MattBunch/hourglass-linux"], capturedStartInfo.ArgumentList);
    }

    [Fact]
    public async Task HttpUriIsAccepted()
    {
        var launcher = new LinuxExternalUriLauncher((_, _) => Task.FromResult(0));

        bool opened = await launcher.OpenAsync(new Uri("http://chris.dziemborowicz.com/apps/hourglass/"));

        Assert.True(opened);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("file:///tmp/hourglass")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.test/hourglass")]
    public async Task UnsupportedUrisAreRejected(string uriText)
    {
        var launcher = new LinuxExternalUriLauncher((_, _) => throw new InvalidOperationException("Should not launch."));
        Uri uri = Uri.TryCreate(uriText, UriKind.Absolute, out Uri? absoluteUri)
            ? absoluteUri
            : new Uri(uriText, UriKind.Relative);

        bool opened = await launcher.OpenAsync(uri);

        Assert.False(opened);
    }

    [Fact]
    public async Task ProcessLaunchFailureReturnsFalse()
    {
        var launcher = new LinuxExternalUriLauncher((_, _) => throw new Win32Exception());

        bool opened = await launcher.OpenAsync(new Uri("https://github.com/MattBunch/hourglass-linux"));

        Assert.False(opened);
    }

    [Fact]
    public async Task FailedProcessExitReturnsFalse()
    {
        var launcher = new LinuxExternalUriLauncher((_, _) => Task.FromResult(1));

        bool opened = await launcher.OpenAsync(new Uri("https://github.com/MattBunch/hourglass-linux"));

        Assert.False(opened);
    }
}
