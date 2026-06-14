namespace Hourglass.Linux.Services;

internal static class LinuxSingleInstanceLockPath
{
    private const string AppDirectoryName = "hourglass-linux";
    private const string LockFileName = "hourglass-linux.lock";
    private const string XdgCacheHomeVariable = "XDG_CACHE_HOME";
    private const string XdgRuntimeDirVariable = "XDG_RUNTIME_DIR";

    public static string Resolve(
        Func<string, string?> getEnvironmentVariable,
        Func<string?> getUserHomeDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(getUserHomeDirectory);

        string? runtimeDirectory = GetAbsolutePathOrNull(getEnvironmentVariable(XdgRuntimeDirVariable));
        if (runtimeDirectory != null)
        {
            return CreateLockPath(runtimeDirectory);
        }

        string? cacheDirectory = GetAbsolutePathOrNull(getEnvironmentVariable(XdgCacheHomeVariable));
        if (cacheDirectory != null)
        {
            return CreateLockPath(cacheDirectory);
        }

        string? homeDirectory = GetAbsolutePathOrNull(getUserHomeDirectory());
        if (homeDirectory != null)
        {
            return Path.Combine(homeDirectory, ".cache", AppDirectoryName, LockFileName);
        }

        throw new InvalidOperationException("Unable to resolve a safe per-user lock file path.");
    }

    private static string CreateLockPath(string baseDirectory)
    {
        return Path.Combine(baseDirectory, AppDirectoryName, LockFileName);
    }

    private static string? GetAbsolutePathOrNull(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? path
            : null;
    }
}
