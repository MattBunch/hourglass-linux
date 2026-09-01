namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia;
using Xunit;

public sealed class X11RenderingModePolicyTests
{
    [Fact]
    public void CreateSelectsSoftwareRendering()
    {
        Assert.Equal([X11RenderingMode.Software], X11RenderingModePolicy.Create());
    }

    [Fact]
    public void CreateReturnsIndependentRenderingModeArrays()
    {
        X11RenderingMode[] first = X11RenderingModePolicy.Create();
        X11RenderingMode[] second = X11RenderingModePolicy.Create();

        Assert.NotSame(first, second);
    }
}
