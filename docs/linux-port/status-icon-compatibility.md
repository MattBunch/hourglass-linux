# Status Icon Compatibility

Hourglass Linux status icon support is optional and capability-based. The first backend uses Avalonia's status icon support where the current desktop session is likely to expose a tray, status notifier, or compatible panel area. Unsupported or unverified environments use a safe no-op backend so the timer remains fully usable without a tray.

`IStatusIconService.IsSupported` means Hourglass selected and initialized a status icon backend. It does not prove that the current desktop visibly rendered the icon. Visible support must be manually validated through the actual desktop, panel, and packaging path users will run.

`IStatusIconService.CanRecoverHiddenWindow` is a stricter capability used for **Hide to notification area**. It must remain false unless the backend or launch path can verify that a hidden window is recoverable. The current Avalonia backend does not expose a reliable visible-icon acknowledgement, and Hourglass does not yet raise an existing hidden instance on relaunch, so hide-to-notification-area remains disabled even when the status icon itself is available.

## Backend Selection

The Linux services layer reads:

- `XDG_CURRENT_DESKTOP`
- `XDG_SESSION_DESKTOP`
- `DESKTOP_SESSION`
- `HOURGLASS_STATUS_ICON_BACKEND`

Desktop identifiers are matched case-insensitively. Colon-separated values such as `ubuntu:GNOME` are split and evaluated individually.

`HOURGLASS_STATUS_ICON_BACKEND` supports:

- `auto`: use normal desktop detection.
- `avalonia`: force the Avalonia status icon backend when a session bus is available.
- `none`: force the unsupported no-op backend.

In automatic mode, Hourglass selects the Avalonia backend only for deliberately recognized tray-capable identifiers such as KDE Plasma, LXQt, Unity or Ubuntu sessions, Xfce, and Cinnamon. Unknown desktops, missing desktop environment values, missing session bus, explicit disablement, and backend initialization failure select the unsupported no-op backend.

## Cross-Desktop Manual Validation

| Environment | Role | Required result |
| --- | --- | --- |
| KDE Plasma | Primary supported environment | Status icon is visible and menu actions work |
| Xfce X11 | Traditional tray compatibility check | Status icon is visible and menu actions work |
| Cinnamon or MATE | Exploratory tray compatibility check | Record the actual result; claim support only if repeatable |
| Ubuntu GNOME with AppIndicator/KStatusNotifier extension | Extension-dependent check | Record extension details and actual result |
| Fedora GNOME stock | Unsupported fallback | No visible icon is acceptable; no errors or timer regressions |

For every tested environment, record:

- distribution and version;
- desktop environment and version;
- `XDG_CURRENT_DESKTOP`;
- `XDG_SESSION_TYPE`;
- whether the app is running under X11, XWayland, or native Wayland;
- panel, tray, or status notifier extension name and version where applicable;
- backend selected;
- whether the status icon is visible;
- show/restore behavior;
- whether hide-to-notification-area is enabled;
- hidden-window recovery behavior, when enabled by a future backend;
- pause/resume menu behavior;
- stop menu behavior;
- restart menu behavior;
- exit menu behavior;
- whether disabling **Show in notification area** hides the icon;
- whether application close clears the icon;
- validation date.

## Manual Validation Steps

Launch Hourglass through the installed `.desktop` entry where possible. Some desktops associate tray/status icon identity with the desktop entry, application ID, or window class.

1. Start Hourglass with **Show in notification area** disabled and confirm no status icon is shown.
2. Enable **Show in notification area** from the window context menu.
3. Confirm the status icon appears only on supported environments.
4. Open the icon menu and confirm the visible actions match the current timer state.
5. Start a duration timer and confirm Pause, Stop, and Restart are enabled.
6. Pause and resume from the status icon menu.
7. Stop from the status icon menu and confirm the window returns to the stopped state.
8. Confirm **Hide to notification area** remains disabled unless the selected backend explicitly reports hidden-window recovery support.
9. Restart from the status icon menu and confirm the timer restarts from the original duration.
10. Disable **Show in notification area** and confirm the icon disappears.
11. Re-enable it and confirm the icon returns.
12. Exit from the status icon menu and confirm normal close/prompt behavior still applies.
13. Close Hourglass while the icon is visible and confirm no stale icon remains.
14. Rapidly pause, resume, restart, show, and stop, confirming the final state is correct and no exceptions occur.
15. If a future backend enables hiding, start again, hide to the notification area, then show/restore from the status icon or relaunch path.

## Validated Combinations

No graphical desktop combinations have been validated in this repository snapshot yet. Do not mark an entire desktop environment as supported until an exact distribution, desktop, session type, panel or extension, and validation date are recorded here.

Use exact tested combinations, for example:

- `KDE Plasma 6.4 on Fedora 42 Wayland: supported`
- `Fedora GNOME stock shell: unsupported/no-op`

## Completion Wording

Milestone 4.2 is implemented for explicitly documented Avalonia-supported desktop, panel, and tray/status icon combinations. Unsupported or unverified desktops continue through the safe no-op backend until separately tested or given dedicated backends.
