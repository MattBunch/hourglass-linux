# Linux Settings Storage

Stage 8 stores Linux MVP settings through `Hourglass.Platform.ISettingsStore` and a Linux JSON file implementation in `Hourglass.Linux.Services`.

## Location

Settings are stored below the XDG config directory:

- `$XDG_CONFIG_HOME/hourglass-linux` when `XDG_CONFIG_HOME` is set.
- `~/.config/hourglass-linux` otherwise.

The first settings file is `app.json`.

## Stored Data

The MVP settings model stores:

- Recent timer inputs, so the timer field can restore the most recently started timer.
- Whether timer-expiry desktop notifications are enabled.
- Timer behavior preferences such as sound, theme, window title mode, progress display, keep-awake behavior, and close/loop options.
- Saved timer definitions, active timer sessions, custom themes, and active-session window geometry in separate versioned documents.

Migrated Windows settings are not part of this stage.

## Privacy

The Linux port does not create or migrate Windows updater UUIDs or other persistent tracking identifiers. Settings are local user configuration only.

Malformed or missing settings files are treated as absent settings. The app should keep starting with defaults rather than failing startup because of local configuration data.
