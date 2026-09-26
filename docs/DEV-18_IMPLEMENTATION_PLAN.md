# DEV-18: shared application runtime, CLI and TUI

## Specification and baseline

Sources: [DEV-18](https://linear.app/matt-bunch/issue/DEV-18/add-first-class-cli-and-full-tui-to-hourglass-linux), the full [research report](research/DEV-18-deep-research-report.md), and the user's implementation requirements and accepted plan. Explicit user requirements take precedence over research recommendations. This is an additive expansion: Avalonia remains a fully supported frontend.

Baseline: `ef001db81f745f5e7b8c769e0fd4a33d18d50a59` on `develop`. The clean local worktree and live remote develop ref matched before branching. Feature branch: `mattbunch/dev-18-add-first-class-cli-and-full-tui-to-hourglass-linux`.

Confirmed decisions:

- `hourglass start` defaults to foreground wait; `--detach` explicitly requests background lifetime.
- In the final product, foreground commands wait through the authoritative GUI/host runtime. This deliberately adjusts the report's literal CLI-process engine ownership recommendation.
- The final TUI attaches to the GUI runtime, otherwise starts/connects to the on-demand headless host.
- A GUI-owned runtime with detached timers stays alive after GUI windows close. Do not transfer live engines.
- Initial terminal distribution is self-contained linux-x64/linux-arm64 tarballs. Evaluate deb/RPM, man pages, completions, musl and .NET tools later. No Native AOT.
- Preserve `hourglass-linux`, `hourglass-linux 25m`, and `hourglass-linux -t "Tea" 5m`. The desktop entry remains `Terminal=false` and launches `hourglass-linux`.

## Current architecture

Project references: Platform -> Core; Linux.Services -> Platform/Core; Avalonia -> Services/Platform/Core. The solution also includes DemoRecorder and ReleaseTool and their tests. All modern projects use net10.0. global.json requests SDK 10.0.108 with feature roll-forward. Package versions are project-local; GUI uses Avalonia 12.0.4 and owns application version 0.2.0.

Startup: `Program.Main/Run` parses through `LinuxCommandLineParser`, acquires `LinuxFileLockSingleInstanceService`, forwards secondary launches, starts the request listener, then starts the Avalonia classic desktop lifetime. `App` constructs `TimerWindowCoordinator`, loads/restores settings/sessions and registers the queued launch dispatcher. Coordinator startup restores valid sessions, optionally opens saved timers if none restored, applies an explicit timer launch, or creates an empty window.

Each `CreateWindow` currently constructs a `CountdownEngine` and `MainWindowViewModel`. A 250 ms `MainWindow` DispatcherTimer calls view-model Tick, advancing the engine. MainWindowViewModel owns parsing/start, pause/resume/reset/restart, input editing, title/options/lock, expiry, audio/inhibition leases, settings, recents, saved timers, themes and persistence as well as bindings. TimerWindowCoordinator owns windows, IDs, restoration, shared saves/services, wake/inhibition coordination, progress/tray aggregation, secondary-launch routing and shutdown. Active-session saves enumerate windows; closing the last window shuts down the process.

Existing stores: app.json (including recent inputs), saved-timers.json, active-sessions.json, legacy active-session.json and custom-themes.json. XDG resolution uses an absolute XDG_CONFIG_HOME, then ~/.config/hourglass-linux, then absolute platform application data. JSON uses a unique same-directory temporary file and replacement; malformed/missing documents recover through defaults and diagnostics. CoordinatedAppSettingsStore and CoordinatedSavedTimersStore serialize in-process changes, not cross-process writes. Preserve missing-versus-empty active-session fallback and startup save ordering.

Existing IPC: per-user one-byte advisory file lock held by an open handle; lock files are not deleted. Paths use XDG_RUNTIME_DIR, XDG_CACHE_HOME, then ~/.cache under hourglass-linux. The owner removes stale sockets after acquiring the lock. Legacy messages are four-byte big-endian length-prefixed JSON, limited to 8 KiB, with a two-second timeout and four concurrent handlers. Activate/StartTimer are a launcher protocol, not application command contracts.

## Verified differences from research

1. Core already has immutable CountdownState, CountdownTransitions and persistence snapshots; do not reimplement them.
2. LinuxSettingsSnapshot, ApplicationPreferences, TimerDefaults and LinuxSettingsMerger already provide focused settings models.
3. IUiDispatcher is internal to Avalonia and its implementation uses Avalonia.Threading; it is not a ready shared runtime dispatcher.
4. Atomic writes do not prevent lost updates between processes; existing gates are process-local.
5. Legacy IPC already supplies useful framing/admission/cleanup mechanisms.
6. Several coordinator tests inspect source strings. Replace moved assertions with behavioral coverage, not weakened text checks.
7. Sound assets currently publish from the GUI project. Host/terminal artifacts need the same assets explicitly.
8. ReleaseTool reads evaluated GUI Version. Preserve that interface when centralizing the version.
9. Some architecture prose predates implemented secondary-launch behavior.
10. The full downloaded report includes sections omitted from the integration response; the full file is retained under docs/research.

## Target boundaries and public application surface

References: Core -> no frontend/infrastructure; Platform -> Core; Application -> Core/Platform; Linux.Services -> Core/Platform/Application infrastructure contracts; Avalonia -> Application/Platform/Services; CLI -> Application/Services/System.CommandLine; TUI -> Application/Services/Terminal.Gui; Host -> Application/Services. Application must never reference Services. Architecture guards check project and transitive assembly/package boundaries.

Exactly one TimerSession owns one CountdownEngine. Frontends issue commands and observe immutable snapshots; they never advance engines. Keep parser, CountdownEngine/State/Transitions, restoration calculations, settings merge functions and serialization models in Core.

| Type | API, responsibility and ownership |
| --- | --- |
| TimerSession | Internal engine owner; committed expression/title/options; lifecycle revision and effect ownership. Operations are invoked by the serialized runtime. |
| TimerSessionSnapshot | Immutable ID/revision/state/input/title/timing/progress/options/allowed actions. No toolkit objects or mutable DTOs. |
| TimerSessionManager | Owns create/find/list/restore/remove registry and stable IDs. |
| IHourglassClient | Async typed command/query/snapshot contract, cancellation tokens and typed failure results; local and remote adapters. |
| HourglassRuntime | Initialization, serialized commands/ticks, subscriptions, platform effects, persistence draining and async disposal. |
| Settings/SavedTimer/RecentInput services | Existing merge/normalization logic behind typed operations and immutable results. |
| ActiveSessionRepository | Existing formats, fallback, ordered saves and restoration mappings. |
| TimerExpiryCoordinator | Notification/audio/inhibition/loop/close policy and presentation-neutral completion signals. |
| Control contracts | Versioned envelopes and capability/snapshot/error results; Linux transport implements them. |

The runtime owns one serialized mutation queue and a frontend-independent tick scheduler. Inject IMonotonicClock and wall-clock delegates. GUI/TUI marshal snapshots to their own dispatcher. Slow effects run outside the mutation queue; completions carry session ID/revision so stale work cannot affect restarted/removed sessions. Await/dispose audio/inhibition leases, subscriptions, cancellation sources and pending saves. Application tests use fake clocks and recording services without real delays.

Move coordinated settings/saved stores, shared custom-theme writes, wake scheduling and CoordinatedSessionInhibitor into Application. Keep genuine windows/activation/geometry/fullscreen/tray/taskbar projection, RelayCommand, bindings, visual feedback and editor drafts in Avalonia. Linux notification/audio/inhibit/wake implementations stay in Services. Do not introduce repository-wide style cleanup or rewrite legacy Windows projects.

## Behavior and persistence contracts

Editing does not pause or replace a running timer. Drafts are frontend-local; applying an edit uses an expected revision and returns conflict if stale. Stop matches existing reset (clear engine/effects, preserve input/title). Restart preserves engine eligibility, including rejection for absolute timers. Dismiss removes stopped/expired sessions. Application enforces lock rules and retains an explicit unlock path. All-target operations prevalidate the selected set before committing, avoiding silent partial mutations.

Initial extraction preserves file schemas, defaults, version behavior, corrupted-document recovery, settings merges and restoration precedence. Geometry stays opaque presentation metadata. Only the authority writes shared data. Without a runtime, a one-shot config operation may acquire the authority lock and transact without restoring/starting timers. Explicit CLI save failures return errors; background/GUI failures remain visible diagnostics. Later optional lifetime metadata distinguishes detached from legacy GUI-owned sessions; missing metadata retains legacy behavior. This is not a timer-schema redesign.

## Runtime authority and compatibility

Reuse the existing runtime lock namespace and retain legacy Activate/StartTimer as a bridge. Add a versioned control socket and separate GUI presentation singleton registration. GUI attaching to a host creates adapters, never engines. Host launches/activates hourglass-linux through process infrastructure without loading Avalonia. GUI runtime survives window close if detached sessions require it. Final CLI/TUI attach to GUI/Host; no live-engine transfer and no system daemon.

Ordinary GUI close behavior remains compatible. Closing a detached timer's view disconnects that view. TUI quit considers only its own non-detached sessions and existing prompt preference; observing another frontend does not grant lifetime ownership. Foreground cancellation affects only its command-owned session. Detached sessions remain queryable until dismissed or configured removal. Host drains work and exits only when sessions/clients no longer require it. On transport loss, do not silently replay uncertain mutations; recovery first acquires authority and restores persisted state.

## CLI contract

Command factory lives outside Program.cs, with injected client and writers. Implement start/list/status/pause/resume/stop/restart/dismiss; saved list/add/run/remove/clear; recent list/clear; config list/get/set; gui/tui/doctor/about/version. Add update <id> for edit/options parity and saved run --all. Normalize shorthand (including multiword input and -t/--title) into start before parsing. No arguments prints CLI help; GUI no-argument launch remains activation.

Saved selectors use exact ID, then an unambiguous display name/header. Config uses an explicit typed key registry, not arbitrary reflection. Global defaults affect future sessions; targeted changes affect existing sessions. Preserve characterized GUI setting semantics.

Successful output is stdout; errors/diagnostics stderr; no noninteractive prompts. --plain uses documented tab-separated text without ANSI/decorative Unicode and escapes embedded controls. --json emits one schemaVersion/command/result document; errors emit structured stderr. Timing units are explicit and culture-invariant. Reject --plain with --json. Foreground mode has no default animation; repeating timers wait until stopped/interrupted.

Exit codes: 0 success; 1 unexpected failure; 2 usage/expression/key/value validation; 3 unknown session/saved timer; 4 unavailable runtime/start failure; 5 transport/timeout; 6 unsupported operation/protocol; 7 persistence/config I/O; 8 locked/invalid transition/ambiguous selector/revision conflict; 130 foreground interruption.

## TUI and IPC contracts

TUI workflows: dashboard, selector, create/edit, saved, recent, settings, help, durable errors/status. Dashboard keys: Enter confirm, Space/Ctrl+P pause, s/Ctrl+S stop, r/Ctrl+R restart, e edit, n new, Tab/Shift+Tab sessions, v saved, comma settings, ?/F1 help, Esc back/dismiss, q quit. Dialog Tab traverses fields and printable shortcuts never steal text input.

Default repaint 5 Hz; accessible mode 1 Hz with textual progress/minimal color. Timing is runtime-owned. At 80x24 use list/detail; smaller sizes collapse to detail/selector and scrollable dialogs; tiny sizes retain readable status/help/quit. Restore terminal modes/cursor/alternate screen on normal/exception paths and print exception diagnostics afterward.

Control requests: protocolVersion/requestId/requestKind/sessionId?/payload. Responses: protocolVersion/requestId/success/result?/errorCode?/errorMessage?. Four-byte big-endian framing; owner-only directory/socket; same-user peer verification; stale cleanup only under lock. Initial constants: request 64 KiB, response 1 MiB, four handlers, five-second request deadline, separate bounded startup readiness. Paginate collections. Start with snapshot polling, no frontend countdown calculations. Cover cancellation, shutdown, protocol mismatch and uncertain outcomes. Never blindly retry mutations.

## Milestone execution plan

All stages update this document with status, work/files/tests/validation, GUI regression evidence, remaining work, discoveries and risks. Run focused characterization tests before moving behavior and full gates after every executable milestone. All stages leave GUI usable. Initial milestones are internal development stages, not a claim that DEV-18 is complete.

| Stage | Objective and concrete affected/new components | Dependency, migration and unchanged behavior | Tests, risks and exit criteria |
| --- | --- | --- | --- |
| 0 | Plan/ADR/research; isolated tools/Hourglass.TerminalSpike and CLI spike; pinned dependencies | First unit; no application behavior moved. Record baseline. | Parser streams/cancellation; 4/5/10 Hz repaint, keys/resize/normal and exceptional cleanup. Risk: toolkit blocker. Exit: evidence and pinned versions committed before extraction. |
| A | Application/Application.Tests; solution and ArchitectureTests | After 0; establish contracts/dependency guards without behavior changes | Direct/transitive boundaries and immutable ownership. Exit: all existing GUI/solution tests pass. |
| B | TimerSession/snapshot/expiry/scheduler; MainWindowViewModel/MainWindow | After A; characterize then extract small lifecycle units; switch ticking once; retain presentation | Lifecycle/zero/exact expiry/absolute/edit/lock/loop/slow effects/stale completion. Risk: double tick/effect ordering. Exit: sole engine owner and GUI behavior preserved. |
| C | Manager/repositories/settings/saved/recent; TimerWindowCoordinator/SavedTimersStore/CoordinatedSessionInhibitor/WakeAlarmController | After B; extract registration/restore before replacing window enumeration; preserve geometry and schemas | Legacy/empty/corrupt restore, options, overlapping saves, startup ordering, close, lease races, wake. Risk: data loss. Exit: runtime owns shared persistence and logical sessions. |
| D | CLI/CLI.Tests, command factory/client/output writers | After C; implement against shared runtime; interim MVP requires exclusive authority, never concurrent writers | Requested syntax/output/exit/JSON/no prompts/cancellation. Risk: grammar regression. Exit: foreground CLI and GUI launch compatibility. |
| E | TUI/TUI.Tests, dashboard/editor/keymap/presentation | After D and spike; bind shared client, interim exclusive authority | Keyboard/text conflicts/resize/selection/edit conflict/PTY. Risk: cleanup/toolkit logic leakage. Exit: complete keyboard timer workflow. |
| F | Terminal parity adapters, remaining shared commands | After E; expose already-shared settings/saved/recent/options/effects/diagnostics | Parity matrix, defaults, selectors, unavailable services, sound assets. Risk: option divergence. Exit: all requested shared behaviors covered; GUI wake retained, unsupported shutdown unchanged. |
| G | Application control contracts; Services server/client; GUI startup/launch bridge | After F; add control alongside legacy endpoint; separate GUI singleton and runtime authority | Malformed/truncated/oversized/version/permission/stale/race/concurrent/cancel/lost response/server death. Risk: split authority. Exit: second-shell commands control identical sessions. |
| H | Host, bootstrap/client lifetimes, optional lifetime metadata | After G; CLI waits through GUI/Host, TUI attaches; preserve ordinary GUI closes | Startup races/readiness/missing host/shell independence/GUI close/TUI quit/cancel/recovery/idle exit. Risk: orphaned or premature shutdown. Exit: detached timers survive frontend exits without transfers. |
| I | Accessibility/PTY; terminal publishing; CI/release/docs/shared version property | After H; preserve GUI artifact/desktop/AppStream/Flatpak behavior; tarballs first | x64/arm64 executable and content checks, terminal cleanup, GUI packaging, consistent versions. Exit: all definition-of-done evidence recorded. |

## Validation commands

```sh
dotnet restore Hourglass.Linux.sln
dotnet build Hourglass.Linux.sln --configuration Release --no-restore -warnaserror
dotnet test Hourglass.Linux.sln --configuration Release --no-build --verbosity normal
dotnet format Hourglass.Linux.sln --verify-no-changes --no-restore --verbosity minimal
dotnet run --project tools/Hourglass.ReleaseTool --configuration Release --no-build -- validate-version
```

Retain scripts/publish-linux-release.sh, packaging/appimage/build-appdir.sh, build-appimage.sh and scripts/validate-linux-packaging.sh with HOURGLASS_REQUIRE_PACKAGE_VALIDATORS=true. Publisher requires a clean worktree: validate, commit, then package. New terminal publishing must include assets/notices and preserve GUI release gates. Do not use the personal-website pnpm workflow here.

Keep attended GUI, real notification/audio, SSH/tmux/terminal-emulator, and physical wake validation separate from automated tests. Mark unavailable checks Not run; do not infer them from CI or headless tests. Domain tests use injected time, not sleep/real clocks; PTY integration uses bounded external process deadlines.

## Risk register

| Risk | Mitigation |
| --- | --- |
| Avalonia regressions | Small moves, characterization and behavioral coordinator tests, GUI gates each stage |
| Multiple timer truths | Sole session engine owner and serialized runtime |
| Concurrent persistence | Authority lock, one writer, ordered atomic saves/merges |
| IPC ownership | Separate process authority from frontend/window lifetime |
| Stale sockets | Cleanup only after lock acquisition |
| Toolkit churn | Version pins, isolated spike, no toolkit types outside TUI |
| Terminal compatibility | Printable alternatives, scoped bindings, PTY/manual matrix |
| Accessibility | CLI parity, textual state/focus, reduced refresh |
| Schema drift | Existing formats first; fixtures for targeted optional lifetime metadata |
| Packaging differences | Native terminal channel, explicit Flatpak namespace limits |
| Delayed effects | Revision checks, cancellation, deterministic disposal |
| Version divergence | Shared evaluated version and release metadata tests |

Highest risks: session/expiry extraction, window-independent persistence, runtime authority/lifetime integration.

## Requirement traceability and definition of done

- User sections 1-4/6-7/36/40/42: source reading, branch, architecture, plan/ADR and milestone evidence: stage 0 and ongoing.
- Sections 2/5/8-12/21/25-26/38: shared runtime, exactly one engine, GUI compatibility and persistence: A-C, protected in every later stage.
- Sections 13-15/27/30: CLI hierarchy, shorthand/output/errors/accessibility: D/F/I.
- Sections 16-20/29-30: toolkit spike, keyboard workflows, parity and PTY: 0/E/F/I.
- Sections 22-24/28: authority, IPC, detached runtime: contracts A/C, implementation G/H.
- Sections 31-35/37/39/41: distribution, CI, decomposition, risks, quality and complete delivery: all stages, final I.
- DoD requires working supported GUI, shared session behavior, complete CLI/TUI, authoritative detached control, compatible XDG data, passing Core/Application/Avalonia/CLI/TUI/IPC/PTY/build/format gates, terminal artifacts and documentation. MVP completion alone is not DEV-18 completion.

## Progress ledger

### Stage 0 — Complete (b30a4da)

- Completed: full specification/research reading, architecture inspection, agreed lifetime decisions, clean worktree check, remote fetch and dedicated feature branch from develop.
- Changed: plan, ADR, full research reference and reproducible isolated tools/Hourglass.TerminalSpike; no domain/GUI changes.
- Baseline restore: passed with SDK 10.0.112 (allowed feature roll-forward).
- Build/tests/format: rerun 2026-09-26; Release build passed without warnings, all 763 tests passed, solution format verification passed. ReleaseTool validate-version passed (0.2.0).
- The initial sandbox test run failed with MSB1025 / SocketException (13), permission denied creating MSBuild IPC sockets. The identical test command passed with escalated permission.
- Spike: pinned System.CommandLine 2.0.0 / Terminal.Gui 2.5.0; parser streams/validation/cancellation and all five PTY scenarios passed. See tools/Hourglass.TerminalSpike/README.md for reproduction and Alt+Enter encoding.
- GUI regression status: existing automated tests passed; attended desktop and hardware checks Not run.
- The original Downloads research path is absent; the preserved repository research remains the reference.

### Stage A — Complete

- Added Application and Application.Tests to the modern solution. Contracts provide immutable snapshots, typed lifecycle/edit commands, explicit expected-failure results and an async local/remote client boundary.
- Session IDs remain strings, matching existing persistence (including non-GUID IDs). Edit revisions describe committed changes, not repaint ticks.
- Guards inspect direct project references and resolved transitive dependencies. Snapshot tests protect earlier values and published collection ownership.
- GUI execution is unchanged. B-I remain pending.
- Validation: restore and Release warning-as-error build passed; all 767 solution tests passed (four new Application tests); solution format verification passed. Existing GUI tests remain green.

### Stage B — In progress: pure policies extracted

- Avalonia now references Application and delegates timer input validation, time/progress formatting and expiry loop/close/attention decisions to shared pure functions.
- Added 11 policy cases for invalid inputs, supplied wall-clock parsing, expiry precedence and display semantics. Focused Application tests passed (15 total).
- Qualified the Avalonia Application base type to avoid the new Hourglass.Application namespace collision.
- Validation: restore, Release build with warnings as errors, all 778 solution tests and solution format verification passed. Existing 354 Avalonia tests passed.
- Engine scheduling, session/effect ownership and GUI adapter migration remain pending. This is the first small extraction, not stage B completion.
