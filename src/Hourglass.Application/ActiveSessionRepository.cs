namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Platform;
using Hourglass.Settings;

/// <summary>Preserves the distinction between an absent collection and an explicitly empty collection.</summary>
internal sealed class ActiveSessionRepository(ApplicationData data, IDiagnosticSink diagnostics)
{
    public async Task<ImmutableArray<RestoredSession>> LoadAsync(DateTime now, CancellationToken cancellationToken)
    {
        ActiveTimerSessionsDocument? document = await data.LoadOrDefaultAsync<ActiveTimerSessionsDocument?>("active-sessions", null, cancellationToken).ConfigureAwait(false);
        if (document == null)
        {
            ActiveTimerSessionDocument? legacy = await data.LoadOrDefaultAsync<ActiveTimerSessionDocument?>("active-session", null, cancellationToken).ConfigureAwait(false);
            document = legacy == null ? ActiveTimerSessionsDocument.Empty
                : new ActiveTimerSessionsDocument(sessions: [new(Guid.NewGuid().ToString("N"), legacy)]);
        }
        var result = ImmutableArray.CreateBuilder<RestoredSession>();
        foreach (ActiveTimerSessionDefinition definition in document.Sessions)
        {
            ActiveTimerSessionSnapshot? restored = definition.Session == null ? null
                : ActiveTimerSessionSnapshot.FromDocument(definition.Session, now, TimeSpan.Zero);
            if (restored != null) { result.Add(new(definition.SessionId, restored, restored.ExpiredWhileClosed)); }
            else
            {
                diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.DataRecovery,
                    "settings", "restore", "active-sessions", "Invalid timer session was skipped."));
            }
        }
        return result.ToImmutable();
    }

    public Task SaveAsync(ImmutableArray<ActiveTimerSessionDefinition> sessions, CancellationToken cancellationToken = default) =>
        data.SaveAsync("active-sessions", new ActiveTimerSessionsDocument(sessions: sessions.ToArray()), cancellationToken);
}

public sealed record RestoredSession(string SessionId, ActiveTimerSessionSnapshot Session, bool ExpiredWhileClosed = false);

/// <summary>Frontend data retained for restoration; it never supplies countdown state or timer options.</summary>
public sealed record SessionPresentation(string TimerInput, ActiveTimerPresentationMode PresentationMode, WindowGeometrySnapshot? WindowGeometry = null);

public sealed record ApplicationDataSnapshot(LinuxAppSettings Settings, SavedTimersDocument SavedTimers, CustomThemesDocument CustomThemes);
public sealed record RuntimeStartupSnapshot(ApplicationDataSnapshot Data, ImmutableArray<RestoredSession> Sessions);
