namespace Hourglass.Application;

using Hourglass.Platform;
using Hourglass.Settings;

/// <summary>The authority's shared document boundary. File formats remain in the existing Core models.</summary>
public sealed class ApplicationData : ISettingsStore
{
    private readonly ISettingsStore store;
    private readonly IDiagnosticSink diagnostics;
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly object saveGate = new();
    private Task pendingSave = Task.CompletedTask;
    private readonly Dictionary<string, Exception> saveFailures = new(StringComparer.Ordinal);

    public ApplicationData(ISettingsStore store, IDiagnosticSink? diagnostics = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.diagnostics = diagnostics ?? NoOpDiagnosticSink.Instance;
        this.AppSettings = new CoordinatedAppSettingsStore(this);
        this.SavedTimers = new CoordinatedSavedTimersStore(this);
        this.ActiveSessions = new ActiveSessionRepository(this, this.diagnostics);
    }

    internal event Action<LinuxAppSettings>? SettingsChanged;

    public IAppSettingsStore AppSettings { get; }
    public ISavedTimersStore SavedTimers { get; }
    internal ActiveSessionRepository ActiveSessions { get; }

    public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) => this.store.LoadAsync<T>(key, cancellationToken);

    public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        lock (this.saveGate)
        {
            return this.pendingSave = SaveAfterAsync(this.pendingSave);
        }
        async Task SaveAfterAsync(Task previous)
        {
            // Ensure even a synchronous backend runs outside the runtime mutation queue.
            await Task.Yield();
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            try
            {
                await this.store.SaveAsync(key, value, cancellationToken).ConfigureAwait(false);
                lock (this.saveGate) { this.saveFailures.Remove(key); }
                if (key == "app" && value is LinuxAppSettings settings) { this.SettingsChanged?.Invoke(settings); }
            }
            catch (Exception exception)
            {
                lock (this.saveGate) { this.saveFailures[key] = exception; }
                this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.DataRecovery,
                    "settings", "save", key, "Shared document save failed.", exception));
                throw;
            }
        }
    }

    public async Task SaveThemesChangeAsync(CustomThemesDocument previous, CustomThemesDocument requested, CancellationToken cancellationToken = default)
    {
        await this.changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CustomThemesDocument latest = await this.LoadOrDefaultAsync("custom-themes", previous, cancellationToken).ConfigureAwait(false);
            await this.SaveAsync("custom-themes", CustomThemeChanges.Merge(previous, requested, latest), cancellationToken).ConfigureAwait(false);
        }
        finally { this.changes.Release(); }
    }

    internal async Task<T> LoadOrDefaultAsync<T>(string key, T fallback, CancellationToken cancellationToken)
    {
        try { return await this.LoadAsync<T>(key, cancellationToken).ConfigureAwait(false) ?? fallback; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.DataRecovery,
                "settings", "load", key, "Document load failed; using fallback.", exception));
            return fallback;
        }
    }

    public async Task<ApplicationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        Task tail;
        lock (this.saveGate) { tail = this.pendingSave; }
        try { await tail.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.PersistenceFailure, exception.Message)); }
        lock (this.saveGate)
        {
            return this.saveFailures.Count == 0 ? new ApplicationResult<bool>.Success(true)
                : new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.PersistenceFailure, this.saveFailures.Values.First().Message));
        }
    }
}
