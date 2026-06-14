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
        string? xdgConfigHome = this.getEnvironmentVariable(XdgConfigHomeVariable);
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, AppDirectoryName);
        }

        string homeDirectory = this.getFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            homeDirectory = this.getFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        return Path.Combine(homeDirectory, ".config", AppDirectoryName);
    }
}
