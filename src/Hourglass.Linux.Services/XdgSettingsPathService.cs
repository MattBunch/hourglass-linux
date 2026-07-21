namespace Hourglass.Linux.Services;

using Hourglass.Platform;

public sealed class XdgSettingsPathService : ISettingsPathService
{
    private const string AppDirectoryName = "hourglass-linux";
    private const string XdgConfigHomeVariable = "XDG_CONFIG_HOME";

    private readonly Func<Environment.SpecialFolder, string> getFolderPath;
    private readonly Func<string, string?> getEnvironmentVariable;

    public XdgSettingsPathService()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath)
    {
    }

    internal XdgSettingsPathService(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        this.getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        this.getFolderPath = getFolderPath ?? throw new ArgumentNullException(nameof(getFolderPath));
    }

    public string GetSettingsDirectory()
    {
        string? xdgConfigHome = GetAbsolutePathOrNull(this.getEnvironmentVariable(XdgConfigHomeVariable));
        if (xdgConfigHome != null)
        {
            return Path.Combine(xdgConfigHome, AppDirectoryName);
        }

        string? homeDirectory = GetAbsolutePathOrNull(this.getFolderPath(Environment.SpecialFolder.UserProfile));
        if (homeDirectory != null)
        {
            return Path.Combine(homeDirectory, ".config", AppDirectoryName);
        }

        string? applicationDataDirectory = GetAbsolutePathOrNull(this.getFolderPath(Environment.SpecialFolder.ApplicationData));
        if (applicationDataDirectory != null)
        {
            return Path.Combine(applicationDataDirectory, AppDirectoryName);
        }

        throw new InvalidOperationException("Unable to resolve a safe absolute settings directory.");
    }

    private static string? GetAbsolutePathOrNull(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? path
            : null;
    }
}
