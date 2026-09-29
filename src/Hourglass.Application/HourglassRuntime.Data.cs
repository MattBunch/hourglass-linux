namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Platform;
using Hourglass.Settings;
using Hourglass.Timing;

public sealed partial class HourglassRuntime
{
    private readonly CoordinatedSessionInhibitor sharedInhibitor;
    private readonly object initializationGate = new();
    private readonly Dictionary<string, SessionPresentation> presentations = new(StringComparer.Ordinal);
    private Task<ApplicationDataSnapshot>? dataInitialization;
    private Task<ApplicationResult<RuntimeStartupSnapshot>>? sessionInitialization;
    private bool suppressPersistence;
    private bool persistenceReady;
    private WakeAlarmController? wakeAlarms;
    private Task pendingWake = Task.CompletedTask;
    private bool wakeEnabled;
    private long wakeGeneration;
    internal ApplicationData Data { get; }

    public Task<ApplicationDataSnapshot> LoadApplicationDataAsync(CancellationToken cancellationToken = default)
    {
        lock (this.initializationGate)
        {
            this.dataInitialization ??= LoadAsync();
            return this.dataInitialization.WaitAsync(cancellationToken);
        }
        Task<ApplicationDataSnapshot> LoadAsync() => this.ReadApplicationDataAsync();
    }

    public async Task<ApplicationDataSnapshot> ReadApplicationDataAsync(CancellationToken cancellationToken = default)
    {
        ApplicationResult<ApplicationDataSnapshot> result = await this.GetApplicationDataAsync(cancellationToken).ConfigureAwait(false);
        return result is ApplicationResult<ApplicationDataSnapshot>.Success success ? success.Value : throw new ObjectDisposedException(nameof(HourglassRuntime));
    }

    private async Task<ApplicationDataSnapshot> ReadApplicationDataCoreAsync(CancellationToken cancellationToken)
    {
        LinuxAppSettings settings = await this.Data.LoadOrDefaultAsync("app", LinuxAppSettings.Default, cancellationToken).ConfigureAwait(false);
        SavedTimersDocument saved = await this.Data.LoadOrDefaultAsync("saved-timers", SavedTimersDocument.Empty, cancellationToken).ConfigureAwait(false);
        CustomThemesDocument themes = await this.Data.LoadOrDefaultAsync("custom-themes", CustomThemesDocument.Empty, cancellationToken).ConfigureAwait(false);
        await this.InvokeAsync(() =>
        {
            bool changed = this.wakeEnabled != settings.WakeFromSuspendEnabled;
            this.wakeEnabled = settings.WakeFromSuspendEnabled;
            this.persistenceReady = true;
            if (changed) { this.QueueWakeAlarm(); }
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return new(settings, saved, themes);
    }

    public Task<ApplicationResult<RuntimeStartupSnapshot>> InitializeSessionsAsync(string? launchInput = null, string? launchTitle = null, CancellationToken cancellationToken = default)
    {
        lock (this.initializationGate)
        {
            this.sessionInitialization ??= InitializeAsync();
            return this.sessionInitialization.WaitAsync(cancellationToken);
        }
        async Task<ApplicationResult<RuntimeStartupSnapshot>> InitializeAsync()
        {
            bool initialized = false;
            await this.InvokeAsync(() => this.suppressPersistence = true).ConfigureAwait(false);
            try
            {
                ApplicationDataSnapshot data = await this.LoadApplicationDataAsync().ConfigureAwait(false);
                ImmutableArray<RestoredSession> persisted = data.Settings.RestoreActiveSessionOnStartup
                    ? await this.Data.ActiveSessions.LoadAsync(this.wallClockNow(), CancellationToken.None).ConfigureAwait(false) : [];
                foreach (RestoredSession restored in persisted)
                {
                    await this.CreateSessionAsync(new(restored.SessionId, restored.Session.TimerStartInput, restored.Session.TimerTitle,
                        TimerDefaults.FromSettings(data.Settings), ApplicationPreferences.FromSettings(data.Settings))).ConfigureAwait(false);
                    await this.RestoreSessionAsync(restored.SessionId, restored.Session,
                        ApplicationPreferences.FromSettings(NormalizeThemeSelection(restored.Session.HasOptions ? restored.Session.Options.ApplyTo(data.Settings) : data.Settings, data.CustomThemes))).ConfigureAwait(false);
                    await this.UpdatePresentationAsync(restored.SessionId, new(restored.Session.TimerInput, restored.Session.PresentationMode, restored.Session.WindowGeometry)).ConfigureAwait(false);
                }
                if (persisted.IsEmpty && data.Settings.OpenSavedTimersOnStartup)
                {
                    foreach (SavedTimerDefinition saved in data.SavedTimers.Timers)
                    {
                        LinuxAppSettings options = NormalizeThemeSelection(saved.Options.ApplyTo(data.Settings), data.CustomThemes);
                        await this.CreateSessionAsync(new(Guid.NewGuid().ToString("N"), saved.TimerInput, saved.TimerTitle,
                            TimerDefaults.FromSettings(options), ApplicationPreferences.FromSettings(options))).ConfigureAwait(false);
                        try { await this.Data.AppSettings.SaveChangeAsync(data.Settings, options).ConfigureAwait(false); }
                        catch (Exception exception)
                        {
                            this.diagnostics.TryRecord(new(DiagnosticSeverity.Warning, DiagnosticFailureClass.DataRecovery,
                                "settings", "startup-saved-options", "app", "Saved timer defaults could not be persisted.", exception));
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(launchInput))
                {
                    string id = Guid.NewGuid().ToString("N");
                    await this.CreateSessionAsync(new(id, launchInput, launchTitle ?? string.Empty, TimerDefaults.FromSettings(data.Settings), ApplicationPreferences.FromSettings(data.Settings))).ConfigureAwait(false);
                    ApplicationResult<TimerSessionSnapshot> launch = await this.ExecuteAsync(new SessionCommand.Start(id)).ConfigureAwait(false);
                    if (launch is ApplicationResult<TimerSessionSnapshot>.Failure failed)
                    {
                        return new ApplicationResult<RuntimeStartupSnapshot>.Failure(failed.Error);
                    }
                }
                if (await this.InvokeAsync(() => this.sessions.Count == 0).ConfigureAwait(false))
                {
                    await this.CreateSessionAsync(new(Guid.NewGuid().ToString("N"), data.Settings.GetInitialTimerInput(TimerStart.Default.ToString()), string.Empty,
                        TimerDefaults.FromSettings(data.Settings), ApplicationPreferences.FromSettings(data.Settings))).ConfigureAwait(false);
                }
                initialized = true;
                return await this.QueryAsync(() => (ApplicationResult<RuntimeStartupSnapshot>)new ApplicationResult<RuntimeStartupSnapshot>.Success(
                    new(data, this.CapturePersistedSessions().Select(item => item with
                    { ExpiredWhileClosed = persisted.Any(original => original.SessionId == item.SessionId && original.ExpiredWhileClosed) }).ToImmutableArray())), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await this.InvokeAsync(() => { this.suppressPersistence = false; this.persistenceReady = initialized; this.QueuePersistence(); this.QueueWakeAlarm(); return true; }).ConfigureAwait(false);
            }
        }
    }

    public async Task<ApplicationResult<TimerSessionSnapshot>> CreateSavedSessionAsync(SavedTimerDefinition saved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!saved.IsValid) { return Failure(ApplicationErrorCode.Validation, "Invalid saved timer."); }
        ApplicationDataSnapshot data = await this.ReadApplicationDataAsync(cancellationToken).ConfigureAwait(false);
        LinuxAppSettings settings = NormalizeThemeSelection(saved.Options.ApplyTo(data.Settings), data.CustomThemes);
        if (TimerInputValidation.Parse(saved.TimerInput, this.wallClockNow()) is ApplicationResult<TimerStart>.Failure invalid)
        {
            return new ApplicationResult<TimerSessionSnapshot>.Failure(invalid.Error);
        }
        string id = Guid.NewGuid().ToString("N");
        ApplicationResult<TimerSessionSnapshot> created = await this.CreateSessionAsync(new(id, saved.TimerInput, saved.TimerTitle,
            TimerDefaults.FromSettings(settings), ApplicationPreferences.FromSettings(settings)), cancellationToken).ConfigureAwait(false);
        if (created is ApplicationResult<TimerSessionSnapshot>.Failure) { return created; }
        try
        {
            ApplicationResult<TimerSessionSnapshot> started = await this.ExecuteAsync(new SessionCommand.Start(id), cancellationToken).ConfigureAwait(false);
            if (started is ApplicationResult<TimerSessionSnapshot>.Failure) { await this.RemoveAsync(id).ConfigureAwait(false); }
            return started;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.RemoveAsync(id).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ApplicationResult<ImmutableArray<TimerSessionSnapshot>>> StartSavedSessionsAsync(
        SavedTimerSelection selection, CancellationToken cancellationToken = default)
    {
        ApplicationResult<ImmutableArray<SavedTimerDefinition>> resolved = await this.ResolveSavedAsync(selection, cancellationToken).ConfigureAwait(false);
        if (resolved is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure failed)
        {
            return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure(failed.Error);
        }
        ImmutableArray<SavedTimerDefinition> templates = ((ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success)resolved).Value;
        var started = ImmutableArray.CreateBuilder<TimerSessionSnapshot>();
        try
        {
            foreach (SavedTimerDefinition template in templates)
            {
                ApplicationResult<TimerSessionSnapshot> result = await this.CreateSavedSessionAsync(template, cancellationToken).ConfigureAwait(false);
                if (result is ApplicationResult<TimerSessionSnapshot>.Failure error)
                {
                    await CleanupAsync().ConfigureAwait(false);
                    return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure(error.Error);
                }
                started.Add(((ApplicationResult<TimerSessionSnapshot>.Success)result).Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
        ApplicationResult<bool> durable;
        try { durable = await this.FlushPersistenceAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
        if (durable is ApplicationResult<bool>.Failure storage)
        {
            await CleanupAsync().ConfigureAwait(false);
            return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Failure(storage.Error);
        }
        return new ApplicationResult<ImmutableArray<TimerSessionSnapshot>>.Success(started.ToImmutable());

        async Task CleanupAsync()
        {
            foreach (TimerSessionSnapshot session in started) { await this.RemoveAsync(session.SessionId).ConfigureAwait(false); }
            await this.FlushPersistenceAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<ApplicationResult<ImmutableArray<SavedTimerDefinition>>> ResolveSavedAsync(
        SavedTimerSelection selection, CancellationToken cancellationToken)
    {
        ApplicationResult<ImmutableArray<SavedTimerDefinition>> listed = await this.ListSavedTimersAsync(cancellationToken).ConfigureAwait(false);
        if (listed is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure failure) { return failure; }
        ImmutableArray<SavedTimerDefinition> templates = ((ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success)listed).Value;
        ApplicationResult<ImmutableArray<SavedTimerDefinition>> selected = SavedTimerSelector.Resolve(templates, selection);
        if (selected is ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure) { return selected; }
        DateTime validationTime = this.wallClockNow();
        foreach (SavedTimerDefinition template in ((ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success)selected).Value)
        {
            if (!template.IsValid || TimerInputValidation.Parse(template.TimerInput, validationTime) is ApplicationResult<TimerStart>.Failure)
            {
                return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.Validation,
                    $"Invalid saved timer: {template.Id}"));
            }
        }
        return selected;
    }

    public Task<ApplicationResult<bool>> UpdatePresentationAsync(string sessionId, SessionPresentation presentation, CancellationToken cancellationToken = default) =>
        this.QueryAsync(() =>
        {
            if (!this.sessions.ContainsKey(sessionId)) { return (ApplicationResult<bool>)new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.NotFound, "Unknown session.")); }
            if (presentation == null || presentation.TimerInput == null || !Enum.IsDefined(presentation.PresentationMode))
            {
                return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.Validation, "Invalid presentation metadata."));
            }
            this.presentations[sessionId] = presentation;
            this.persistenceReady = true;
            this.QueuePersistence();
            return new ApplicationResult<bool>.Success(true);
        }, cancellationToken);

    public async Task SaveLegacySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ActiveTimerSessionDocument? document = await this.InvokeAsync(() => this.CapturePersistedSessions()
            .FirstOrDefault(item => item.SessionId == sessionId)?.Session.ToDocument(), cancellationToken).ConfigureAwait(false);
        if (document != null) { await this.Data.SaveAsync("active-session", document, cancellationToken).ConfigureAwait(false); }
    }

    private ImmutableArray<RestoredSession> CapturePersistedSessions()
    {
        var result = ImmutableArray.CreateBuilder<RestoredSession>();
        foreach (var pair in this.sessions)
        {
            TimerSession session = pair.Value.Session;
            SessionPresentation presentation = this.presentations.GetValueOrDefault(pair.Key)
                ?? new(session.TimerInput, session.Countdown.State == TimerState.Stopped ? ActiveTimerPresentationMode.Input : ActiveTimerPresentationMode.Status);
            ActiveTimerSessionSnapshot snapshot = ActiveTimerSessionSnapshot.FromState(presentation.TimerInput, session.TimerTitle,
                presentation.PresentationMode, session.Countdown, this.wallClockNow(),
                SavedTimerOptions.FromSettings(new LinuxSettingsSnapshot([], session.Preferences, session.Options).ToSettings()), presentation.WindowGeometry);
            result.Add(new(pair.Key, snapshot));
        }
        return result.ToImmutable();
    }

    private void QueuePersistence()
    {
        if (!this.persistenceReady || this.suppressPersistence || this.IsStopping) { return; }
        ImmutableArray<ActiveTimerSessionDefinition> sessions = this.CapturePersistedSessions()
            .Select(item => new ActiveTimerSessionDefinition(item.SessionId, item.Session.ToDocument())).ToImmutableArray();
        _ = this.ObserveLateCleanupAsync(this.Data.ActiveSessions.SaveAsync(sessions), "save-sessions");
    }

    public async Task<ApplicationResult<bool>> FlushPersistenceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Task recentWork = await this.InvokeAsync(() => this.pendingRecentSave, cancellationToken).ConfigureAwait(false);
            await recentWork.WaitAsync(cancellationToken).ConfigureAwait(false);
            await this.catalogChanges.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await this.Data.FlushAsync(cancellationToken).ConfigureAwait(false); }
            finally { this.catalogChanges.Release(); }
        }
        catch (ObjectDisposedException)
        {
            return new ApplicationResult<bool>.Failure(new(ApplicationErrorCode.RuntimeUnavailable, "The runtime has stopped."));
        }
    }

    public void ConfigureWakeAlarms(IWakeAlarmService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        this.Invoke(() =>
        {
            if (this.wakeAlarms != null) { throw new InvalidOperationException("Wake alarms are already configured."); }
            this.wakeAlarms = new WakeAlarmController(service, () => new DateTimeOffset(this.wallClockNow()), this.diagnostics, this.services.InhibitionReason);
            this.QueueWakeAlarm();
            return true;
        });
    }

    public async Task WaitForServicesAsync(CancellationToken cancellationToken = default)
    {
        await this.Data.FlushAsync(cancellationToken).ConfigureAwait(false);
        Task work = await this.InvokeAsync(() => this.pendingWake, cancellationToken).ConfigureAwait(false);
        await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void QueueWakeAlarm()
    {
        if (this.wakeAlarms == null || this.suppressPersistence) { return; }
        this.wakeAlarms.Invalidate();
        WakeAlarmTimerSnapshot[] timers = this.sessions.Values.Select(item => new WakeAlarmTimerSnapshot(item.Session.Countdown.State, item.Session.Snapshot(item.Id).Countdown.EndTime)).ToArray();
        Task previous = this.pendingWake;
        long generation = ++this.wakeGeneration;
        bool enabled = this.wakeEnabled;
        this.pendingWake = ApplyAsync();
        _ = this.ObserveLateCleanupAsync(this.pendingWake, "wake-alarm");
        async Task ApplyAsync()
        {
            await Task.Yield();
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            if (generation != Volatile.Read(ref this.wakeGeneration) || this.IsStopping) { return; }
            await this.wakeAlarms.ApplyAsync(timers, enabled).ConfigureAwait(false);
        }
    }

    private void OnSettingsChanged(LinuxAppSettings settings)
    {
        _ = this.QueryAsync(() =>
        {
            if (this.wakeEnabled != settings.WakeFromSuspendEnabled)
            {
                this.wakeEnabled = settings.WakeFromSuspendEnabled;
                this.QueueWakeAlarm();
            }
            return (ApplicationResult<bool>)new ApplicationResult<bool>.Success(true);
        }, CancellationToken.None);
    }

    private async Task DisposeWakeAlarmsAsync()
    {
        if (this.wakeAlarms == null) { return; }
        Task disposal = this.wakeAlarms.DisposeAsync().AsTask();
        try { await Task.WhenAll(this.pendingWake, disposal).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task DrainDataAsync()
    {
        await this.pendingRecentSave.ConfigureAwait(false);
        await this.catalogChanges.WaitAsync().ConfigureAwait(false);
        try { await this.Data.FlushAsync().ConfigureAwait(false); }
        finally { this.catalogChanges.Release(); }
    }

    public async Task<ActiveTimerSessionSnapshot?> LoadLegacyRestorationAsync(CancellationToken cancellationToken = default)
    {
        ActiveTimerSessionDocument? document = await this.Data.LoadOrDefaultAsync<ActiveTimerSessionDocument?>("active-session", null, cancellationToken).ConfigureAwait(false);
        return document == null ? null : ActiveTimerSessionSnapshot.FromDocument(document, this.wallClockNow(), TimeSpan.Zero);
    }

    public static LinuxAppSettings NormalizeThemeSelection(LinuxAppSettings settings, CustomThemesDocument themes)
    {
        if (settings.ThemePreference != LinuxThemePreference.Custom) { return settings with { CustomThemeId = null }; }
        return themes.Find(settings.CustomThemeId) == null
            ? settings with { ThemePreference = LinuxThemePreference.System, CustomThemeId = null } : settings;
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> documents = new(StringComparer.Ordinal);
        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(this.documents.TryGetValue(key, out object? value) ? (T?)value : default);
        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (value is not null) { this.documents[key] = value; }
            return Task.CompletedTask;
        }
    }
}
