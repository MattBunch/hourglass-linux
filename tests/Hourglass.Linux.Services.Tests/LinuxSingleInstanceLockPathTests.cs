namespace Hourglass.Linux.Services.Tests;

using Xunit;

public sealed class LinuxSingleInstanceLockPathTests
{
    [Fact]
    public void RuntimeDirectoryProducesPreferredLockPath()
    {
        string path = Resolve(("XDG_RUNTIME_DIR", "/run/user/1000"), ("XDG_CACHE_HOME", "/cache"), home: "/home/matt");

        Assert.Equal("/run/user/1000/hourglass-linux/hourglass-linux.lock", path);
    }

    [Fact]
    public void RuntimeDirectoryProducesMatchingSocketPath()
    {
        string path = ResolveSocket(("XDG_RUNTIME_DIR", "/run/user/1000"), ("XDG_CACHE_HOME", "/cache"), home: "/home/matt");

        Assert.Equal("/run/user/1000/hourglass-linux/hourglass-linux.sock", path);
    }

    [Fact]
    public void RelativeRuntimeDirectoryIsIgnored()
    {
        string path = Resolve(("XDG_RUNTIME_DIR", "relative-runtime"), ("XDG_CACHE_HOME", "/cache"), home: "/home/matt");

        Assert.Equal("/cache/hourglass-linux/hourglass-linux.lock", path);
    }

    [Fact]
    public void CacheDirectoryIsUsedWhenRuntimeDirectoryIsUnavailable()
    {
        string path = Resolve(("XDG_CACHE_HOME", "/cache"), home: "/home/matt");

        Assert.Equal("/cache/hourglass-linux/hourglass-linux.lock", path);
    }

    [Fact]
    public void RelativeCacheDirectoryIsIgnored()
    {
        string path = Resolve(("XDG_CACHE_HOME", "relative-cache"), home: "/home/matt");

        Assert.Equal("/home/matt/.cache/hourglass-linux/hourglass-linux.lock", path);
    }

    [Fact]
    public void HomeCacheFallbackIsUsed()
    {
        string path = Resolve(home: "/home/matt");

        Assert.Equal("/home/matt/.cache/hourglass-linux/hourglass-linux.lock", path);
    }

    [Fact]
    public void MissingSafeUserPathsThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Resolve(home: null));
    }

    private static string Resolve(params (string Name, string? Value)[] variables)
    {
        return Resolve(variables, home: null);
    }

    private static string Resolve(string? home)
    {
        return Resolve([], home);
    }

    private static string Resolve((string Name, string? Value) variable, string? home)
    {
        return Resolve([variable], home);
    }

    private static string Resolve((string Name, string? Value) first, (string Name, string? Value) second, string? home)
    {
        return Resolve([first, second], home);
    }

    private static string Resolve((string Name, string? Value)[] variables, string? home)
    {
        return LinuxSingleInstanceLockPath.Resolve(
            name => variables.FirstOrDefault(variable => variable.Name == name).Value,
            () => home);
    }

    private static string ResolveSocket((string Name, string? Value) first, (string Name, string? Value) second, string? home)
    {
        (string Name, string? Value)[] variables = [first, second];
        return LinuxSingleInstanceLockPath.ResolveSocketPath(
            name => variables.FirstOrDefault(variable => variable.Name == name).Value,
            () => home);
    }
}
