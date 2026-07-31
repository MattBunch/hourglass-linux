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
| App version | `0.1.0` |
| Commit SHA | Native/AppDir evidence: `41be4a1240604076ade5ff864af576dc000890cd`; Flatpak failed-layout evidence: `a19e5f9088ae87069755d87209afaca0a7e9a3f5`; Flatpak fixed-layout evidence: PR branch `agent/fix-flatpak-manifest-layout`, installed Flatpak commit `f441f0251cc2a1d14319eff79f6e24c361926c3008f7c0348fd0e13666774346`. |
| CI/artifact source | Local publish: `scripts/publish-linux-release.sh --runtime linux-x64 --output /tmp/hourglass-linux-publish`; local AppDir: `packaging/appimage/build-appdir.sh /tmp/hourglass-linux-publish /tmp/hourglass-linux.AppDir`; failed-layout local Flatpak: `flatpak-builder --user --force-clean --install --install-deps-from=flathub /tmp/hourglass-flatpak-build /tmp/hourglass-flatpak-src.A3luNJ/packaging/flatpak/io.github.MattBunch.Hourglass.yml`; fixed-layout local Flatpak: `scripts/publish-linux-release.sh --runtime linux-x64 --output /tmp/hourglass-linux-publish`, then temp source copy `/tmp/hourglass-flatpak-src-pr.Q1d9xZ` and `flatpak-builder --user --force-clean --install --install-deps-from=flathub /tmp/hourglass-flatpak-build-pr /tmp/hourglass-flatpak-src-pr.Q1d9xZ/packaging/flatpak/io.github.MattBunch.Hourglass.yml`. |
| Package type | Native publish, AppDir prototype, and Flatpak prototype. |
| Tester | matt |
| Date | Native/AppDir evidence: 2026-07-27; Flatpak failed-layout evidence: 2026-07-29; Flatpak fixed-layout evidence: 2026-07-31. |
| Overall result | `Skipped` |
| Notes | Native publish and AppDir prototype were built locally and passed static package validation. Bounded desktop launches created `active-sessions.json` under isolated `XDG_CONFIG_HOME` paths. The initial Flatpak prototype built and installed locally, but failed before app startup because `/app/bin/hourglass-linux.dll` was missing from the installed sandbox. The fixed Flatpak manifest installed the complete publish output under `/app/lib/hourglass-linux`, installed a `/app/bin/hourglass-linux` launcher, granted the X11 socket required by the current Avalonia desktop backend, and reached a bounded desktop launch. Flathub `org.flatpak.Builder` provided `flatpak-builder-lint`; after removing unneeded Wayland and portal talk-name permissions, manifest lint still reported `appid-url-not-reachable` because `io.github.MattBunch.Hourglass` maps to `https://github.com/mattbunch/hourglass`, while the source repository is `hourglass-linux`. |
| Skip reason | Full attended GUI smoke coverage requires attended GUI operation in the target desktop session. |

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
| Fedora GNOME Wayland | Native publish | `Skipped` | Fedora Linux 44 Workstation, GNOME Shell 50.3, `XDG_SESSION_TYPE=wayland`, `WAYLAND_DISPLAY=wayland-0`, `XDG_CURRENT_DESKTOP=GNOME`. Artifact `/tmp/hourglass-linux-publish/hourglass-linux` started under a bounded desktop launch outside the Codex filesystem sandbox and created `/tmp/hourglass-linux-smoke-native-config/hourglass-linux/active-sessions.json` with default timer/session state. Bundled sounds were present under `/tmp/hourglass-linux-publish/Assets/Sounds/`. Static package validation passed. Full start, pause, resume, stop, restart, parsing, expiry, notification, sound, always-on-top, completion attention, session inhibition, desktop progress, status icon, clean shutdown, settings persistence, and single-instance handoff were not exercised end to end. GNOME stock taskbar progress and status icon support were not claimed. | Full attended GUI smoke coverage was not available in this validation run; Codex could only perform bounded launch and filesystem/artifact checks. |
| Fedora GNOME Wayland | AppImage | `Skipped` | No final `.AppImage` artifact exists in this repository output. AppDir prototype `/tmp/hourglass-linux.AppDir/AppRun` started under a bounded desktop launch outside the Codex filesystem sandbox and created `/tmp/hourglass-linux-smoke-appdir-config/hourglass-linux/active-sessions.json`. AppDir layout included `AppRun`, desktop metadata, AppStream metadata, bundled sounds, and root icon links (`hourglass.png`, `hourglass.svg`, `.DirIcon`). Static package validation passed. | Final `.AppImage` artifact not produced in this validation run; current packaging flow produces an AppDir prototype only. |
| Fedora GNOME X11 where available | Native publish | `Skipped` | No Fedora GNOME X11 session was available from the current desktop session. | Environment not available in this validation run. |
| Fedora GNOME X11 where available | AppImage | `Skipped` | No Fedora GNOME X11 session was available from the current desktop session, and no final `.AppImage` artifact was produced. | Environment not available in this validation run. |
| KDE Plasma Wayland | Native publish | `Skipped` | KDE Plasma Wayland was not available on this machine during the validation run. | Environment not available in this validation run. |
| KDE Plasma Wayland | AppImage | `Skipped` | KDE Plasma Wayland was not available on this machine during the validation run, and no final `.AppImage` artifact was produced. | Environment not available in this validation run. |
| KDE Plasma X11 where available | Native publish | `Skipped` | KDE Plasma X11 was not available on this machine during the validation run. | Environment not available in this validation run. |
| KDE Plasma X11 where available | AppImage | `Skipped` | KDE Plasma X11 was not available on this machine during the validation run, and no final `.AppImage` artifact was produced. | Environment not available in this validation run. |
| XFCE X11 | Native publish | `Skipped` | XFCE X11 was not available on this machine during the validation run. | Environment not available in this validation run. |
| XFCE X11 | AppImage | `Skipped` | XFCE X11 was not available on this machine during the validation run, and no final `.AppImage` artifact was produced. | Environment not available in this validation run. |
| Cinnamon or MATE X11 | Native publish | `Skipped` | Cinnamon or MATE X11 was not available on this machine during the validation run. | Environment not available in this validation run. |
| Cinnamon or MATE X11 | AppImage | `Skipped` | Cinnamon or MATE X11 was not available on this machine during the validation run, and no final `.AppImage` artifact was produced. | Environment not available in this validation run. |

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
| Fedora GNOME Wayland | Flatpak | `Skipped` | Fedora Linux 44 Workstation, GNOME Shell 50.3, `XDG_SESSION_TYPE=wayland`, `WAYLAND_DISPLAY=wayland-0`, `XDG_CURRENT_DESKTOP=GNOME`. Host tooling included Flatpak 1.18.0 and flatpak-builder 1.4.10. `org.freedesktop.Platform//24.08`, `org.freedesktop.Sdk//24.08`, and Flathub `org.flatpak.Builder` were installed. The fixed Flatpak manifest built and installed as user app `io.github.MattBunch.Hourglass`, version `0.1.0`, runtime `org.freedesktop.Platform/x86_64/24.08`, commit `f441f0251cc2a1d14319eff79f6e24c361926c3008f7c0348fd0e13666774346`. Effective permissions were X11, shared IPC, and notification talk access. `/app/bin/hourglass-linux` was an executable launcher, and the complete publish output was present under `/app/lib/hourglass-linux`, including `hourglass-linux.dll` and `Assets/Sounds/BeepNormal.wav`. A previous fixed-layout build with Wayland plus fallback X11 still failed with `XOpenDisplay failed`; changing the manifest to `--socket=x11` matched the current Avalonia X11 backend and allowed `timeout 12s flatpak run io.github.MattBunch.Hourglass` to stay up until timeout. The bounded launch wrote `/home/matt/.var/app/io.github.MattBunch.Hourglass/config/hourglass-linux/active-sessions.json`. `flatpak-builder-lint manifest` reported only `appid-url-not-reachable` after permission cleanup; this is a Flathub app-ID/source-repository naming issue, not a local startup failure. The log only contained Mesa DRI diagnostics. Core timer workflow, duration and absolute-time parsing, expiry notification, bundled sound playback, completion attention, session inhibition, single-instance handoff, and desktop integration behavior were not exercised end to end. | Full attended GUI smoke coverage was not available in this validation run; Codex could only perform bounded launch and filesystem/package checks. |
| Fedora GNOME X11 where available | Flatpak | `Skipped` | No Fedora GNOME X11 session was available from the current desktop session. | Environment not available in this validation run. |
| KDE Plasma Wayland | Flatpak | `Skipped` | KDE Plasma Wayland was not available on this machine during the validation run. | Environment not available in this validation run. |
| KDE Plasma X11 where available | Flatpak | `Skipped` | KDE Plasma X11 was not available on this machine during the validation run. | Environment not available in this validation run. |
| XFCE X11 | Flatpak | `Skipped` | XFCE X11 was not available on this machine during the validation run. | Environment not available in this validation run. |
| Cinnamon or MATE X11 | Flatpak | `Skipped` | Cinnamon or MATE X11 was not available on this machine during the validation run. | Environment not available in this validation run. |

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
