using Hourglass.Platform;

namespace Hourglass.DemoRecorder.Services;

public sealed class DemoSoundService : IAudioAlertService
{
    public int PlayCount { get; private set; }

    public int StopCount { get; private set; }

    public bool IsSupported => true;

    public bool IsSoundAvailable(string soundId)
    {
        return !StringComparer.Ordinal.Equals(soundId, AudioAlertSoundIds.None);
    }

    public Task<IAsyncDisposable?> PlayAlertAsync(string soundId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.PlayCount++;
        return Task.FromResult<IAsyncDisposable?>(new Playback(this));
    }

    public Task<IAsyncDisposable?> PlayAlertLoopingAsync(string soundId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.PlayCount++;
        return Task.FromResult<IAsyncDisposable?>(new Playback(this));
    }

    private sealed class Playback(DemoSoundService owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.StopCount++;
            return ValueTask.CompletedTask;
        }
    }
}
