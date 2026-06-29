# Desktop Progress Compatibility

Hourglass Linux taskbar/dock progress is capability-based. The first visible backend targets desktops and docks compatible with the Unity LauncherEntry D-Bus signal. Unsupported or unverified environments use the safe no-op backend so timer behavior remains unchanged.

`IDesktopProgressService.IsSupported` means Hourglass selected and initialized a backend. It does not prove that the current dock visibly rendered the one-way D-Bus signal. Visible support must be manually validated through an installed desktop launcher.

## Backend Selection

The Linux services layer reads:

- `XDG_CURRENT_DESKTOP`
- `XDG_SESSION_DESKTOP`
- `DESKTOP_SESSION`
- `HOURGLASS_DESKTOP_PROGRESS_BACKEND`

Desktop identifiers are matched case-insensitively. Colon-separated values such as `ubuntu:GNOME` are split and evaluated individually.

`HOURGLASS_DESKTOP_PROGRESS_BACKEND` supports:

- `auto`: use normal desktop detection.
- `unity`: force the Unity LauncherEntry backend when the session bus sender can be initialized.
- `none`: force the unsupported no-op backend.

In automatic mode, Hourglass selects the Unity LauncherEntry backend only for deliberately recognized Unity-compatible identifiers such as Unity or Ubuntu Dock sessions. Unknown desktops, missing desktop environment values, and failed D-Bus sender initialization select the unsupported no-op backend.

## Cross-Desktop Manual Validation

| Environment | Role | Required result |
| --- | --- | --- |
| Ubuntu GNOME with Ubuntu Dock, or another confirmed Unity LauncherEntry-compatible dock | Primary supported environment | Running progress is visibly displayed |
| Fedora GNOME stock | Unsupported fallback | No visible progress is acceptable; no errors or timer regressions |
| KDE Plasma | Exploratory compatibility check | Record the actual result; do not claim support unless it is repeatable |
| Xfce X11 | Optional fallback check | No errors and normal in-window progress |

For every tested environment, record:

- distribution and version;
- desktop environment and version;
- `XDG_CURRENT_DESKTOP`;
- `XDG_SESSION_TYPE`;
- whether the app is running under X11, XWayland, or native Wayland;
- dock or panel name and version where applicable;
- backend selected;
- whether progress is visible;
- paused-state behavior;
- expired/urgent-state behavior;
- whether stopping clears progress;
- whether disabling **Show progress in taskbar** clears progress;
- whether application close clears stale progress;
- validation date.

## Manual Validation Steps

Launch Hourglass through the installed `.desktop` entry rather than only through `dotnet run`, because launcher identity affects whether a dock associates progress with the correct icon.

1. Start a duration timer and confirm progress changes.
2. Pause and confirm sensible degradation where pause state is unsupported.
3. Resume and confirm normal progress continues.
4. Expire the timer and inspect full progress plus urgency or attention behavior.
5. Stop the timer and confirm the progress indicator disappears.
6. Disable **Show progress in taskbar** during an active timer and confirm immediate clearing.
7. Re-enable **Show progress in taskbar** and confirm progress returns.
8. Test normal and reverse-progress modes.
9. Close Hourglass while progress is visible and confirm no stale indicator remains.
10. Restart Hourglass and confirm stale progress does not reappear.
11. Rapidly pause, resume, restart, and stop, confirming the final state is correct and no exceptions occur.
12. Monitor the session bus during testing so D-Bus emission can be verified independently of whether the dock displays it.

## Validated Combinations

No graphical desktop combinations have been validated in this repository snapshot yet. Do not mark an entire desktop environment as supported until an exact distribution, desktop, session type, dock, and validation date are recorded here.

Use exact tested combinations, for example:

- `Ubuntu GNOME + Ubuntu Dock: supported`
- `Fedora GNOME stock shell: unsupported/no-op`

## Completion Wording

Milestone 4.1 is implemented for the explicitly documented Unity LauncherEntry-compatible desktop and dock combinations. Unsupported or unverified desktops continue through the safe no-op backend until separately tested or given dedicated backends.
