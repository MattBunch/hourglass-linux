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

### Stage B — In progress: pure policies and session engine boundary extracted

- Avalonia now references Application and delegates timer input validation, time/progress formatting and expiry loop/close/attention decisions to shared pure functions.
- Added 11 policy cases for invalid inputs, supplied wall-clock parsing, expiry precedence and display semantics. Focused Application tests passed (15 total).
- Qualified the Avalonia Application base type to avoid the new Hourglass.Application namespace collision.
- Validation: restore, Release build with warnings as errors, all 778 solution tests and solution format verification passed. Existing 354 Avalonia tests passed.
- TimerSession now contains the engine and captures its synchronous lifecycle signals as CountdownTransition results. The GUI reads immutable CountdownState values and applies returned effects instead of subscribing directly to engine expiry. Legacy engine-taking GUI constructors remain a migration bridge.
- Session lifecycle revisions invalidate delayed expiry work after stop/restart/restore/disposal. GUI expiry tasks are available to deterministic regression tests; stale notification completions cannot play audio, restart or close a changed session, and stale acquired audio playback is disposed.
- Five session tests cover immutable earlier state, exactly-once expiry, pause-at-expiry, monotonic resume, absolute restart rejection and disposal. Three GUI regression cases cover delayed notifications across stop/restart/disposal.
- Intentional remaining mutation: TimerSession captures engine events locally; Avalonia still schedules Tick, owns effect adapters/settings and composes sessions through a temporary internal assembly bridge. The serialized runtime queue, independent scheduler, session registry, shared effect/lease coordinator and complete GUI adapter migration remain pending. Stage B is not complete.
- Next boundary: move ticking and session registration into HourglassRuntime, route GUI lifecycle commands through its serialized queue, then extract effect/lease ownership with revision-aware completion dispatch. Retain deterministic direct-tick seams for tests and DemoRecorder.
- Session-boundary validation: restore, warning-as-error Release build, format and version validation passed. The default full run hit the existing DisposeWaitsForActiveRequestHandler timing assertion (production drain is bounded to 250 ms); the isolated rerun passed and the full suite with `-m:1` passed all 786 tests. No test assertion or production IPC timeout was weakened.

### Stage B — Runtime scheduling continuation

- HourglassRuntime now owns a bounded serialized mutation queue, session registrations and an independent 250 ms scheduler. TimerWindowCoordinator supplies one runtime to its view models. Window refresh timers no longer advance engines.
- GUI commands and restoration execute short session mutations on the queue; immutable countdown snapshots are published with lifecycle revisions. Tick callbacks marshal to the GUI dispatcher and discard stale or closing-session updates. Platform effects run outside the queue.
- Close preparation suspends its session's automatic updates; disposal removes registration and unsubscribes engine events. Runtime disposal cancels scheduling and drains accepted commands. DemoRecorder explicitly selects manual scheduling and retains injected-clock advancement.
- Added seven runtime tests and two GUI behavioral tests for multi-session ticking without windows, serialization, callback isolation/reentrancy, cancellation, removal/disposal, dispatcher publication and close behavior. Replaced the obsolete window-timer source assertion with the new wiring plus behavior coverage.
- Validation: restore, warning-as-error Release build, format and full solution tests (`-m:1`) passed: 795 tests. The first integration run caught the obsolete refreshTimer source assertion; its replacement passed. Attended GUI/terminal/hardware checks remain Not run.
- Still pending in B: shared expiry/effect and lease ownership, fully typed frontend commands and complete view-state adaptation. Registration and synchronous GUI command invocation are deliberate migration bridges. C-I remain pending.

### Stage B — Shared expiry sequencing continuation

- TimerExpiryCoordinator now sequences inhibition release, notification, audio and optional shutdown through a narrow effect interface. It returns a revision-tagged restart/close/none/superseded result; it never mutates timers through frontend callbacks.
- GUI presentation prepares completion visuals and applies final restart/close on its dispatcher. Restart validates the expected revision inside the runtime queue. Restored expiry shares notification/audio sequencing while preserving its no-loop/no-shutdown/no-auto-close behavior.
- Six new Application tests cover effect ordering, supersession at each boundary and restored expiry. Restore, Release warning-as-error build, format, release-version validation and all 801 solution tests (`-m:1`) passed, including 359 Avalonia tests and 89 DemoRecorder tests.
- Remaining B work: move audio/inhibition lease ownership out of the GUI adapter, drain asynchronous effects during shutdown, and replace the internal migration bridge with typed session commands and complete immutable metadata. Then proceed to C's shared persistence/settings/restoration and D-I's terminal clients, IPC, host and distribution. DEV-18 is not complete.

### Stage B — Application effect ownership

- SessionEffects now owns notification/audio/power adapters and audio/inhibition leases. Immutable expiry requests capture options and notification text; no application effect reads frontend properties.
- Resource generations and lifecycle revisions guard acquisition/adoption on the runtime queue. Cancellation callbacks, service calls and lease disposal run outside that queue. Late or superseded acquisitions are disposed exactly once, including preference changes during notification work.
- Runtime registrations retain effect work through removal. Async cleanup and a five-second TimeProvider-based drain deadline retain late cleanup and report incomplete drains. GUI IDisposable initiates cleanup; awaited production close integration follows in the next commit.
- Added nine deterministic application cases covering late audio/inhibition acquisition across lifecycle changes/removal/preferences, reverse completion, notification-time preferences, and queue responsiveness. Existing GUI assertions now await asynchronous expiry where needed.
- Validation: warning-as-error Release build and all 810 tests passed (`-m:1`). Formatting applied. Baseline 801 tests also passed before changes. Sandbox GUI VSTest failed to create its socket; the escalated full run passed. Attended desktop/audio/hardware checks Not run.
- Next unit: await cleanup from window close, drain preview/shared inhibition, kill canceled notification subprocesses, and add explicit deadline/shutdown regression coverage.

### Stage B — Awaited effect shutdown integration

- Window close now awaits view-model cleanup after persistence preparation, including in failure paths. Repeated disposal shares a completion task; IDisposable starts observed cleanup, while production close uses IAsyncDisposable.
- Cancellation sources remain alive until their operations finish; late backends can still inspect canceled tokens. Runtime shutdown rejection is normal cancellation, and adopted or rejected leases retain exactly one disposal owner.
- Session removal invalidates ownership before waiting. Each effect drain has a five-second injectable TimeProvider deadline; timeout records an incomplete drain and preserves cleanup/observation of late completions. Runtime cleanup runs even after scheduler failure. Shared inhibition is disposed after session effects finish, with cancellation forwarded to its underlying acquisition.
- Preview work remains a GUI concern but cancellation and returned playback disposal are awaited. Expiry effect tracking finishes before restart/close publication, preventing a close callback from waiting on itself. Suspended/closing views reject delayed restart/close actions.
- Canceled notify-send operations now terminate and reap their owned process. The subprocess regression uses a shell blocked on input, without timer sleeps.
- The recorder now pumps its dispatcher while async close finishes; final window UI work is explicitly dispatched. The first integration run exposed the previously synchronous headless-close assumption and stale source assertions; those were corrected, retaining rendered-frame and deterministic-scenario coverage.
- Added deterministic tests for pause/restart acquisition races, drain deadlines and late cleanup, idempotent shutdown, cancellation diagnostics, platform failures, slow/failing lease disposal, close callback disposal and preview cancellation. Existing GUI effect tests await completion rather than depending on immediate execution; their outcome assertions are retained.
- Validation: restore, Release warning-as-error build, format verification and release-version validation (0.2.0) passed. All 825 solution tests passed (`-m:1`): Core 119, Avalonia 361, Linux.Services 157, DemoRecorder 89, ReleaseTool 45, Application 54. Sandbox restore/format/test IPC restrictions required escalated reruns. Attended GUI, real notification/audio, SSH/terminal and physical wake checks Not run; no packaging claim is made for this internal extraction.
- This completes the planned effect-ownership/shutdown unit. Stage B still needs typed frontend commands and complete immutable metadata/view-state adaptation. Stage C then moves settings, persistence and restoration; CLI/TUI/IPC/host/distribution remain D-I.

### Stage B — Complete: typed client and presentation migration (2026-09-27)

- The existing HourglassRuntime implements IHourglassClient. Typed creation, preparation, lifecycle, update, unlock, query and subscription operations run on its serialized queue. Creation and preparation do not start a timer. Invalid expressions, stale edits, duplicate IDs, locks and invalid transitions return typed failures before mutation.
- TimerSession owns committed input/title, TimerDefaults and ApplicationPreferences alongside its sole CountdownEngine. Application constructs production engines from injected clocks. Public edit revisions exclude ticks and no-ops; lifecycle invalidation and resource generations remain separate so renaming cannot cancel pending expiry. Publication sequence numbers order snapshots independently of edit revisions.
- Runtime commands and scheduler ticks use the same transition processor. Application starts audio/inhibition work and owns expiry, automatic unlock, looping and configured logical removal without subscribers. Restored expiry retains notification/audio-only behavior. Completion-policy changes invalidate delayed final actions; existing resource-generation and bounded-drain protections remain.
- Subscriptions atomically return an initial snapshot, serialize asynchronous delivery outside the mutation queue and isolate subscriber failures. Avalonia consumes immutable snapshots and typed commands; its internal Application assembly bridge is removed. Frontend code constructs no engines, owns no internal sessions/effects and supplies no arbitrary runtime mutation delegates.
- Avalonia retains drafts, localized strings, visual/attention events, geometry, desktop projections, audio preview and current stores. Commands are observed and serialized asynchronously. Edit baselines detect external conflicts; only acknowledged local changes advance them. Locked timers allow title changes and explicit Unlock. Existing persistence documents and merge rules remain; settings mutations persist after application acceptance. Persistence/restoration orchestration remains for Stage C.
- The recorder advances the shared runtime using injected time and awaits commands/effects while pumping its headless dispatcher. Regression tests retain their behavior assertions and await asynchronous completion. Integration exposed durable-error refresh, restoration ordering, expiry signal ordering, title preparation and headless loading races; these were corrected.
- Added public-client lifecycle/edit/lock/cancellation/preparation tests, no-window expiry/loop/removal/restoration tests, slow-subscriber and delayed-policy coverage, GUI unlock/conflict coverage, and architecture guards. Earlier lease-race, shutdown and GUI tests remain.
- Validation: restore, Release build with `-warnaserror`, all 840 solution tests (`-m:1`), format verification and release-version validation (0.2.0) passed. Counts: Core 119, Avalonia 363, Linux.Services 157, DemoRecorder 89, ReleaseTool 45, Application 67. Local tooling sockets required escalated execution. The failed intermediate headless run and projection races were corrected before the successful full run; generated hang evidence was moved outside the worktree to /tmp.
- Intentional mutation remains inside the queued runtime/session/resource adapters and the shallow Avalonia adapter. Command submissions and snapshot projections share a presentation gate, while committed domain values and public snapshots remain immutable. Platform effects and asynchronous subscribers run outside the runtime mutation queue.
- Stage B is complete. Stage C has not started: its next scope is session registry/persistence/restoration/store coordination extraction, using the existing formats and runtime/client architecture. CLI/TUI/IPC/host/distribution remain D-I. Attended desktop, real audio/notifications, SSH/terminal and physical wake checks remain Not run; no packaging claim is made.

### Stage C — Complete (2026-09-27)

- Baseline: clean feature branch at `a05c1aa`; all 840 existing solution tests passed before editing.
- C1/C2: retained settings/saved/theme merge characterization; moved coordinated stores and theme merges into Application. `ApplicationData` supplies the authority's ordered writer, diagnostics, and per-document durability failures. Typed client operations cover settings, saved timers, recents, themes, presentation metadata, and persistence flushing.
- C3: extracted the existing registry into `TimerSessionManager` without creating another engine owner. `ActiveSessionRepository` retains missing/empty/corrupt/legacy behavior. Data loading and single-flight session startup are separate operations; startup suppresses partial session saves and preserves restore/saved-template/explicit-launch precedence.
- C4: GUI startup attaches to existing runtime sessions. Session documents are captured from the logical registry; Avalonia supplies drafts, presentation mode, and geometry. Ordinary close removes the logical session before flushing; runtime disposal preserves persisted sessions. Legacy standalone saves use the same Application mapping/writer. Settings-save results carry revisions to prevent stale acknowledgments from changing newer selections.
- C5: Application owns shared inhibition and aggregate wake scheduling. Wake requests use logical sessions and injected clocks/text, skip superseded requests, dispose late leases, and drain alongside persistence and session effects using the existing shutdown deadline.
- New coverage: headless restoration, startup cancellation and ordering, invalid/corrupt documents, ordered/failed saves, concurrent catalog changes, drafts/geometry, shared inhibition, logical wake scheduling, late wake leases, and GUI attachment/restored attention. Existing wake tests moved into Application; migrated source-text restoration assertions are replaced by behavioral tests.
- Validation: baseline 840 tests passed. Final restore, Release warning-as-error build, all 860 solution tests (`-m:1`), and release-version validation (0.2.0) passed. Counts: Core 119, Avalonia 355, Linux.Services 157, DemoRecorder 89, ReleaseTool 45, Application 95. Focused Application and GUI runs also passed. Full solution format verification passed after correcting registry indentation. Local tooling sockets required escalated execution; the sandboxed restore exited unsuccessfully without diagnostic output.
- Regression fixes: duplicate snapshot delivery no longer overwrites newer GUI selections; settings acknowledgments are revision guarded; controlled GUI scheduler tests wait for queued work explicitly. Persistence requests after shutdown return typed unavailable results. Runtime owns inhibition cleanup once; the coordinator no longer starts a second drain.
- Intentional mutation remains in the serialized registry/session adapters, ordered document writer, resource controllers, and shallow presentation state. Countdown/domain values and public snapshots remain immutable. Storage and platform effects run outside the mutation queue; window drafts and geometry remain presentation metadata. Existing schemas and the authority lock remain unchanged; legacy standalone restoration/document helpers are retained for compatibility.
- Stage C is complete; the final gates passed. Stage D is next; CLI/TUI/IPC/host/distribution remain D-I. Attended desktop, real audio/notifications, SSH/terminal, and physical wake checks remain Not run; no packaging claim is made for this internal extraction.


### Stage D — Complete

- Approved scope: foreground CLI and typed lifecycle handlers over the existing runtime. Cross-process control remains G and detached ownership H. Interim start refuses an existing authority or preserved recovery records; it does not restore unrelated sessions.
- Remote Stage C validation failed at c8cb588: run 36310575861 observed the legacy checkpoint before subscription delivery had queued its save. Earlier Stage B run 36303462263 exposed the lease-test fake signaling acquisition entry before capturing its result task. The previously recorded 860-test pass was local evidence only.
- CI repair: persistence waits include accepted subscription delivery without waiting for slow expiry services. A controlled held-dispatcher regression protects the barrier. Each acquisition captures its result before publishing readiness, and the reversed-acquisition test controls both entries. Held notification tasks are released in finally paths. CI now records TRX failure artifacts.
- Validation: Release warning-as-error build and all 861 solution tests passed with normal project parallelism. Both repaired races and the new held-delivery case passed three focused repetitions. Formatting applied to changed C# files. Remote validation of the completed milestone is pending.
- Shared application commands: `RunForegroundAsync` owns one session through the authoritative runtime, waits for its terminal state and effects, persists the result, and removes only that session on completion/cancellation. `ExecuteAllAsync` prevalidates the selected sessions and applies pause/resume/stop within one serialized runtime operation. No second timer implementation or engine owner was added.
- CLI project: pinned System.CommandLine 2.0.0, injected command handler/output/runtime factory, exact-ID lifecycle commands, atomic `all` operations, expression shorthand/title alias, human/plain/JSON output, defined exit codes, and Ctrl+C/SIGTERM handling. `--detach` reports unsupported until H. The interim local factory uses the GUI authority lock; it refuses an active owner or saved recovery state before foreground start. Queries do not restore or reinterpret saved sessions.
- Process output: Linux notification/audio subprocesses capture both streams so their diagnostics cannot corrupt CLI JSON. CLI includes shared sound assets. See `docs/cli.md` for the Stage D contract and limits.
- New tests: foreground expiry/effects/cancellation/looping/persistence and recovery guards, atomic batch validation, CLI parser/streams/JSON/exit codes/locking, and service stream redirection. GUI architecture tests include the CLI dependency boundary.
- Local validation: restore and Release warning-as-error build passed with zero warnings. All 912 solution tests passed with normal project parallelism (Core 119, Application 108, Avalonia 356, Linux.Services 157, CLI 38, DemoRecorder 89, ReleaseTool 45). A prior full-run attempt was interrupted after an apparent stall; the isolated Application suite passed and two subsequent full runs completed. Release-version validation passed at 0.2.0; full solution format verification and `git diff --check` passed.
- Process smoke: isolated XDG state, `hourglass start '0 seconds' --json` exited 0 and emitted one expired-result JSON document.
- Remote verification: branch push succeeded over SSH because the keyring-backed GitHub OAuth credential has no workflow scope. Run 36397579313 reached the test step, which exceeded local test duration and was canceled by the diagnostic follow-up push. CI now applies a per-test 90-second hang deadline and uploads the test sequence alongside TRX failures without collecting memory dumps. [Run 36398020504](https://github.com/MattBunch/hourglass-linux/actions/runs/36398020504) completed successfully: solution restore, build, test, format and release-version steps passed; the dependent GUI publish, AppDir, AppImage, package validation, and artifact uploads passed.
- Stage D exit: foreground lifecycle works through one `HourglassRuntime`, terminal output and errors are tested, the exclusive authority rule preserves GUI recovery state, and the GUI distribution gate remains green. Cross-process commands, detached lifetime, saved/recent/config parity, the interactive TUI and terminal release tarballs remain E-I work. Attended GUI, real desktop notification/audio, terminal emulators, and physical wake remain Not run.

### Stage E — Complete locally (2026-09-29)

- Scope: Terminal.Gui 2.5.0 interactive timer MVP. The agreed interim startup policy preserves and refuses GUI recovery sessions. Saved/recent/settings parity remains F, IPC G, detached lifetime H, distribution I.
- The existing exclusive runtime factory has moved into Linux.Services so CLI and TUI use one authority/recovery implementation. Application adds typed close through the existing session removal and persistence boundary; no presentation project constructs a timer engine.
- TUI uses immutable shared snapshots for multiple sessions, selection, create/edit, lifecycle keys, conflict handling and quit. Terminal.Gui owns only views and keyboard routing. Dashboard and editor inputs are scoped, and conflicts retain drafts for reload or retry. The terminal returns to its prior state on normal quit, interruption and exception.
- Validation: restore; Release build with `-warnaserror` (zero warnings); all 923 solution tests (`-m:1`); full solution format verification; release-version validation (0.2.0); and bounded PTY lifecycle, resize, launcher and terminal-restoration checks passed. Counts: Core 119, Application 112, Avalonia 356, Linux.Services 157, CLI 39, TUI 6, DemoRecorder 89, ReleaseTool 45.
- An earlier full test run under concurrent PTY/format load hit a timing-sensitive existing Services test; an isolated full solution rerun passed. Final gate runs are sequential. [Remote run 36529592431](https://github.com/MattBunch/hourglass-linux/actions/runs/36529592431) passed restore, build, solution tests, TUI PTY smoke, format, version metadata, GUI publish, AppDir, AppImage, packaging validation and artifact upload.
- Intentional mutation remains in the shared queued runtime, infrastructure authority lease, and shallow TUI controller/view state. Immutable application snapshots are the TUI's sole timer data source. Attended GUI, desktop notification/audio, SSH/terminal-emulator, screen-reader and physical wake checks remain Not run.
- Stage F is next: saved/recent/settings and remaining application parity. IPC, detached lifetime and terminal distribution remain G-I.

### Stage F — Complete locally (2026-09-29)

- Shared application commands now resolve saved templates by exact ID or unambiguous name/header, prevalidate foreground batches, and apply one explicit registry of terminal-editable preferences. Saved batches use existing session ownership and cleanup. Runtime audio preview and a bounded diagnostic journal keep platform effects and errors behind the application boundary.
- CLI now exposes saved/recent/config/update/unlock/sounds/doctor/about/gui commands, option overrides and per-session progress/options. It retains the Stage D exclusive authority and foreground lifetime rules.
- TUI now has saved, recent, settings and selected-session option views, saved and recent collection confirmations, sound preview and diagnostic status. PTY coverage includes menu navigation and terminal restoration.
- Parity audit: saved list/add/run/remove/clear and open-all use `SavedTimerSelector` and application sessions in CLI/TUI; recents list/clear/use share `app.json`; config and per-session option edits use `SharedOptionRegistry`; expiry, loop, lock, notifications, audio and inhibition remain application-owned. Sound preview uses the existing Linux audio service through an application command. CLI `doctor` reports prerequisite checks and the absent Stage G control capability without invoking desktop services. `gui` launches the existing graphical executable. GUI-specific preferences and wake support remain available in Avalonia; unsupported shutdown remains unsupported.
- Validation: restore, Release warning-as-error build, 933 solution tests (Core 119, Application 117, Avalonia 356, Linux.Services 157, CLI 42, TUI 8, DemoRecorder 89, ReleaseTool 45), format verification, release-version validation (0.2.0), and the expanded PTY workflow passed locally. [Remote run 36536271021](https://github.com/MattBunch/hourglass-linux/actions/runs/36536271021) passed solution restore/build/tests, TUI PTY smoke, format, release metadata, GUI publish, AppDir, AppImage, package validation and artifact upload. An intermediate Application test assumed insertion order for saved templates; its assertion now follows the existing newest-first catalog order.
- Intentional mutation remains in the runtime queue, infrastructure adapters, bounded diagnostic journal and shallow TUI presentation state. Terminal commands consume immutable snapshots and do not construct engines. Existing GUI recovery sessions still require the GUI; cross-process control, detached lifetime and terminal distribution remain G-I. Attended GUI, physical audio/notification, SSH/terminal emulator, screen-reader and wake checks are Not run.

### Stage G — Complete (2026-10-01)

- Application adds versioned control envelopes, handshake identity, captured collection pages, ordered subscription polling and foreground operation handles. Existing application commands remain the behavior boundary; snapshots cross the socket without advancing countdowns. Each TimerSession continues to own exactly one engine.
- Linux.Services adds the explicit command dispatcher, framed control server and remote IHourglassClient. The connect-or-own factory attaches without constructing a runtime when authority is held, and publishes control for terminal-owned session runtimes. Query-only operations retain their short transaction and recovery-protection behavior.
- Protocol 1 limits: 64 KiB requests, 1 MiB responses, four request handlers, 32 connections, 32 subscriptions/eight foreground operations per connection, 256 retained notifications per subscription, five-second request/shutdown deadlines and ten-second startup readiness. Collections use captured pages. Same-user credentials and private endpoint ownership/type/modes are checked; symlinks are rejected. Only owners remove stale endpoints. Mutations are never replayed automatically.
- GUI presentation registration has its own lock/socket. GUI startup owns or attaches to the authority; view models accept the application client. Observed windows disconnect their views without removing sessions. GUI-created windows retain close behavior. GUI lifetime awaits runtime cleanup before releasing presentation registration.
- Foreground cancellation affects only connection-owned operations. Existing responses, durability checks and diagnostics drain during shutdown. Owner exit preserves recovery state and disconnects attached clients; host/detached lifetime remains Stage H. CLI doctor reports negotiated control availability; GUI/TUI show disconnection and disable mutations until explicitly reopened.
- Tests added: transport framing/version/payload errors, peer policy and endpoint permissions/symlinks, immutable countdown round trips, collection pagination, same-authority mutation, ordered effects/removal, foreground cancellation isolation, factory attachment, owner shutdown draining, and GUI client-only ownership/commands. CI now includes a process CLI control smoke and TUI attachment/owned-session PTY scenarios.
- Validation: restore; Release warning-as-error build; 960 solution tests (Core 119, Application 117, Avalonia 359, Linux.Services 181, CLI 42, TUI 8, DemoRecorder 89, ReleaseTool 45); format verification; release-version validation (0.2.0); CLI process control smoke; and the expanded TUI PTY matrix passed. A bounded GUI check attached to a terminal authority without replacement sessions, received lifecycle updates and exited cleanly after session removal. GUI evidence: `/tmp/dev18-g-gui-m0_ox7v5/gui.log`.
- Smoke-discovered fixes: verify/make the existing lock directory private before binding control; drain mutation responses and durability checks during owner shutdown; deliver queued removal notifications before disconnecting GUI observers. Regression tests cover the shutdown races.
- Delivery: implementation commit `d664b58` pushed to the DEV-18 branch. [Remote CI run 36798625598](https://github.com/MattBunch/hourglass-linux/actions/runs/36798625598) passed both test and packaging jobs, including CLI control, TUI PTY, formatting, version metadata, GUI publish, AppDir/AppImage creation and strict package validation. Existing SSH credentials supplied workflow-write access after the HTTPS OAuth token rejected the workflow change for missing scope.
- Local packaging: self-contained linux-x64 publish (`/tmp/hourglass-dev18-g-publish`), AppDir (`/tmp/hourglass-dev18-g.AppDir`), AppImage (`/tmp/Hourglass-0.2.0-dev18-g-x86_64.AppImage`), SHA256 verification and `HOURGLASS_REQUIRE_PACKAGE_VALIDATORS=true` validation passed. These artifacts preserve GUI distribution; terminal release artifacts remain Stage I.
- Intentional mutation is confined to the queued runtime, connection/subscription/operation registries, transport DTOs and shallow frontend state. Countdown snapshot mapping is invariant across cultures and performs no timer transitions. No persistence schema or timer implementation was duplicated.
- Attended desktop, real notification/audio, SSH, screen-reader and physical wake checks remain Not run.
