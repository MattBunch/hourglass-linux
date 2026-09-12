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
| Commit SHA | DEV-5 audit: `cd1646f7900c7e03c99e66df301ebadf46e20473`; Native/AppDir evidence: `41be4a1240604076ade5ff864af576dc000890cd`; Flatpak failed-layout evidence: `a19e5f9088ae87069755d87209afaca0a7e9a3f5`; Flatpak fixed-layout source Git commit: `65ff66f10c5b170822ed3c8d8df578ab809c6141`; installed Flatpak/OSTree commit: `f441f0251cc2a1d14319eff79f6e24c361926c3008f7c0348fd0e13666774346`. |
| CI/artifact source | DEV-5 local publish: `scripts/publish-linux-release.sh --runtime linux-x64 --output /tmp/hourglass-linux-dev5-publish`; DEV-5 AppDir: `packaging/appimage/build-appdir.sh /tmp/hourglass-linux-dev5-publish /tmp/hourglass-linux-dev5.AppDir`; DEV-5 validation: `HOURGLASS_REQUIRE_PACKAGE_VALIDATORS=true scripts/validate-linux-packaging.sh /tmp/hourglass-linux-dev5-publish /tmp/hourglass-linux-dev5.AppDir`; previous evidence retained below. |
| Package type | Native publish, AppDir prototype, and Flatpak prototype. |
| Tester | matt |
| Date | DEV-5 audit: 2026-08-12; Native/AppDir evidence: 2026-07-27; Flatpak failed-layout evidence: 2026-07-29; Flatpak fixed-layout evidence: 2026-07-31. |
| Overall result | `Fail` |
| Notes | DEV-5 restore, warning-free Release build, 430 Release tests, formatting verification, clean self-contained publish, AppDir build, and required desktop/AppStream validators passed. The publish embeds its source revision and includes all three bundled sounds. The publish artifact still includes portable PDB/debug information containing `/home/matt/Projects/hourglass-linux` source paths, and it does not include the project license or required .NET/native third-party notice bundle. These are public-release blockers. The earlier Native/AppDir and Flatpak evidence remains below. |
| Skip reason | Full attended GUI smoke coverage requires attended GUI operation in the target desktop session. |

## Public Beta Validation Record — `v0.2.0-beta.2`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.2` |
| Tag and commit | `v0.2.0-beta.2` at `b3d2b18417af999d60205f23cf82cdafa3abaf83` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/32711569392); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.2) containing `hourglass-linux-0.2.0-beta.2-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Tester and date | matt, 2026-08-24 |
| Overall result | `Fail` |
| Evidence | `sha256sum --check SHA256SUMS` passed. The process stayed alive, but no window appeared. Its captured output reported `libEGL warning: DRI3 error: Could not get DRI3 device`; the earlier GLX attempt reported that `llvmpipe` is blacklisted. The application did not reach functional timer validation. |
| Follow-up | DEV-15 remains in progress. Subsequent beta validation evidence is recorded below. DEV-14 cannot be completed until a beta visibly launches and passes the full Ubuntu validation. |

## Public Beta Validation Record — `v0.2.0-beta.3`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.3` |
| Tag and commit | `v0.2.0-beta.3` at `ae8c54f3c02b392af57d8134e819abb9e87e919c` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/32843560548); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.3) containing `hourglass-linux-0.2.0-beta.3-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Tester and date | matt, 2026-08-26 |
| Overall result | `Fail` |
| Evidence | `sha256sum --check SHA256SUMS` passed. `./hourglass-linux` exited with code `1` before showing a window and reported `X connection to :0 broken (explicit kill or server shutdown)`. No Hourglass process remained and no user-journal entry was recorded. |
| Follow-up | DEV-15 remains in progress. `v0.2.0-beta.4` replaced the probe but still did not create a visible window. `v0.2.0-beta.5` must select software rendering without native DRI3 probing, then repeat the complete Ubuntu validation before DEV-14 can be completed. |

## Public Beta Validation Record — `v0.2.0-beta.5`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.5` |
| Tag and commit | `v0.2.0-beta.5` at `5cf546d8b01351bc4a111bd35bd96bb736227102` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/33498793106); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.5) containing `hourglass-linux-0.2.0-beta.5-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Tester and date | matt, 2026-09-04 |
| Overall result | `Fail` |
| Evidence | `sha256sum --check SHA256SUMS` passed. The process remained alive for more than two minutes, but no Hourglass X11 window appeared and captured stderr was empty. |
| Follow-up | DEV-15 remains in progress. `v0.2.0-beta.6` showed that startup stops after `ClassicDesktopLifetimeConfigured`, before `CoordinatorCreated`; the process remained alive and waited on `futex_do_wait`. `v0.2.0-beta.7` adds paired per-service construction markers to identify the precise blocker without changing rendering. |

## Public Beta Validation Record — `v0.2.0-beta.6`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.6` |
| Tag and commit | `v0.2.0-beta.6` at `f8a2bf8f692d002f71125aa003fc3a11d2d9ffeb` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/33853974234); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.6) containing `hourglass-linux-0.2.0-beta.6-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Tester and date | matt, 2026-09-04 |
| Overall result | `Fail` |
| Evidence | `sha256sum --check SHA256SUMS` passed. With `HOURGLASS_STARTUP_DIAGNOSTICS=1`, startup reached `ClassicDesktopLifetimeConfigured` but never reached `CoordinatorCreated`; the process remained alive after more than three minutes and `/proc/<pid>/wchan` reported `futex_do_wait`. |
| Follow-up | DEV-15 remains in progress. `v0.2.0-beta.7` adds paired service-construction diagnostics to identify the exact blocking dependency before another behavior change is attempted. |

## Startup Diagnostics

Run a diagnostic beta from the extracted archive with:

```bash
HOURGLASS_STARTUP_DIAGNOSTICS=1 \
  ./hourglass-linux > hourglass-startup.log 2>&1 &
```

The log records startup milestones and display-session context. It is evidence
for investigating a failed launch, not evidence that a visible window launched.

## Public Beta Validation Record — `v0.2.0-beta.7`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.7` |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Overall result | `Fail` |
| Evidence | Checksum verification passed. With `HOURGLASS_STARTUP_DIAGNOSTICS=1`, startup reached `service=DesktopProgressService state=Starting` but not its corresponding completion marker or `CoordinatorCreated`. This isolated synchronous Unity LauncherEntry D-Bus connection and owner-watcher setup as the startup blocker. |
| Follow-up | DEV-15 remains in progress. `v0.2.0-beta.8` defers that discovery until the first post-window desktop-progress request on a background task. |

## Public Beta Validation Record — `v0.2.0-beta.8`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.8` |
| Tag and commit | `v0.2.0-beta.8` at `b3c2429583e4da87f55193a1b9229e0f92660b86`. |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/33868411835); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.8) containing `hourglass-linux-0.2.0-beta.8-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly in the VM. |
| Environment | Installed Ubuntu 24.04.4 GNOME Shell 46.0 Wayland VM with Xwayland; `DISPLAY=:0`, `WAYLAND_DISPLAY=wayland-0`, and no separately installed .NET runtime. |
| Tester and date | matt, 2026-09-07. |
| Overall result | `Pass` |
| Evidence | `sha256sum --check SHA256SUMS` passed. With `HOURGLASS_STARTUP_DIAGNOSTICS=1`, the released application visibly opened and recorded `CoordinatorCreated`, `MainWindowOpened`, and `MainWindowActivated`; all desktop-progress service construction markers completed. The launched process remained running. Manual timer validation passed for a 10-second timer: start, pause, resume, and expiry. |
| Deferred coverage | Dock/taskbar progress visibility remains a separate desktop-integration validation concern. DEV-14 retains its broader Ubuntu release-validation checks. |

## Public Beta Validation Record — `v0.2.0-beta.4`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.4` |
| Tag and commit | `v0.2.0-beta.4` at `32dfbbf0ae1a9d7320d1b4b42ff82a9e2029306b` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/32963639202); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.4) containing `hourglass-linux-0.2.0-beta.4-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Installed Ubuntu 24.04.4 GNOME Wayland VM with Xwayland; no separately installed .NET runtime. |
| Tester and date | matt, 2026-09-01 |
| Overall result | `Fail` |
| Evidence | `sha256sum --check SHA256SUMS` passed. The process remained alive, but no Hourglass X11 window was present after five seconds. It created neither a settings directory nor diagnostic output. |
| Follow-up | DEV-15 remains in progress. Remove native DRI3 probing and select software rendering directly for the detected Wayland session in `v0.2.0-beta.5`, then repeat Ubuntu validation before DEV-14 can be completed. |

## Public Beta Validation Record — `v0.2.0-beta.1`

| Field | Value |
| --- | --- |
| App version | `0.2.0-beta.1` |
| Tag and commit | `v0.2.0-beta.1` at `26f2fd54a540a4ff39b2e8a05374edcc3c8e1340` |
| CI/artifact source | [Successful release workflow](https://github.com/MattBunch/hourglass-linux/actions/runs/31942848612); [GitHub prerelease](https://github.com/MattBunch/hourglass-linux/releases/tag/v0.2.0-beta.1) containing `hourglass-linux-0.2.0-beta.1-linux-x64.tar.gz` and `SHA256SUMS`. |
| Package type | Self-contained `linux-x64` tarball, extracted directly from a path containing whitespace. |
| Environment | Clean Fedora 44 Workstation GNOME VM; no separately installed .NET runtime. |
| Tester and date | matt, 2026-08-23 |
| Overall result | `Pass` |
| Evidence | `sha256sum --check SHA256SUMS` passed. The tarball extracted and `./hourglass-linux` launched. Manual start, pause, resume, stop, expiry, notification, bundled sound, and settings-persistence checks passed. GNOME displayed its generic notification fallback icon because the raw archive does not install a desktop entry or icon and the current `notify-send` invocation supplies no explicit icon; notification delivery passed. |
| Deferred coverage | Ubuntu artifact validation is tracked by DEV-14. Multi-monitor and wake-from-suspend validation remain `Not run`; this VM result does not claim either capability. |

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
| Ubuntu GNOME Wayland | AppImage | `Not run` | `v0.2.0-beta.9` CI will provide a checksummed final AppImage for an attended clean-VM validation from a path containing whitespace. Record Ubuntu and GNOME versions, Wayland/Xwayland details, no separately installed .NET, timer workflow, notification, sound, persistence, and single-instance evidence. | Awaiting release-candidate CI artifact and attended validation. |
| Fedora GNOME Wayland | AppImage | `Not run` | `v0.2.0-beta.9` CI will provide a checksummed final AppImage for an attended clean-VM validation from a path containing whitespace. Record Fedora and GNOME versions, Wayland details, no separately installed .NET, timer workflow, notification, sound, persistence, and single-instance evidence. | Awaiting release-candidate CI artifact and attended validation. |
| Arch Linux GNOME Wayland | AppImage | `Skipped` | Attended validation by matt on 2026-09-12 in an Arch Linux rolling QEMU/KVM VM (UEFI; 2 vCPUs; 4 GiB RAM; single VirtIO/SPICE display). App `v0.2.0-beta.9`, commit `3e2661c76e41b6c07c0c22f912cb7f56996caecd`, was downloaded from the published GitHub release into `/home/test/Hourglass beta9`; `sha256sum --check --ignore-missing SHA256SUMS` passed for `Hourglass-0.2.0-beta.9-x86_64.AppImage`. GNOME Shell 50.4; `gnome-shell 1:50.4-1`, `xorg-xwayland 24.1.13-1`, and `fuse2 2.9.9-6`; `XDG_SESSION_TYPE=wayland`, `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0`, session Type=wayland. Diagnostics reached `CoordinatorCreated`, `MainWindowOpened`, and `MainWindowActivated`; the AppImage process remained running and its window was visible. Attended start, pause, resume, expiry, notification, bundled sound, stop/restart, duration parsing, absolute-time parsing, minimize via `Alt+F9`, and settings-persistence checks passed. | Aggregate AppImage coverage remains incomplete: always-on-top, completion attention, session inhibition, clean shutdown, single-instance handoff, desktop-file launch, application identity, stock GNOME dock/taskbar progress, status-icon integration, and separately installed .NET state were not recorded. |
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

**Status:** `Not run`. Deferred until an attended physical two-monitor setup is available; single-display or VM-only checks do not provide completion evidence for these topology-dependent cases.

| Case | Result | Notes | Skip reason |
| --- | --- | --- | --- |
| Mixed scaling across monitors | `Not run` | Record display server, monitor sizes, scale factors, and whether saved geometry restores to the intended work area. | Not applicable. |
| Monitor disconnect while timers are open | `Not run` | Record whether windows remain reachable and whether follow-up placement saves avoid off-screen geometry. | Not applicable. |
| Restored placement after topology change | `Not run` | Record the saved bounds, new monitor layout, and restored bounds. | Not applicable. |
| Full-screen on secondary display | `Not run` | Record display server, secondary monitor, entry/exit behavior, and restored normal bounds. | Not applicable. |
| Off-screen recovery | `Not run` | Record the invalid saved placement and the recovered visible placement. | Not applicable. |

## Physical Wake-From-Suspend Validation

Wake from suspend remains disabled by default. Do not expose or recommend it for normal users until physical hardware, permissions, and packaging behavior are proven.

**2026-08-07 discovery result:** `Skipped`. The Codex execution environment reported `container-other`; it exposed `/sys/class/rtc/rtc0/wakealarm` with no current alarm and an enabled wakeup device, but a container cannot prove host suspend/resume behavior. No wake alarm was scheduled or cleared. The rows below remain `Not run` pending physical-hardware validation.

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
| Clean self-contained publish launch | `Fail` | `scripts/publish-linux-release.sh --runtime linux-x64 --output /tmp/hourglass-linux-dev5-publish` produced self-contained `linux-x64` version `0.1.0` at `cd1646f7900c7e03c99e66df301ebadf46e20473`; source-revision metadata was present and bundled sounds were present. A bounded GNOME Wayland launch could not acquire its normal runtime lock in the Codex sandbox, and a temporary runtime directory reached an expected sandbox `XOpenDisplay failed` error. The artifact scan found absolute development paths in portable PDB/debug information. | An attended desktop launch and a release artifact without development paths require a target desktop session and release-pipeline debug-symbol policy. |
| Metadata consistency | `Pass` | Project, desktop entry, AppStream metadata, and Flatpak manifest consistently use `io.github.MattBunch.Hourglass`, `hourglass-linux`, version `0.1.0`, homepage, repository, bug tracker, and current Linux-timer description. `desktop-file-validate` and `appstreamcli validate --no-net` passed. The AppStream release date is the prototype date `2026-07-18`; it must be updated when a public `0.1.0` tag is prepared. | Not applicable. |
| Legal and attribution | `Fail` | Root `LICENSE.md` contains the upstream MIT license; README and About UI retain original Hourglass attribution; bundled sounds match upstream MIT-covered resources; icon sources are repository-authored. See `docs/THIRD_PARTY_NOTICES.md` for the runtime review. The publish artifact does not include the project license or third-party notices. | Public artifact notice bundling has not been implemented. |
| Dependency notices | `Fail` | `dotnet package list --project src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj --include-transitive --format json --no-restore` and NuGet package metadata were reviewed. `dotnet package list --project src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj --vulnerable --include-transitive --no-restore` reported no vulnerable packages. .NET runtime and native asset packages supply verbatim notices that must accompany a public artifact. | The current publish/AppDir flow omits those notice files. |
| Validators | `Pass` | Passed: `dotnet restore Hourglass.Linux.sln`; `dotnet build Hourglass.Linux.sln --configuration Release --no-restore -warnaserror`; `dotnet test Hourglass.Linux.sln --configuration Release --no-build --verbosity normal` (430 tests); `dotnet format Hourglass.Linux.sln --verify-no-changes --no-restore --verbosity minimal`; clean publish, AppDir build, and `HOURGLASS_REQUIRE_PACKAGE_VALIDATORS=true scripts/validate-linux-packaging.sh /tmp/hourglass-linux-dev5-publish /tmp/hourglass-linux-dev5.AppDir`. | Not applicable. |
| First public version readiness | `Fail` | Intended first public version remains `0.1.0`; no public tag, release workflow, checksums, final AppImage, or production Flatpak/Flathub submission exists. Release blockers are development paths in artifact debug information, missing bundled license/notice files, and unrun attended desktop, multi-monitor, and physical wake validation. | These items require Milestones 1–5 and target-environment validation. |

## Compatibility Policy

Taskbar/dock progress and status-icon behavior are capability-based and desktop-dependent. Reuse the dedicated compatibility documents before making support claims:

- [Desktop Progress Compatibility](desktop-progress-compatibility.md)
- [Status Icon Compatibility](status-icon-compatibility.md)

Before claiming support, record the exact distro, desktop environment, session type, display server, panel/dock/extension, selected backend, package type, app version, commit SHA, tester, date, and visible behavior. A backend reporting support is not enough by itself; visible desktop behavior must be observed in the target environment.
