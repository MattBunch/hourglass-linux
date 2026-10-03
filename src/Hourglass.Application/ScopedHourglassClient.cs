namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;
internal sealed class ScopedHourglassClient(HourglassRuntime runtime, string leaseId, RuntimeClientKind kind) : IHourglassClient
{
    private SessionLifetime Lifetime => kind == RuntimeClientKind.Tui ? SessionLifetime.Tui : SessionLifetime.Gui;
    public Task<ApplicationResult<TimerSessionSnapshot>> StartDetachedAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) => runtime.StartDetachedAsync(request, cancellationToken);
    public Task<ApplicationResult<TimerSessionSnapshot>> DetachSessionAsync(string sessionId, long expectedRevision, CancellationToken cancellationToken = default) => runtime.DetachSessionAsync(sessionId, expectedRevision, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> StartSavedDetachedAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) => runtime.StartSavedDetachedAsync(selection, cancellationToken);
    public Task<ApplicationResult<System.Collections.Immutable.ImmutableArray<TimerSessionSnapshot>>> ExecuteAllAsync(SessionBatchCommand command, CancellationToken cancellationToken = default) => runtime.ExecuteAllAsync(command, cancellationToken);
    public Task<ApplicationResult<SessionSubscription>> SubscribeAsync(string sessionId, Func<SessionNotification, Task> publish, CancellationToken cancellationToken = default) => runtime.SubscribeAsync(sessionId, publish, cancellationToken);
    public Task<ApplicationResult<TimerSessionSnapshot>> ExecuteAsync(SessionCommand command, CancellationToken cancellationToken = default) => runtime.ExecuteAsync(command, cancellationToken);
    public Task<ApplicationResult<TimerSessionSnapshot>> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => runtime.GetSessionAsync(sessionId, cancellationToken);
    public Task<ApplicationResult<bool>> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default) => kind is RuntimeClientKind.Gui or RuntimeClientKind.Tui ? runtime.ClosePresentationSessionAsync(sessionId, leaseId, cancellationToken) : runtime.CloseSessionAsync(sessionId, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> ListSessionsAsync(CancellationToken cancellationToken = default) => runtime.ListSessionsAsync(cancellationToken);
    public Task<ApplicationResult<ApplicationDataSnapshot>> GetApplicationDataAsync(CancellationToken cancellationToken = default) => runtime.GetApplicationDataAsync(cancellationToken);
    public Task<ApplicationResult<bool>> SaveSavedTimersChangeAsync(SavedTimersDocument previous, SavedTimersDocument requested, CancellationToken cancellationToken = default) => runtime.SaveSavedTimersChangeAsync(previous, requested, cancellationToken);
    public Task<ApplicationResult<LinuxAppSettings>> GetSettingsAsync(CancellationToken cancellationToken = default) => runtime.GetSettingsAsync(cancellationToken);
    public Task<ApplicationResult<LinuxAppSettings>> ChangeSettingsAsync(LinuxAppSettings previous, LinuxAppSettings requested, CancellationToken cancellationToken = default) => runtime.ChangeSettingsAsync(previous, requested, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ListSavedTimersAsync(CancellationToken cancellationToken = default) => runtime.ListSavedTimersAsync(cancellationToken);
    public Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ChangeSavedTimersAsync(SavedTimerChange change, CancellationToken cancellationToken = default) => runtime.ChangeSavedTimersAsync(change, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<string>>> ListRecentInputsAsync(CancellationToken cancellationToken = default) => runtime.ListRecentInputsAsync(cancellationToken);
    public Task<ApplicationResult<ImmutableArray<string>>> ClearRecentInputsAsync(CancellationToken cancellationToken = default) => runtime.ClearRecentInputsAsync(cancellationToken);
    public Task<ApplicationResult<ImmutableArray<CustomThemeDefinition>>> ListThemesAsync(CancellationToken cancellationToken = default) => runtime.ListThemesAsync(cancellationToken);
    public Task<ApplicationResult<bool>> ChangeThemesAsync(CustomThemesDocument previous, CustomThemesDocument requested, CancellationToken cancellationToken = default) => runtime.ChangeThemesAsync(previous, requested, cancellationToken);
    public Task<ApplicationResult<bool>> UpdatePresentationAsync(string sessionId, SessionPresentation presentation, CancellationToken cancellationToken = default) => runtime.UpdatePresentationAsync(sessionId, presentation, cancellationToken);
    public Task<ApplicationResult<bool>> FlushPersistenceAsync(CancellationToken cancellationToken = default) => runtime.FlushPersistenceAsync(cancellationToken);
    public Task<ApplicationResult<ImmutableArray<SoundAvailability>>> ListSoundsAsync(CancellationToken cancellationToken = default) => runtime.ListSoundsAsync(cancellationToken);
    public Task<ApplicationResult<bool>> PreviewSoundAsync(string soundId, CancellationToken cancellationToken = default) => runtime.PreviewSoundAsync(soundId, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<ApplicationDiagnostic>>> ListDiagnosticsAsync(CancellationToken cancellationToken = default) => runtime.ListDiagnosticsAsync(cancellationToken);
    public Task<ApplicationResult<TimerSessionSnapshot>> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) => runtime.CreateSessionAsync(request with { Lifetime = this.Lifetime, OwnerLeaseId = leaseId }, cancellationToken);
    public Task<ApplicationResult<TimerSessionSnapshot>> CreateSavedSessionAsync(SavedTimerDefinition saved, CancellationToken cancellationToken = default) => runtime.CreateSavedSessionCoreAsync(saved, this.Lifetime, leaseId, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> StartSavedSessionsAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) => runtime.StartSavedSessionsCoreAsync(selection, this.Lifetime, leaseId, cancellationToken);
    public Task<ApplicationResult<ForegroundOutcome>> RunForegroundAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) => runtime.RunForegroundAsync(request with { OwnerLeaseId = leaseId }, cancellationToken);
    public Task<ApplicationResult<ImmutableArray<ForegroundOutcome>>> RunSavedForegroundAsync(SavedTimerSelection selection, CancellationToken cancellationToken = default) => runtime.RunSavedForegroundCoreAsync(selection, leaseId, cancellationToken);
    public Task<ApplicationResult<RuntimeStartupSnapshot>> InitializeGuiAsync(string? launchInput = null, string? launchTitle = null, CancellationToken cancellationToken = default) => kind == RuntimeClientKind.Gui ? runtime.InitializeSessionsCoreAsync(leaseId, launchInput, launchTitle, cancellationToken) : Task.FromResult<ApplicationResult<RuntimeStartupSnapshot>>(new ApplicationResult<RuntimeStartupSnapshot>.Failure(new(ApplicationErrorCode.Unsupported, "GUI client registration required.")));
}
