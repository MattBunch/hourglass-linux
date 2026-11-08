# Local runtime control (DEV-18 Stages G–H)

One process holds `hourglass-linux.lock` and owns all engines and shared writes.
GUI, CLI and TUI use `hourglass-control.sock` in the same per-user runtime directory.
GUI presentation uses its separate `hourglass-gui.lock` / `.sock` registration.
The original `hourglass-linux.sock` remains a compatibility GUI-launch bridge.

## Runtime and frontend lifetime

A GUI launched first can own the runtime. Terminal session commands attach to it,
or launch the internal `hourglass-host` when no authority exists. CLI/TUI processes
never own production engines. There is no live-engine handoff and no system daemon.
The host creates a new Linux session, redirects its standard descriptors to
`/dev/null`, and retains the XDG and desktop-service environment of its launcher.
Host resolution checks the installation directory, matching development output,
then PATH. Build the full solution before running development terminal binaries.

Session snapshots expose `Gui`, `Foreground`, `Tui` or `Detached` lifetime separately
from timer state. Connection identities are assigned by the authority and never
persisted. Foreground exit/cancellation affects only its operation. TUI quit or
connection loss closes its ordinary sessions. Observing another frontend's timer
does not grant ownership of its lifetime.

`hourglass start --detach`, `hourglass saved run --detach` and `hourglass detach ID`
explicitly select detached lifetime. The TUI's dashboard `d` action asks for
confirmation. Detachment preserves the engine and countdown. Locked timers must
be unlocked first. An active foreground operation cannot be detached in place;
start it with `--detach` instead. Detachment is one-way in Stage H.

Closing a detached GUI view disconnects presentation without removing the timer.
A GUI authority stays alive without windows while retained sessions or terminal
clients require it; reopening activates views in that same process. Retained
paused, stopped and expired detached sessions keep authority until dismissed or
removed by their configured completion behavior. A host exits after three seconds
without clients or live sessions, after draining effects and persistence.
Explicit process termination saves recovery; it does not transfer running engines.

## Recovery and durability

The existing active-session envelope has optional lifetime metadata. Missing
metadata means legacy GUI lifetime; timing document version and calculations are
unchanged. Transient foreground/TUI checkpoints are not restored after a crash.

Terminal startup restores detached records when `RestoreActiveSessionOnStartup`
is enabled, and preserves deferred GUI records alongside live checkpoints. It does
not restore ordinary GUI records, open startup saved timers or create an empty GUI
session. An attached GUI invokes shared initialization to apply the existing GUI
restoration and startup preferences exactly once. GUI connection loss retires its
ordinary live sessions into recovery records; explicitly closed sessions are removed.
Disabling restoration retains the existing discard semantics when each frontend's
recovery is initialized. Configuration/catalog transactions do not initialize sessions.
An unreadable recovery file prevents terminal host startup with a persistence error;
GUI recovery retains its existing fallback and diagnostic behavior.

Detached success is acknowledged after persistence drains. If storage fails, the
error identifies live detached sessions whose state can be inspected; it does not
claim durable success. Lost mutation responses have uncertain outcomes and are
never automatically replayed. After authority loss, reopen clients to acquire or
attach to a new authority and restore eligible records with stable IDs.

## Protocol 2

Framing remains four-byte big-endian length plus UTF-8 JSON. Requests contain
`protocolVersion`, `requestId`, `requestKind`, optional `sessionId` and `payload`.
Responses contain matching version/ID, `success`, `result`, `errorCode` and
`errorMessage`. `hello` registers a typed client kind and returns authority identity.
A connection cannot renegotiate ownership. Protocol 1 is rejected with unsupported
operation / CLI exit code 6. Lifecycle and catalog requests retain explicit payload
allowlists; new requests are `detached-start`, `saved-detached`, `detach` and
`gui-initialize`. Frontends receive snapshots and never advance countdowns.

Limits remain: 64 KiB requests, 1 MiB responses, four handlers, 32 connections,
32 subscriptions/eight foreground operations/32 captured pages per connection.
Pages contain at most 64 entries and half the response byte limit. Subscription
queues hold 256 notifications and report overflow. Request/shutdown deadlines are
five seconds; startup readiness is ten seconds. Foreground operations use bounded
start/status/cancel requests rather than one long-running protocol request.

Endpoints retain owner-only permissions, filesystem type/ownership validation,
same-user peer credentials and stale cleanup only under authority. Idle shutdown
is elected on the serialized runtime queue; new ownership registrations are refused
once draining begins. Existing responses and durability checks drain before the
lock is released. GUI/TUI show disconnection and require explicit reopening.

## Checks

After a Release solution build:

```sh
python3 tests/Hourglass.Cli.Tests/verify-control.py
python3 tests/Hourglass.Host.Tests/verify-host.py
python3 tests/Hourglass.Tui.Tests/verify-terminal.py
python3 tests/Hourglass.Linux.Avalonia.Tests/verify-lifetime.py
```

The GUI process check requires X11 (CI uses Xvfb). These bounded checks do not
establish attended desktop, SSH, real notification/audio or physical wake coverage.
Host publishing includes sound assets; combined terminal release tarballs remain
Stage I.
