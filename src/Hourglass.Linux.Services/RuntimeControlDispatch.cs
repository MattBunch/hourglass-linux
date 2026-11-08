namespace Hourglass.Linux.Services;

using System.Text.Json;
using Hourglass.Application;
using Hourglass.Settings;

internal static class RuntimeControlDispatch
{
    internal static ControlResponse Result<T>(string id, ApplicationResult<T> result) => result switch
    {
        ApplicationResult<T>.Success success => new(RuntimeControlTransport.Version, id, true, RuntimeControlJson.Value(success.Value), null),
        ApplicationResult<T>.Failure failure => Error(id, failure.Error),
        _ => throw new InvalidOperationException("Unknown application result.")
    };
    internal static ControlResponse Error(string id, ApplicationError error) => new(RuntimeControlTransport.Version, id, false, null, error);
    internal static async Task<ControlResponse> ExecuteAsync(IHourglassClient client, ControlRequest request, CancellationToken token)
    {
        string id = request.RequestId;
        JsonElement payload = request.Payload;
        switch (request.RequestKind)
        {
            case "detached-start": return Result(id, await client.StartDetachedAsync(RuntimeControlJson.Read<CreateSessionRequest>(payload), token).ConfigureAwait(false));
            case "saved-detached": return Result(id, await client.StartSavedDetachedAsync(RuntimeControlJson.Read<SavedTimerSelection>(payload), token).ConfigureAwait(false));
            case "detach":
                DetachRequest detach = RuntimeControlJson.Read<DetachRequest>(payload);
                return Result(id, await client.DetachSessionAsync(detach.SessionId, detach.ExpectedRevision, token).ConfigureAwait(false));
            case "gui-initialize":
                GuiInitialization gui = RuntimeControlJson.Read<GuiInitialization>(payload);
                return Result(id, await client.InitializeGuiAsync(gui.LaunchInput, gui.LaunchTitle, token).ConfigureAwait(false));
            case "create": return Result(id, await client.CreateSessionAsync(RuntimeControlJson.Read<CreateSessionRequest>(payload), token).ConfigureAwait(false));
            case "execute": return Result(id, await client.ExecuteAsync(RuntimeControlJson.Read<SessionCommand>(payload), token).ConfigureAwait(false));
            case "batch": return Result(id, await client.ExecuteAllAsync(RuntimeControlJson.Read<SessionBatchCommand>(payload), token).ConfigureAwait(false));
            case "saved-start": return Result(id, await client.StartSavedSessionsAsync(RuntimeControlJson.Read<SavedTimerSelection>(payload), token).ConfigureAwait(false));
            case "saved-create": return Result(id, await client.CreateSavedSessionAsync(RuntimeControlJson.Read<SavedTimerDefinition>(payload), token).ConfigureAwait(false));
            case "saved-change": return Result(id, await client.ChangeSavedTimersAsync(RuntimeControlJson.Read<SavedTimerChange>(payload), token).ConfigureAwait(false));
            case "preview": return Result(id, await client.PreviewSoundAsync(RuntimeControlJson.Read<string>(payload), token).ConfigureAwait(false));
            case "sessions": return Result(id, await client.ListSessionsAsync(token).ConfigureAwait(false));
            case "data": return Result(id, await client.GetApplicationDataAsync(token).ConfigureAwait(false));
            case "settings": return Result(id, await client.GetSettingsAsync(token).ConfigureAwait(false));
            case "saved": return Result(id, await client.ListSavedTimersAsync(token).ConfigureAwait(false));
            case "recent": return Result(id, await client.ListRecentInputsAsync(token).ConfigureAwait(false));
            case "recent-clear": return Result(id, await client.ClearRecentInputsAsync(token).ConfigureAwait(false));
            case "themes": return Result(id, await client.ListThemesAsync(token).ConfigureAwait(false));
            case "flush": return Result(id, await client.FlushPersistenceAsync(token).ConfigureAwait(false));
            case "sounds": return Result(id, await client.ListSoundsAsync(token).ConfigureAwait(false));
            case "diagnostics": return Result(id, await client.ListDiagnosticsAsync(token).ConfigureAwait(false));
            case "get": return Result(id, await client.GetSessionAsync(request.SessionId ?? throw new JsonException("Missing session ID."), token).ConfigureAwait(false));
            case "close": return Result(id, await client.CloseSessionAsync(request.SessionId ?? throw new JsonException("Missing session ID."), token).ConfigureAwait(false));
            case "settings-change":
                SettingsChange settings = RuntimeControlJson.Read<SettingsChange>(payload);
                return Result(id, await client.ChangeSettingsAsync(settings.Previous, settings.Requested, token).ConfigureAwait(false));
            case "saved-document-change":
                SavedDocumentChange saved = RuntimeControlJson.Read<SavedDocumentChange>(payload);
                return Result(id, await client.SaveSavedTimersChangeAsync(saved.Previous, saved.Requested, token).ConfigureAwait(false));
            case "themes-change":
                ThemeDocumentChange themes = RuntimeControlJson.Read<ThemeDocumentChange>(payload);
                return Result(id, await client.ChangeThemesAsync(themes.Previous, themes.Requested, token).ConfigureAwait(false));
            case "presentation":
                PresentationChange presentation = RuntimeControlJson.Read<PresentationChange>(payload);
                return Result(id, await client.UpdatePresentationAsync(presentation.SessionId, presentation.Presentation, token).ConfigureAwait(false));
            default: return Error(id, new(ApplicationErrorCode.Unsupported, "Unsupported control request."));
        }
    }
}
