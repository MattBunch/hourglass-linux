# Linux Port Roadmap

## Phase 1: Scaffold

- Add modern .NET 10 SDK-style projects.
- Add a placeholder Avalonia shell.
- Add a modern unit test project.
- Document architecture, risks, parity gaps, and decisions.
- Leave the legacy Windows WPF app untouched.

## Phase 2: Core Extraction

- Move reusable parsing logic into `Hourglass.Core`.
- Move serialization and settings models into `Hourglass.Core`.
- Add tests around existing parsing behavior before changing semantics.
- Introduce a monotonic-clock countdown engine.

## Phase 3: Linux MVP

- Build the primary timer UI in Avalonia.
- Add notification-first timer expiry behavior.
- Add audio alerts.
- Add settings storage under Linux user config/data paths.
- Add single-instance behavior.
- Add session inhibition for keep-awake behavior.

Tray support remains optional for the first Linux UI.

## Phase 4: Packaging

- Disable or replace the Windows in-app updater for Linux builds.
- Plan Flatpak as the primary Linux package.
- Add AppImage as a secondary portable artifact.
- Evaluate distro-native packaging after the MVP stabilizes.

## Phase 5: Advanced Parity

- Evaluate optional tray/status notifier support across desktop environments.
- Evaluate wake-from-suspend only after the MVP.
- Add broader GNOME, KDE, Wayland, X11, XFCE, Cinnamon, and MATE smoke testing.
