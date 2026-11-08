namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;

public sealed record SoundAvailability(string Id, string Name, bool Available);

public sealed partial class HourglassRuntime
{
    public Task<ApplicationResult<ImmutableArray<SoundAvailability>>> ListSoundsAsync(CancellationToken cancellationToken = default) =>
        this.QueryAsync(() => (ApplicationResult<ImmutableArray<SoundAvailability>>)new ApplicationResult<ImmutableArray<SoundAvailability>>.Success(
            BuiltInAudioAlertSounds.All.Select(sound => new SoundAvailability(sound.Id, sound.DisplayName,
                sound.IsNone || this.services.Audio.IsSoundAvailable(sound.Id))).ToImmutableArray()), cancellationToken);

    public async Task<ApplicationResult<bool>> PreviewSoundAsync(string soundId, CancellationToken cancellationToken = default)
    {
        if (!BuiltInAudioAlertSounds.TryGet(soundId, out AudioAlertSoundDefinition? sound) || sound == null || sound.IsNone)
        {
            return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.Validation, "Choose a playable sound ID."));
        }
        if (!this.services.Audio.IsSoundAvailable(sound.Id))
        {
            return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.Unsupported, "The selected sound or audio player is unavailable."));
        }
        try
        {
            await using IAsyncDisposable? playback = await this.services.Audio.PlayAlertAsync(sound.Id, cancellationToken).ConfigureAwait(false);
            return new ApplicationResult<bool>.Success(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.InternalFailure, exception.Message));
        }
    }

    public Task<ApplicationResult<ImmutableArray<ApplicationDiagnostic>>> ListDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        this.QueryAsync(() => (ApplicationResult<ImmutableArray<ApplicationDiagnostic>>)new ApplicationResult<ImmutableArray<ApplicationDiagnostic>>.Success(
            this.diagnostics is DiagnosticJournal journal ? journal.Snapshot() : []), cancellationToken);
}
