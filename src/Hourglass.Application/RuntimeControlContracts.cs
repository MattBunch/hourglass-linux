namespace Hourglass.Application;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hourglass.Settings;

/// <summary>Versioned application boundary values. Transport implementations live in Linux.Services.</summary>
public sealed record ControlRequest(int ProtocolVersion, string RequestId, string RequestKind, string? SessionId, JsonElement Payload);
[method: JsonConstructor]
public sealed record ControlResponse(int ProtocolVersion, string RequestId, bool Success, JsonElement? Result,
    ApplicationErrorCode? ErrorCode, string? ErrorMessage)
{
    public ControlResponse(int protocolVersion, string requestId, bool success, JsonElement? result, ApplicationError? error)
        : this(protocolVersion, requestId, success, result, error?.Code, error?.Message) { }
    [JsonIgnore]
    public ApplicationError? Error => this.ErrorCode is ApplicationErrorCode code ? new(code, this.ErrorMessage ?? string.Empty) : null;
}
public sealed record ControlHandshake(string AuthorityId, int ProtocolVersion);
public sealed record ControlPage(ImmutableArray<JsonElement> Items, string? Continuation);
public sealed record ControlPoll(string Id);
public sealed record ControlSubscription(string Id, TimerSessionSnapshot InitialSnapshot, long InitialSequence);
public sealed record ControlNotifications(ImmutableArray<SessionNotification> Items, bool Completed);
public sealed record ControlOperation(string Id, bool Completed, ControlResponse? Outcome = null);
public sealed record SettingsChange(LinuxAppSettings Previous, LinuxAppSettings Requested);
public sealed record SavedDocumentChange(SavedTimersDocument Previous, SavedTimersDocument Requested);
public sealed record ThemeDocumentChange(CustomThemesDocument Previous, CustomThemesDocument Requested);
public sealed record PresentationChange(string SessionId, SessionPresentation Presentation);
