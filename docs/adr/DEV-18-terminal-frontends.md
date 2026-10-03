# ADR DEV-18: three frontends over one session runtime

Status: accepted architectural direction; initial toolkit feasibility verified.

## Context

DEV-18 expands the native Avalonia application with CLI and TUI. Parsing and immutable countdown transitions already exist in Core. Most orchestration currently lives in MainWindowViewModel and TimerWindowCoordinator. Reimplementing timer behavior in terminal projects would create incompatible products and unsafe concurrent persistence.

## Decision

Keep C# and .NET 10. Add Application over Core/Platform. Each logical TimerSession owns exactly one engine. Frontends issue typed commands and observe immutable snapshots. Linux.Services implements capability and control infrastructure. Avalonia retains presentation, window and desktop integration. Preserve normal hourglass-linux and timer-expression launch behavior.

Use System.CommandLine 2.0.0 and Terminal.Gui 2.5.0 as exact spike candidates. Pin validated versions; isolate Terminal.Gui in TUI (and the temporary isolated spike). Do not add Spectre.Console, alternate language runtimes, Native AOT or a system daemon.

The final CLI defaults to foreground waiting through the authoritative runtime. The final TUI connects to GUI or an on-demand Host. This user-approved adjustment to the report avoids engine handoff when terminal frontends exit. GUI runtime stays alive for explicitly detached timers after window close. Existing non-detached GUI behavior remains compatible.

Retain XDG location and schemas during extraction. Serialize mutations under one runtime authority. Keep legacy launcher bridge and add separate versioned bounded request/response IPC. Separate GUI singleton from runtime ownership.

Initial terminal distribution is self-contained x64/arm64 tarballs. Preserve existing GUI artifacts. Native packages, man pages and completions are later evaluations.

## Consequences

Application gains explicit scheduling, session registry, persistence and effect lifecycle. Existing Core transitions are reused. GUI migration proceeds in small tested units. Multi-process mode requires IPC before it is released; interim CLI/TUI MVPs must acquire exclusive authority. No frontend may independently mutate shared files while another runtime owns them.

## Spike acceptance

Prove parser invocation/output/cancellation and terminal startup/shutdown, 4/5/10 Hz updates, resize, Space/Escape/Ctrl/Alt/Tab/Shift+Tab input, normal and exceptional terminal restoration. Record exact versions and environment. PTY evidence does not prove physical desktop/hardware or all terminal emulators.

Results (2026-09-26): System.CommandLine 2.0.0 and Terminal.Gui 2.5.0 pass the reproducible parser and Linux PTY checks in tools/Hourglass.TerminalSpike. Checks cover 4/5/10 Hz updates, keys, resize, text-entry routing, normal/exception exits and independent terminal restoration assertions. Alt+Enter's legacy Escape+CR encoding arrives as Ctrl+Alt+M; normalize within the relevant TUI action context and preserve ordinary Enter. Attended emulator/SSH/accessibility coverage remains Not run. See the spike README for commands and limits.

See [implementation plan](../DEV-18_IMPLEMENTATION_PLAN.md) and [research](../research/DEV-18-deep-research-report.md).

## Stage G lifetime decision

Until Stage H supplies the host, runtime ownership remains with the process that acquired the existing authority lock. Owner exit drains active responses and saves recovery state, then disconnects attached clients. Frontends do not transfer engines or replay uncertain commands. GUI presentation uses a separate registration lock/socket; an attached GUI closes only its own logical sessions.

## Stage H lifetime implementation

Terminal frontends attach to GUI authority or bootstrap an on-demand internal Host.
They do not acquire production engine ownership. GUI-first authority remains valid
and stays alive without windows when retained sessions or terminal clients need it.
There is no engine transfer or system daemon.

A typed session lifetime and authority-assigned client lease separate timer state
from frontend lifetime. Detached sessions remain retained even when stopped or
expired. TUI connection loss removes only its ordinary sessions. GUI connection
loss preserves ordinary sessions as deferred recovery records. Terminal startup
restores only eligible detached records and preserves GUI records until shared GUI
initialization; this follows the user's Stage H recovery choices. Existing restore
preferences, schemas, XDG paths and GUI startup actions remain compatible.

Protocol 2 explicitly registers client kinds and lifetime operations. Version 1
clients receive a compatibility error; lifetime behavior is not silently downgraded.
The host's three-second idle grace is followed by an atomic runtime draining
transition and bounded cleanup. Detached acknowledgement includes persistence;
failed durability remains an error with inspectable live IDs.
