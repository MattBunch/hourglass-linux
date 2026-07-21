#nullable enable

using Hourglass.Platform;
using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

internal interface IAppSettingsStore
{
    Task<LinuxAppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task<LinuxAppSettings> SaveChangeAsync(
        LinuxAppSettings previous,
        LinuxAppSettings requested,
        CancellationToken cancellationToken = default);
}

internal sealed class DirectAppSettingsStore : IAppSettingsStore
{
    private const string SettingsKey = "app";

    private readonly ISettingsStore settingsStore;

    public DirectAppSettingsStore(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<LinuxAppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await this.settingsStore.LoadAsync<LinuxAppSettings>(SettingsKey, cancellationToken)
            ?? LinuxAppSettings.Default;
    }

    public async Task<LinuxAppSettings> SaveChangeAsync(
        LinuxAppSettings previous,
        LinuxAppSettings requested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);

        LinuxAppSettings latest = await this.LoadAsync(cancellationToken);
        LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);
        await this.settingsStore.SaveAsync(SettingsKey, merged, cancellationToken);
        return merged;
    }
}

internal sealed class CoordinatedAppSettingsStore : IAppSettingsStore
{
    private const string SettingsKey = "app";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISettingsStore settingsStore;
    private LinuxAppSettings? latestSnapshot;

    public CoordinatedAppSettingsStore(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<LinuxAppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this.latestSnapshot = await this.LoadLatestAsync(cancellationToken).ConfigureAwait(false);
            return this.latestSnapshot;
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<LinuxAppSettings> SaveChangeAsync(
        LinuxAppSettings previous,
        LinuxAppSettings requested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LinuxAppSettings latest = await this.LoadLatestAsync(cancellationToken).ConfigureAwait(false);
            LinuxAppSettings merged = LinuxSettingsMerger.MergeSettingsChange(previous, requested, latest);
            await this.settingsStore.SaveAsync(SettingsKey, merged, cancellationToken).ConfigureAwait(false);
            this.latestSnapshot = merged;
            return merged;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task<LinuxAppSettings> LoadLatestAsync(CancellationToken cancellationToken)
    {
        return await this.settingsStore.LoadAsync<LinuxAppSettings>(SettingsKey, cancellationToken).ConfigureAwait(false)
            ?? this.latestSnapshot
            ?? LinuxAppSettings.Default;
    }
}

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
