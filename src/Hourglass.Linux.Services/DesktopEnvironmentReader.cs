namespace Hourglass.Linux.Services;

public interface IDesktopEnvironmentReader
{
    string? GetEnvironmentVariable(string name);
}

public sealed class ProcessDesktopEnvironmentReader : IDesktopEnvironmentReader
{
    public string? GetEnvironmentVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Environment.GetEnvironmentVariable(name);
    }
}
