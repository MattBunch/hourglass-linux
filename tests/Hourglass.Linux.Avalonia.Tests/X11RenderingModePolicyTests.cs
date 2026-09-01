namespace Hourglass.Linux.Avalonia.Tests;

using Xunit;

public sealed class X11RenderingModePolicyTests
{
    [Theory]
    [InlineData("wayland", "wayland-0")]
    [InlineData("WAYLAND", "wayland-1")]
    public void RequiresSoftwareRenderingForWaylandSessionWithDisplay(string sessionType, string waylandDisplay)
    {
        Assert.True(X11RenderingModePolicy.RequiresSoftwareRendering(sessionType, waylandDisplay));
    }

    [Theory]
    [InlineData("x11", "wayland-0")]
    [InlineData("wayland", "")]
    [InlineData("wayland", "   ")]
    [InlineData(null, "wayland-0")]
    public void DoesNotRequireSoftwareRenderingWithoutCompleteWaylandSession(
        string? sessionType,
        string? waylandDisplay)
    {
        Assert.False(X11RenderingModePolicy.RequiresSoftwareRendering(sessionType, waylandDisplay));
    }
}
