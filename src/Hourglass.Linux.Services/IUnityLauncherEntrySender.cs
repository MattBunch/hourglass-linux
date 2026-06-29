namespace Hourglass.Linux.Services;

public interface IUnityLauncherEntrySender
{
    Task SendUpdateAsync(
        string applicationUri,
        UnityLauncherEntryUpdate update,
        CancellationToken cancellationToken = default);
}

public interface IUnityLauncherEntrySenderFactory
{
    bool TryCreate(out IUnityLauncherEntrySender sender);
}
