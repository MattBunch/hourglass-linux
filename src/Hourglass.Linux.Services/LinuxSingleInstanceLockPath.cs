namespace Hourglass.Linux.Services;

internal static class LinuxSingleInstanceLockPath
{
    private const string AppDirectoryName = "hourglass-linux";
    private const string LockFileName = "hourglass-linux.lock";
    private const string SocketFileName = "hourglass-linux.sock";
    private const string XdgCacheHomeVariable = "XDG_CACHE_HOME";
    private const string XdgRuntimeDirVariable = "XDG_RUNTIME_DIR";

    public static string Resolve(
        Func<string, string?> getEnvironmentVariable,
        Func<string?> getUserHomeDirectory)
    {
        return ResolvePath(getEnvironmentVariable, getUserHomeDirectory, LockFileName);
    }

    public static string ResolveSocketPath(
        Func<string, string?> getEnvironmentVariable,
        Func<string?> getUserHomeDirectory)
    {
        return ResolvePath(getEnvironmentVariable, getUserHomeDirectory, SocketFileName);
    }

    private static string ResolvePath(
        Func<string, string?> getEnvironmentVariable,
        Func<string?> getUserHomeDirectory,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(getUserHomeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string? runtimeDirectory = GetAbsolutePathOrNull(getEnvironmentVariable(XdgRuntimeDirVariable));
        if (runtimeDirectory != null)
        {
            return CreatePath(runtimeDirectory, fileName);
        }

        string? cacheDirectory = GetAbsolutePathOrNull(getEnvironmentVariable(XdgCacheHomeVariable));
        if (cacheDirectory != null)
        {
            return CreatePath(cacheDirectory, fileName);
        }

        string? homeDirectory = GetAbsolutePathOrNull(getUserHomeDirectory());
        if (homeDirectory != null)
        {
            return Path.Combine(homeDirectory, ".cache", AppDirectoryName, fileName);
        }

        throw new InvalidOperationException("Unable to resolve a safe per-user lock file path.");
    }

    private static string CreatePath(string baseDirectory, string fileName)
    {
        return Path.Combine(baseDirectory, AppDirectoryName, fileName);
    }

    private static string? GetAbsolutePathOrNull(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? path
            : null;
    }
}
