namespace Hourglass.Application;

using Hourglass.Platform;

/// <summary>Linux capabilities and localized notification text supplied at composition.</summary>
public sealed record SessionRuntimeServices(
    INotificationService Notifications,
    IAudioAlertService Audio,
    ISessionInhibitor Inhibitor,
    ISystemPowerService Power,
    string ApplicationTitle,
    string CompletionBody,
    string InhibitionReason)
{
    public static SessionRuntimeServices Unsupported { get; } = new(
        UnsupportedNotificationService.Instance, UnsupportedAudioAlertService.Instance,
        UnsupportedSessionInhibitor.Instance, UnsupportedSystemPowerService.Instance,
        "Hourglass", "Timer completed.", "Timer running");
}
