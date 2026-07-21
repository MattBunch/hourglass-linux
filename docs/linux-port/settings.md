# Linux Settings Storage

Stage 8 stores Linux MVP settings through `Hourglass.Platform.ISettingsStore` and a Linux JSON file implementation in `Hourglass.Linux.Services`. Post-Milestone-10 work keeps those files compatible while splitting the in-memory settings model into focused immutable records.

## Location

Settings are stored below the XDG config directory:

- `$XDG_CONFIG_HOME/hourglass-linux` when `XDG_CONFIG_HOME` is set to an absolute fully qualified path.
- `~/.config/hourglass-linux` when the user home directory is absolute.
- The platform application-data folder only when it is absolute.

Relative or whitespace-only XDG paths are ignored so settings are never written relative to the current working directory. If no safe absolute directory can be resolved, startup fails with a clear configuration error instead of creating files in an unsafe location.

The first settings file is `app.json`. Settings keys are still validated before file access, so document names cannot escape the selected Hourglass settings directory.

## Stored Data

The persisted settings documents store:

- Recent timer inputs, so the timer field can restore the most recently started timer.
- Whether timer-expiry desktop notifications are enabled.
- Timer behavior preferences such as sound, theme, window title mode, progress display, keep-awake behavior, and close/loop options.
- Saved timer definitions, active timer sessions, custom themes, and active-session window geometry in separate versioned documents.

Migrated Windows settings are not part of this stage.

## In-Memory Model

`LinuxAppSettings` remains the compatibility shape for `app.json`, so older settings files continue to deserialize safely.

Runtime code should prefer focused immutable snapshots from `Hourglass.Core.Settings` when it needs to reason about groups of settings:

- `ApplicationPreferences` for app-level UI and startup preferences.
- `TimerDefaults` for options applied to newly started or saved timers.
- `LinuxSettingsSnapshot` for mapping between the compatibility DTO and focused settings models.
- `LinuxSettingsMerger` for pure previous/requested/latest merge behavior during coordinated saves.
- `ActiveTimerSessionSnapshot` for active timer session restore/save decisions.
- `ActiveTimerSessionsSnapshot` for immutable multi-window active-session collections.
- `WindowGeometrySnapshot` for active-session window placement.

The persisted active-session documents remain the compatibility shapes for `active-session` and `active-sessions`, while runtime code maps them into focused snapshots before restoring timer state. New setting groups should be added to the focused model first, with explicit mapping back to the persisted compatibility document.

## Concurrent Saves

`app.json` is global application state shared by every timer window. Windows created by `TimerWindowCoordinator` use one shared `CoordinatedAppSettingsStore` so load/merge/write transactions are serialized across windows. The coordinator reloads the latest persisted snapshot under the same gate, merges the requesting window's previous/requested settings against that latest snapshot, writes one full document, and returns the actual persisted result.

`LinuxSettingsMerger` merges independently changed fields first, then applies invariants once to the merged result. For example, notification and always-on-top changes do not overwrite each other, loop timer and loop sound changes are preserved unless close-on-expiry requires resolution, and audio enabled state remains coherent with the selected sound. When two requests directly conflict on an invariant, the later serialized request wins for that invariant only.

Recent timer inputs remain ordered, unique, and bounded during merges.

## Active Sessions

Active timer sessions are per-window state stored separately from `app.json`. Startup restoration applies restored sessions, saved timers, or command-line timer requests before queueing the first replacement active-session document. If restored sessions are invalid, they are pruned only after startup initialization has completed.

## Privacy

The Linux port does not create or migrate Windows updater UUIDs or other persistent tracking identifiers. Settings are local user configuration only.

Malformed or missing settings files are treated as absent settings. The app should keep starting with defaults rather than failing startup because of local configuration data.

Nullable reference analysis is enabled for the modern Core project and the Linux solution. Mutable serialization DTOs keep nullable annotations that match legacy-compatible JSON and XML shapes.
