namespace Hourglass.Host.Tests;

using Hourglass.Linux.Services;
using Xunit;
public sealed class HostTests
{
    [Fact]
    public void IdleGraceResetsWhenAClientOrSessionNeedsAuthority()
    {
        HostIdlePolicy state = new();
        state = state.Observe(TimeSpan.Zero, false);
        Assert.False(state.CanStop(TimeSpan.FromSeconds(2)));
        Assert.True(state.CanStop(TimeSpan.FromSeconds(3)));
        state = state.Observe(TimeSpan.FromSeconds(3), true);
        Assert.False(state.CanStop(TimeSpan.FromSeconds(10)));
        state = state.Observe(TimeSpan.FromSeconds(10), false);
        Assert.False(state.CanStop(TimeSpan.FromSeconds(12)));
        Assert.True(state.CanStop(TimeSpan.FromSeconds(13)));
    }
    [Fact] public async Task InvalidArgumentsCannotStartRuntime() => Assert.Equal(2, await Program.Main(["--invalid"]));
    [Fact]
    public async Task MissingHostReturnsRuntimeUnavailable()
    {
        HostBootstrap bootstrap = new(_ => throw new System.ComponentModel.Win32Exception("Missing executable"), () => "/missing/hourglass-host");
        var result = await bootstrap.StartAsync(Hourglass.Application.RuntimeClientKind.Tui, CancellationToken.None);
        Assert.Equal(Hourglass.Application.ApplicationErrorCode.RuntimeUnavailable, Assert.IsType<Hourglass.Application.ApplicationResult<RemoteHourglassClient>.Failure>(result).Error.Code);
    }
    [Fact]
    public void HostHasNoPresentationDependencies()
    {
        string[] references = typeof(Program).Assembly.GetReferencedAssemblies().Select(item => item.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(references, item => item.Contains("Avalonia") || item.Contains("Terminal.Gui"));
    }
}
