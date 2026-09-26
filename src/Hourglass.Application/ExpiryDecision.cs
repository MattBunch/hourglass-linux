namespace Hourglass.Application;

using Hourglass.Settings;

/// <summary>Completion policy captured before effects run; frontend attention remains a presentation signal.</summary>
public sealed record ExpiryDecision(bool Restart, bool Close, bool RequestAttention)
{
    public static ExpiryDecision FromOptions(TimerDefaults options, ApplicationPreferences preferences, bool supportsRestart)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(preferences);
        bool restart = options.LoopTimer && supportsRestart;
        bool close = options.CloseWhenExpired && !restart && !options.LoopSound;
        return new ExpiryDecision(restart, close, preferences.PopUpWhenExpired && !close);
    }
}
