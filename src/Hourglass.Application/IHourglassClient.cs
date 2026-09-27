namespace Hourglass.Application;

using System.Collections.Immutable;

/// <summary>Local and remote clients expose the same session commands and immutable query results.</summary>
public interface IHourglassClient
{
    Task<ApplicationResult<TimerSessionSnapshot>> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default);

    Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Func<SessionNotification, Task> publish, CancellationToken cancellationToken = default);

    Task<ApplicationResult<TimerSessionSnapshot>> ExecuteAsync(SessionCommand command, CancellationToken cancellationToken = default);

    Task<ApplicationResult<TimerSessionSnapshot>> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ListSessionsAsync(CancellationToken cancellationToken = default);
}
