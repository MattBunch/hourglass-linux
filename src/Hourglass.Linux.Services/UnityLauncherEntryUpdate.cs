namespace Hourglass.Linux.Services;

public readonly record struct UnityLauncherEntryUpdate(
    bool ProgressVisible,
    double Progress,
    bool Urgent);
