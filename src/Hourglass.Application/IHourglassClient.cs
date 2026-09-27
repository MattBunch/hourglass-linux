namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;

/// <summary>Local and remote clients expose the same session commands and immutable query results.</summary>
public interface IHourglassClient
{
    Task<ApplicationResult<TimerSessionSnapshot>> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default);

    Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Func<SessionNotification, Task> publish, CancellationToken cancellationToken = default);

    Task<ApplicationResult<TimerSessionSnapshot>> ExecuteAsync(SessionCommand command, CancellationToken cancellationToken = default);

    Task<ApplicationResult<TimerSessionSnapshot>> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<ApplicationDataSnapshot>> GetApplicationDataAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<bool>> SaveSavedTimersChangeAsync(SavedTimersDocument previous, SavedTimersDocument requested, CancellationToken cancellationToken = default);
    Task<ApplicationResult<LinuxAppSettings>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<LinuxAppSettings>> ChangeSettingsAsync(LinuxAppSettings previous, LinuxAppSettings requested, CancellationToken cancellationToken = default);
    Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ListSavedTimersAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ChangeSavedTimersAsync(SavedTimerChange change, CancellationToken cancellationToken = default);
    Task<ApplicationResult<TimerSessionSnapshot>> CreateSavedSessionAsync(SavedTimerDefinition saved, CancellationToken cancellationToken = default);
    Task<ApplicationResult<ImmutableArray<string>>> ListRecentInputsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<ImmutableArray<string>>> ClearRecentInputsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<ImmutableArray<CustomThemeDefinition>>> ListThemesAsync(CancellationToken cancellationToken = default);
    Task<ApplicationResult<bool>> ChangeThemesAsync(CustomThemesDocument previous, CustomThemesDocument requested, CancellationToken cancellationToken = default);
    Task<ApplicationResult<bool>> UpdatePresentationAsync(string sessionId, SessionPresentation presentation, CancellationToken cancellationToken = default);
    Task<ApplicationResult<bool>> FlushPersistenceAsync(CancellationToken cancellationToken = default);
}
