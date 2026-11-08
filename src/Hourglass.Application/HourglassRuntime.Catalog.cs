namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;
using System.Text.Json.Serialization;

public sealed partial class HourglassRuntime
{
    private readonly SemaphoreSlim catalogChanges = new(1, 1);
    private Task pendingRecentSave = Task.CompletedTask;

    public Task<ApplicationResult<LinuxAppSettings>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(() => this.Data.AppSettings.LoadAsync(cancellationToken), cancellationToken);

    public Task<ApplicationResult<LinuxAppSettings>> ChangeSettingsAsync(LinuxAppSettings previous, LinuxAppSettings requested, CancellationToken cancellationToken = default) =>
        this.CatalogAsync(() => this.Data.AppSettings.SaveChangeAsync(previous, requested, cancellationToken), cancellationToken);

    public Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ListSavedTimersAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () => (await this.Data.SavedTimers.LoadAsync(cancellationToken).ConfigureAwait(false)).Timers.ToImmutableArray(), cancellationToken);

    public async Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ChangeSavedTimersAsync(SavedTimerChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change is SavedTimerChange.Add { Timer.IsValid: false } or SavedTimerChange.Add { Timer: null })
        {
            return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.Validation, "Invalid saved timer."));
        }
        if (change is not (SavedTimerChange.Add or SavedTimerChange.Remove or SavedTimerChange.Clear))
        {
            return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.Unsupported, "Unknown saved timer change."));
        }
        ApplicationResult<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> result = await this.CatalogAsync(async () =>
        {
            SavedTimersDocument previous = await this.Data.SavedTimers.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (change is SavedTimerChange.Remove selected && !previous.Timers.Any(timer => timer.Id == selected.Id?.Trim()))
            {
                return (ApplicationResult<ImmutableArray<SavedTimerDefinition>>)new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(
                    new(ApplicationErrorCode.NotFound, "Unknown saved timer."));
            }
            SavedTimersDocument requested = change switch
            {
                SavedTimerChange.Add add => previous.AddOrReplace(add.Timer),
                SavedTimerChange.Remove remove => previous.Remove(remove.Id),
                _ => SavedTimersDocument.Empty
            };
            await this.Data.SavedTimers.SaveAsync(previous, requested, cancellationToken).ConfigureAwait(false);
            SavedTimersDocument committed = await this.Data.SavedTimers.LoadAsync(cancellationToken).ConfigureAwait(false);
            return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success(committed.Timers.ToImmutableArray());
        }, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ApplicationResult<ApplicationResult<ImmutableArray<SavedTimerDefinition>>>.Success success => success.Value,
            ApplicationResult<ApplicationResult<ImmutableArray<SavedTimerDefinition>>>.Failure failure => new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(failure.Error),
            _ => throw new InvalidOperationException("Unknown application result.")
        };
    }

    public Task<ApplicationResult<ImmutableArray<string>>> ListRecentInputsAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () => (await this.Data.AppSettings.LoadAsync(cancellationToken).ConfigureAwait(false)).RecentTimerInputs.ToImmutableArray(), cancellationToken);

    public Task<ApplicationResult<ImmutableArray<string>>> ClearRecentInputsAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () =>
        {
            LinuxAppSettings previous = await this.Data.AppSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
            LinuxAppSettings saved = await this.Data.AppSettings.SaveChangeAsync(previous, previous.ClearRecentTimerInputs(), cancellationToken).ConfigureAwait(false);
            return saved.RecentTimerInputs.ToImmutableArray();
        }, cancellationToken);

    public Task<ApplicationResult<ImmutableArray<CustomThemeDefinition>>> ListThemesAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () => (await this.Data.LoadOrDefaultAsync("custom-themes", CustomThemesDocument.Empty, cancellationToken).ConfigureAwait(false)).Themes.ToImmutableArray(), cancellationToken);

    public Task<ApplicationResult<bool>> ChangeThemesAsync(CustomThemesDocument previous, CustomThemesDocument requested, CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () => { await this.Data.SaveThemesChangeAsync(previous, requested, cancellationToken).ConfigureAwait(false); return true; }, cancellationToken);

    public Task<ApplicationResult<ApplicationDataSnapshot>> GetApplicationDataAsync(CancellationToken cancellationToken = default) =>
        this.CatalogAsync(() => this.ReadApplicationDataCoreAsync(cancellationToken), cancellationToken);

    public Task<ApplicationResult<bool>> SaveSavedTimersChangeAsync(SavedTimersDocument previous, SavedTimersDocument requested, CancellationToken cancellationToken = default) =>
        this.CatalogAsync(async () => { await this.Data.SavedTimers.SaveAsync(previous, requested, cancellationToken).ConfigureAwait(false); return true; }, cancellationToken);

    private async Task<ApplicationResult<T>> CatalogAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, bool accepted = false)
    {
        if (this.IsStopping && !accepted) { return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, "The runtime has stopped.")); }
        await this.catalogChanges.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return new ApplicationResult<T>.Success(await operation().ConfigureAwait(false)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ObjectDisposedException) { return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, "The runtime has stopped.")); }
        catch (Exception exception) { return new ApplicationResult<T>.Failure(new(ApplicationErrorCode.PersistenceFailure, exception.Message)); }
        finally { this.catalogChanges.Release(); }
    }

    private async Task RememberInputAfterAsync(Task previousSave, string input)
    {
        await Task.Yield();
        await previousSave.ConfigureAwait(false);
        await this.CatalogAsync(async () =>
        {
            LinuxAppSettings previous = await this.Data.AppSettings.LoadAsync().ConfigureAwait(false);
            return await this.Data.AppSettings.SaveChangeAsync(previous, previous.AddRecentTimerInput(input)).ConfigureAwait(false);
        }, CancellationToken.None, accepted: true).ConfigureAwait(false);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SavedTimerChange.Add), "add")]
[JsonDerivedType(typeof(SavedTimerChange.Remove), "remove")]
[JsonDerivedType(typeof(SavedTimerChange.Clear), "clear")]
public abstract record SavedTimerChange
{
    public sealed record Add(SavedTimerDefinition Timer) : SavedTimerChange;
    public sealed record Remove(string Id) : SavedTimerChange;
    public sealed record Clear : SavedTimerChange;
}
