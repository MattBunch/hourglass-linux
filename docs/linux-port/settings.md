# Linux Settings Storage

Stage 8 stores Linux MVP settings through `Hourglass.Platform.ISettingsStore` and a Linux JSON file implementation in `Hourglass.Linux.Services`. Post-Milestone-10 work keeps those files compatible while splitting the in-memory settings model into focused immutable records.

## Location

Settings are stored below the XDG config directory:

- `$XDG_CONFIG_HOME/hourglass-linux` when `XDG_CONFIG_HOME` is set.
- `~/.config/hourglass-linux` otherwise.

The first settings file is `app.json`.

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

New setting groups should be added to the focused model first, with explicit mapping back to the persisted compatibility document.

## Privacy

The Linux port does not create or migrate Windows updater UUIDs or other persistent tracking identifiers. Settings are local user configuration only.

Malformed or missing settings files are treated as absent settings. The app should keep starting with defaults rather than failing startup because of local configuration data.
