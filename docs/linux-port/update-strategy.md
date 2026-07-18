# Linux Update Strategy

Hourglass Linux does not use the legacy Windows in-app updater.

## Policy

- Flatpak builds should be updated by the configured Flatpak repository or store.
- Distro-native packages should be updated by the system package manager.
- AppImage builds may later use AppImage-specific update metadata after release signing and hosting are designed.
- The app must not silently download and execute replacement binaries.
- Linux builds must not create or transmit the Windows updater UUID.
- Any future network update check must be opt-in and documented before it is enabled.

## Current Release Information

The current Linux app version comes from `src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj`.

Milestone 10 may expose static version or release information, but it must not add update status, telemetry, persistent update identifiers, or outbound network calls.
