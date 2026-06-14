# Linux Notifications

Stage 7 uses the `Hourglass.Platform.INotificationService` boundary and implements Linux timer-expiry notifications in `Hourglass.Linux.Services`.

The first Linux backend shells out to `notify-send`, which targets the freedesktop desktop notification path used by common Linux environments. This keeps the implementation small while the Linux port is still moving through MVP features.

## Desktop Behavior

- GNOME: notifications may appear in the notification list and can be disabled by the user or session policy. Hourglass must keep the completed timer visible in the app UI.
- KDE Plasma: notifications normally appear through the Plasma notification daemon. User notification rules may suppress or group them.
- XFCE: notifications normally appear through `xfce4-notifyd` when installed and running. Minimal installations may not provide a notification daemon.

Notification delivery is best-effort. If `notify-send` is missing, the process exits with an error, or no notification daemon is available, the app must not crash and the timer-complete UI state remains the recoverable source of truth.

Tray/status notifier behavior is intentionally separate and remains optional for a later phase.
