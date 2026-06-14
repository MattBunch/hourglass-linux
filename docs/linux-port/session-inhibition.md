# Linux Session Inhibition

Stage 9 adds keep-awake behavior through `Hourglass.Platform.ISessionInhibitor`.

The first Linux backend uses `systemd-inhibit` to block idle and suspend while a timer is running. The Avalonia shell owns the inhibition lease and releases it when the timer is paused, reset, completed, or the view model is disposed.

## Behavior

- Inhibition is best-effort. If `systemd-inhibit` is missing or cannot start, timers still run normally.
- Core timer logic does not know about Linux session APIs.
- This feature prevents idle or suspend while Hourglass is already running.
- This feature does not wake a machine that is already suspended.

## Desktop Notes

GNOME, KDE Plasma, and other systemd-based desktop sessions commonly support `systemd-inhibit`. Portal-based inhibition may become preferable when packaging work starts, especially for Flatpak. That should be handled as a later backend behind the same `ISessionInhibitor` interface.
