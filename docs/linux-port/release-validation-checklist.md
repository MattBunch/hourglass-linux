# Release Validation Checklist

This document is the release-candidate validation record for Hourglass Linux. Use it with the distribution roadmap's [Milestone 0: Release Readiness Audit](../HOURGLASS_LINUX_DISTRIBUTION_ROADMAP.md#4-milestone-0--release-readiness-audit) before publishing public Linux artifacts.

Do not mark a desktop, package type, tray backend, dock backend, multi-monitor scenario, or wake-from-suspend path as supported until an exact validation row records the environment and observed behavior. Unknown or unavailable coverage remains `Not run` or `Skipped` with a reason.

## Result Values

| Result | Meaning |
| --- | --- |
| `Pass` | The case was run and matched the expected behavior. |
| `Fail` | The case was run and did not match the expected behavior. |
| `Unsupported` | The app detected or documented that the capability is not available in this environment, and normal timer behavior still worked. |
| `Skipped` | The case was intentionally not run for this release candidate; record the reason. |
| `Not run` | The case still needs validation and has no result yet. |

## Release Candidate Record

| Field | Value |
| --- | --- |
| App version | `Not run` |
| Commit SHA | `Not run` |
| CI/artifact source | `Not run` |
| Package type | `Not run` |
| Tester | `Not run` |
| Date | `Not run` |
| Overall result | `Not run` |
| Notes | No release candidate has been validated from this checklist yet. |
| Skip reason | Not applicable. |

## Environment Fields

Every validation row should identify:

- app version;
- commit SHA;
- CI/artifact source, such as a GitHub Actions run, release draft, or local publish command;
- package type, such as native publish, AppImage, or Flatpak;
- distribution and version;
- desktop environment and version;
- session type and display server, such as Wayland, X11, or XWayland;
- panel, dock, tray extension, desktop-progress backend, and status-icon backend where relevant;
- hardware notes, including monitor topology and wake-capable hardware when relevant;
- tester, date, result, notes, and skip reason.

## Native and AppImage Desktop Smoke Matrix

Run these rows from an installed launcher entry where possible so desktop identity, icons, taskbar progress, and status icons use the same path users will use.

| Environment | Package type | Result | Notes | Skip reason |
| --- | --- | --- | --- | --- |
| Fedora GNOME Wayland | Native publish | `Not run` | Record distro version, GNOME version, display server, dock/panel details, backend selection, and visible behavior. | Not applicable. |
| Fedora GNOME Wayland | AppImage | `Not run` | Record launcher identity, icon identity, bundled sounds, config/data paths, and single-instance handoff. | Not applicable. |
| Fedora GNOME X11 where available | Native publish | `Not run` | Record whether X11 is available on the target distro and whether activation/always-on-top behavior differs from Wayland. | Not applicable. |
| Fedora GNOME X11 where available | AppImage | `Not run` | Record AppImage launcher identity, icon identity, and single-instance behavior under X11. | Not applicable. |
| KDE Plasma Wayland | Native publish | `Not run` | Record Plasma version, panel/task manager details, status icon visibility, and taskbar progress behavior. | Not applicable. |
| KDE Plasma Wayland | AppImage | `Not run` | Record AppImage desktop integration, status icon visibility, and taskbar progress behavior. | Not applicable. |
| KDE Plasma X11 where available | Native publish | `Not run` | Record status icon, taskbar progress, attention, and always-on-top behavior. | Not applicable. |
| KDE Plasma X11 where available | AppImage | `Not run` | Record AppImage desktop integration, status icon, taskbar progress, and single-instance behavior. | Not applicable. |
| XFCE X11 | Native publish | `Not run` | Record panel, tray plugin, notification daemon, status icon visibility, and unsupported fallback behavior. | Not applicable. |
| XFCE X11 | AppImage | `Not run` | Record AppImage desktop integration, status icon, notification, audio, and config path behavior. | Not applicable. |
| Cinnamon or MATE X11 | Native publish | `Not run` | Record exact desktop, panel, tray/status notifier behavior, notifications, and taskbar progress behavior. | Not applicable. |
| Cinnamon or MATE X11 | AppImage | `Not run` | Record exact desktop, AppImage integration, status icon, notification, audio, and config path behavior. | Not applicable. |

### Core Smoke Cases

For each native/AppImage matrix row, validate:

- start, pause, resume, stop, and restart;
- duration parsing and absolute-time parsing;
- minimize and expiry;
- notification delivery;
- sound playback, including bundled sounds;
- always-on-top behavior;
- completion attention and visual completion state;
- session inhibition while a timer is running;
- clean shutdown through the window and status icon when available;
- settings persistence across restart.

### Desktop Integration Cases

Use the compatibility policy from [desktop progress compatibility](desktop-progress-compatibility.md) and [status icon compatibility](status-icon-compatibility.md).

- Taskbar/dock progress: record the backend, dock or panel, whether progress is visible, paused behavior, expired/urgent behavior, clearing on stop, and clearing on close.
- Status icon: record the backend, panel/tray/extension, icon visibility, menu state, pause/resume, stop, restart, exit, and whether disabling the option removes the icon.
- Tray recovery: test hide/show recovery only when the selected backend reports that hidden windows are recoverable. Otherwise record `Unsupported`, not `Fail`.

### Native/AppImage Package Cases

For each native/AppImage matrix row, validate:

- desktop file launch;
- application ID and icon identity;
- bundled sounds;
- config and data paths;
- single-instance handoff for show/restore and timer-start command lines.

## Flatpak Sandbox Smoke Matrix

Run Flatpak rows from the installed Flatpak application, not from an unpackaged build.

| Environment | Package type | Result | Notes | Skip reason |
| --- | --- | --- | --- | --- |
| Fedora GNOME Wayland | Flatpak | `Not run` | Record sandboxed settings paths, notifications, audio behavior, portals/session inhibition, and single-instance behavior. | Not applicable. |
| Fedora GNOME X11 where available | Flatpak | `Not run` | Record whether X11 is available and whether sandboxed notifications, audio, and single-instance behavior differ from Wayland. | Not applicable. |
| KDE Plasma Wayland | Flatpak | `Not run` | Record portal behavior, notifications, audio, status icon behavior, and taskbar progress behavior. | Not applicable. |
| KDE Plasma X11 where available | Flatpak | `Not run` | Record portal behavior, notifications, audio, status icon behavior, and taskbar progress behavior. | Not applicable. |
| XFCE X11 | Flatpak | `Not run` | Record notification daemon, audio behavior, status icon support, and unsupported fallback behavior. | Not applicable. |
| Cinnamon or MATE X11 | Flatpak | `Not run` | Record exact desktop, panel/tray support, notifications, audio, and sandboxed settings paths. | Not applicable. |

For each Flatpak row, validate the core smoke cases plus:

- sandboxed settings and data paths;
- notification permissions and delivery;
- audio playback inside the sandbox;
- portal or session-inhibition behavior;
- single-instance behavior inside the sandbox namespace;
- desktop file, AppStream, and icon identity after Flatpak installation.

## Multi-Monitor Placement Validation

| Case | Result | Notes | Skip reason |
| --- | --- | --- | --- |
| Mixed scaling across monitors | `Not run` | Record display server, monitor sizes, scale factors, and whether saved geometry restores to the intended work area. | Not applicable. |
| Monitor disconnect while timers are open | `Not run` | Record whether windows remain reachable and whether follow-up placement saves avoid off-screen geometry. | Not applicable. |
| Restored placement after topology change | `Not run` | Record the saved bounds, new monitor layout, and restored bounds. | Not applicable. |
| Full-screen on secondary display | `Not run` | Record display server, secondary monitor, entry/exit behavior, and restored normal bounds. | Not applicable. |
| Off-screen recovery | `Not run` | Record the invalid saved placement and the recovered visible placement. | Not applicable. |

## Physical Wake-From-Suspend Validation

Wake from suspend remains disabled by default. Do not expose or recommend it for normal users until physical hardware, permissions, and packaging behavior are proven.

| Case | Package type | Result | Notes | Skip reason |
| --- | --- | --- | --- | --- |
| Capability and permission discovery | Native publish | `Not run` | Record hardware, kernel, RTC wakealarm path, user permissions, and selected backend. | Not applicable. |
| Schedule and cancel running timer wake alarm | Native publish | `Not run` | Record scheduling, replacement, cancellation, and countdown behavior when the alarm is not needed. | Not applicable. |
| Resume for expired timer | Native publish | `Not run` | Record suspend method, scheduled wake time, actual resume time, notification, sound, and completion state. | Not applicable. |
| Unsupported or permission-denied path | Native publish | `Not run` | Record exact failure or unsupported reason and confirm timer behavior continues. | Not applicable. |
| Capability and permission discovery | AppImage | `Not run` | Record whether AppImage execution changes permissions or backend selection. | Not applicable. |
| Schedule, cancel, and resume behavior | AppImage | `Not run` | Record schedule/cancel behavior and actual resume behavior. | Not applicable. |
| Sandbox capability behavior | Flatpak | `Not run` | Record portal availability, denied permissions, unsupported reason, and normal timer behavior. | Not applicable. |

## Distribution Milestone 0 Readiness

Use this section as the execution evidence for the distribution roadmap's Release Readiness Audit.

| Case | Result | Notes | Skip reason |
| --- | --- | --- | --- |
| Clean self-contained publish launch | `Not run` | Record publish command, artifact path, commit SHA, version, launch command, and whether no development paths are required. | Not applicable. |
| Metadata consistency | `Not run` | Confirm application ID, desktop filename, AppStream ID, Flatpak ID, `Exec=hourglass-linux`, binary name, icon names, version, release date, homepage, repository, issue tracker, and description. | Not applicable. |
| Legal and attribution | `Not run` | Confirm MIT license, original Hourglass attribution, bundled sound redistribution, icon redistribution, and whether third-party notices are required. | Not applicable. |
| Dependency notices | `Not run` | Record dependency report source and any unresolved notice obligations. | Not applicable. |
| Validators | `Not run` | Record desktop-file, AppStream, package, build, test, format, and checksum validation commands and results. | Not applicable. |
| First public version readiness | `Not run` | Record intended version, tag, artifact source, known limitations, and release-blocking failures or deferrals. | Not applicable. |

## Compatibility Policy

Taskbar/dock progress and status-icon behavior are capability-based and desktop-dependent. Reuse the dedicated compatibility documents before making support claims:

- [Desktop Progress Compatibility](desktop-progress-compatibility.md)
- [Status Icon Compatibility](status-icon-compatibility.md)

Before claiming support, record the exact distro, desktop environment, session type, display server, panel/dock/extension, selected backend, package type, app version, commit SHA, tester, date, and visible behavior. A backend reporting support is not enough by itself; visible desktop behavior must be observed in the target environment.
