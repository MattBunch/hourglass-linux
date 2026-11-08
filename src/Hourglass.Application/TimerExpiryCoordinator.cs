namespace Hourglass.Application;

internal enum ExpiryAction
{
    None,
    Restart,
    Close,
    Superseded
}

internal sealed record ExpiryCompletion(long Revision, ExpiryAction Action);

/// <summary>Effect implementations contain infrastructure and diagnostics, never timer mutations.</summary>
internal interface ITimerExpiryEffects
{
    Task ReleaseInhibitionAsync();
    Task NotifyAsync();
    Task PlayAudioAsync(long revision);
    bool IsShutdownRequested { get; }
    Task RequestShutdownAsync();
}

internal static class TimerExpiryCoordinator
{
    public static async Task<ExpiryCompletion> CompleteAsync(
        TimerSession session, long revision, ExpiryDecision decision, ITimerExpiryEffects effects)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(effects);
        if (!session.IsCurrent(revision))
        {
            return new(revision, ExpiryAction.Superseded);
        }

        await effects.ReleaseInhibitionAsync().ConfigureAwait(false);
        if (!await NotifyAndPlayAsync(session, revision, effects).ConfigureAwait(false))
        {
            return new(revision, ExpiryAction.Superseded);
        }

        if (effects.IsShutdownRequested)
        {
            await effects.RequestShutdownAsync().ConfigureAwait(false);
        }

        if (!session.IsCurrent(revision))
        {
            return new(revision, ExpiryAction.Superseded);
        }

        return new(revision, decision.Restart ? ExpiryAction.Restart : decision.Close ? ExpiryAction.Close : ExpiryAction.None);
    }

    // Restored expiry deliberately does not repeat shutdown, looping or automatic close.
    public static async Task NotifyRestoredAsync(TimerSession session, long revision, ITimerExpiryEffects effects)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(effects);
        await NotifyAndPlayAsync(session, revision, effects).ConfigureAwait(false);
    }

    private static async Task<bool> NotifyAndPlayAsync(TimerSession session, long revision, ITimerExpiryEffects effects)
    {
        if (!session.IsCurrent(revision))
        {
            return false;
        }

        await effects.NotifyAsync().ConfigureAwait(false);
        if (!session.IsCurrent(revision))
        {
            return false;
        }

        await effects.PlayAudioAsync(revision).ConfigureAwait(false);
        return session.IsCurrent(revision);
    }
}
