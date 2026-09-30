# Local runtime control (DEV-18 Stage G)

A single process holds the existing `hourglass-linux.lock` and owns all logical
sessions and persistence. GUI, CLI and TUI attach through `hourglass-control.sock`
in the same per-user runtime directory. GUI presentation is registered separately
through `hourglass-gui.lock` and `hourglass-gui.sock`. The existing
`hourglass-linux.sock` remains a compatibility launcher bridge.

Protocol 1 uses a four-byte big-endian length followed by UTF-8 JSON. Requests
contain `protocolVersion`, `requestId`, `requestKind`, optional `sessionId`, and
`payload`. Responses contain matching version/ID, `success`, `result`, `errorCode`, and `errorMessage`. Error codes map to the
existing typed application errors. The initial `hello` exchanges the authority instance ID.
Supported command payloads are explicit application contracts, including typed
session and saved-timer command discriminators.

Requests are limited to 64 KiB and responses to 1 MiB. Four handlers execute at
once across at most 32 connections. Each connection supports 32 subscriptions,
eight foreground operations, and 32 captured continuation pages. Collection pages
hold at most 64 entries and half the response byte limit. Ordered subscription
polls hold up to 256 retained notifications; overflow reports a transport error.
Frontends receive authoritative timing values and never calculate replacement
countdowns. Slow subscriber delivery is drained before fetching another batch.

Each request has a five-second deadline; connection readiness has ten seconds.
Foreground timers use short start/status/cancel requests and can run longer than
a request deadline. Connection loss cancels that connection's foreground
operations, while ordinary sessions remain recoverable. Commands with lost
responses can have uncertain outcomes and are never automatically replayed.

Endpoints must have the expected filesystem type, belong to the current user,
and deny group/other access. Both peers verify Linux socket credentials. The
owner verifies the runtime directory before making it private; stale control
sockets are removed only while holding runtime authority.

Owner shutdown stops accepting clients, lets existing responses and durability
checks drain, then disconnects remaining clients within five seconds. GUI and
TUI display disconnection and require reopening to reconnect. The process owner
continues to determine lifetime during Stage G; the on-demand host and explicitly
detached sessions arrive in Stage H. Persistence documents are unchanged.

After a Release build, run `python3 tests/Hourglass.Cli.Tests/verify-control.py`
and `python3 tests/Hourglass.Tui.Tests/verify-terminal.py` for bounded process and
terminal checks. These do not establish attended desktop or physical service
coverage.
