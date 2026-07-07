#nullable enable

using Hourglass.Platform;
using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

internal interface ISavedTimersStore
{
    Task<SavedTimersDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        SavedTimersDocument previous,
        SavedTimersDocument requested,
        CancellationToken cancellationToken = default);
}

internal sealed class DirectSavedTimersStore : ISavedTimersStore
{
    private const string SavedTimersKey = "saved-timers";

    private readonly ISettingsStore settingsStore;

    public DirectSavedTimersStore(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<SavedTimersDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await this.settingsStore.LoadAsync<SavedTimersDocument>(SavedTimersKey, cancellationToken).ConfigureAwait(false)
            ?? SavedTimersDocument.Empty;
    }

    public Task SaveAsync(
        SavedTimersDocument previous,
        SavedTimersDocument requested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);

        return this.settingsStore.SaveAsync(SavedTimersKey, requested, cancellationToken);
    }
}

internal sealed class CoordinatedSavedTimersStore : ISavedTimersStore
{
    private const string SavedTimersKey = "saved-timers";

    private readonly ISettingsStore settingsStore;
    private readonly object pendingSaveLock = new();
    private Task pendingSave = Task.CompletedTask;

    public CoordinatedSavedTimersStore(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<SavedTimersDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await this.settingsStore.LoadAsync<SavedTimersDocument>(SavedTimersKey, cancellationToken).ConfigureAwait(false)
            ?? SavedTimersDocument.Empty;
    }

    public Task SaveAsync(
        SavedTimersDocument previous,
        SavedTimersDocument requested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);

        lock (this.pendingSaveLock)
        {
            this.pendingSave = this.SaveAfterAsync(this.pendingSave, previous, requested, cancellationToken);
            return this.pendingSave;
        }
    }

    private async Task SaveAfterAsync(
        Task previousSave,
        SavedTimersDocument previous,
        SavedTimersDocument requested,
        CancellationToken cancellationToken)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        SavedTimersDocument latest;
        try
        {
            latest = await this.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            latest = previous;
        }

        SavedTimersDocument merged = MergeSavedTimers(previous, requested, latest);
        await this.settingsStore.SaveAsync(SavedTimersKey, merged, cancellationToken).ConfigureAwait(false);
    }

    private static SavedTimersDocument MergeSavedTimers(
        SavedTimersDocument previous,
        SavedTimersDocument requested,
        SavedTimersDocument latest)
    {
        SavedTimerDefinition[] previousTimers = previous.Timers;
        SavedTimerDefinition[] requestedTimers = requested.Timers;
        SavedTimerDefinition[] latestTimers = latest.Timers;

        var previousIds = previousTimers
            .Select(timer => timer.Id)
            .ToHashSet(StringComparer.Ordinal);
        var requestedById = requestedTimers
            .ToDictionary(timer => timer.Id, StringComparer.Ordinal);
        var latestIds = latestTimers
            .Select(timer => timer.Id)
            .ToHashSet(StringComparer.Ordinal);

        IEnumerable<SavedTimerDefinition> mergedLatest = latestTimers
            .Select(timer => requestedById.TryGetValue(timer.Id, out SavedTimerDefinition? requestedTimer)
                ? requestedTimer
                : previousIds.Contains(timer.Id)
                    ? null
                    : timer)
            .OfType<SavedTimerDefinition>();

        IEnumerable<SavedTimerDefinition> requestedAdditions = requestedTimers
            .Where(timer => !latestIds.Contains(timer.Id) && !previousIds.Contains(timer.Id));

        return new SavedTimersDocument(timers: requestedAdditions.Concat(mergedLatest).ToArray());
    }
}
