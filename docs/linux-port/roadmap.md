# Linux Port Roadmap

## Phase 1: Scaffold

- Add modern .NET 10 SDK-style projects.
- Add a placeholder Avalonia shell.
- Add a modern unit test project.
- Document architecture, risks, parity gaps, and decisions.
- Leave the legacy Windows WPF app untouched.

## Phase 2: Core Extraction

- Reusable parsing logic has been moved into `Hourglass.Core`.
- Core timer serialization DTOs and timer-start models have been moved into `Hourglass.Core`; broader settings migration remains future work.
- Modern tests cover parser, serialization, and countdown-engine behavior.
- A monotonic-clock countdown engine has been introduced.
- The source inventory for remaining reusable and replacement-required code lives in `docs/linux-port/source-inventory.md`.

## Phase 3: Linux MVP

- Primary timer UI in Avalonia is implemented.
- Notification-first timer expiry behavior is implemented.
- Audio alerts are implemented.
- Settings storage under Linux user config/data paths is implemented.
- Single-instance behavior is implemented.
- Session inhibition for keep-awake behavior is implemented.

Tray support remains optional for the first Linux UI.

## Phase 4: Packaging

- Disable or replace the Windows in-app updater for Linux builds.
- Plan Flatpak as the primary Linux package.
- Add AppImage as a secondary portable artifact.
- Evaluate distro-native packaging after the MVP stabilizes.

## Phase 11: Linux Audio Alerts

- Play the built-in Normal beep when a timer expires.
- Keep audio playback behind `IAudioAlertService`.
- Treat Linux audio playback as best-effort and independent from notifications.
- Preserve old settings files while adding an audio-enabled preference.

## Phase 12: Single-Instance Behavior

- Linux single-instance behavior is implemented behind `ISingleInstanceService`.
- Command-line handoff remains deferred.
- Existing-window activation remains deferred.

## Phase 5: Advanced Parity

- Evaluate optional tray/status notifier support across desktop environments.
- Evaluate wake-from-suspend only after the MVP.
- Add broader GNOME, KDE, Wayland, X11, XFCE, Cinnamon, and MATE smoke testing.
