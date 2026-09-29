# Hourglass CLI (DEV-18 Stage D)

Build with `dotnet build Hourglass.Linux.sln -c Release`; the executable in
`src/Hourglass.Cli/bin/Release/net10.0/` is `hourglass`. For a development run:

```sh
dotnet run --project src/Hourglass.Cli -- start "25 minutes" --title "Focus"
dotnet run --project src/Hourglass.Cli -- list --json
```

`hourglass` with no arguments prints help. The standalone CLI accepts:

```text
hourglass start <expression...> [-t|--title <title>] [--wait]
hourglass <expression...> [-t|--title <title>]
hourglass list
hourglass status <session-id>
hourglass pause|resume|stop <session-id|all>
hourglass restart|dismiss <session-id>
hourglass version
hourglass --version
hourglass --help
hourglass tui
```

`start` waits without animation until expiry or a stop/dismiss command in its
shared runtime. A repeating timer keeps running until stopped or interrupted.
Ctrl+C and SIGTERM remove only the foreground command's session and return
130. `--wait` is equivalent to the default. `--detach` is reserved and returns
6; `--wait --detach` returns 2. Use `--` to end options before an expression.

Stage D uses the same per-user authority lock as the GUI. It does not contact
the GUI's legacy launch socket for session commands. When another runtime owns
the lock, CLI queries and commands return 4. With no owner, `list` shows no
*live* sessions and ID commands return 3; they do not reinterpret saved recovery
records as running timers. If saved GUI sessions remain on disk, foreground
start returns 4 and preserves those records. Restore or dismiss them in the GUI
first. Cross-process control arrives in Stage G and detached ownership in H.
This Stage D binary is an internal development artifact; terminal release
tarballs arrive in Stage I.

All information commands accept `--json` or `--plain`, including before the
command. The flags are exclusive. Successful data goes to stdout. The CLI
does not prompt. `--json` emits exactly one document with `schemaVersion: 1`,
`command`, and `result`. Session results contain a `sessions` array; each entry
has `sessionId`, `revision`, stable lowercase `state`, `input`, `title`, and
`remainingMilliseconds`, `elapsedMilliseconds`, and `totalMilliseconds`.
Unavailable time values are `null`. Foreground results also carry `outcome`:
`expired`, `stopped`, or `dismissed`. List order and successful bulk result
order are ascending by ID. `version --json` returns a `version` string.

`--plain` emits a header followed by tab-separated session rows:

```text
sessionId  state  title  input  remainingMilliseconds  elapsedMilliseconds  totalMilliseconds  revision
```

The actual separators are tabs. Cells escape backslashes, tabs, newlines,
carriage returns, and other control characters. Numbers use invariant decimal
formatting. Help remains conventional human-readable text. Notification and
audio subprocess diagnostics are captured by the service boundary so they do
not add raw terminal output to JSON or plain results.

Expected failures write one structured JSON error document to stderr in JSON
mode, with `schemaVersion`, `command`, and `error` containing numeric `code`,
application `kind`, and human-readable `message`. In other modes the error
message goes to stderr. Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Unexpected internal failure |
| 2 | Usage or validation error |
| 3 | Unknown session |
| 4 | Runtime unavailable or recovery records require GUI handling |
| 5 | Transport error |
| 6 | Unsupported operation |
| 7 | Persistence failure |
| 8 | Locked session, invalid transition, or conflict |
| 130 | Foreground interruption |

`pause`, `resume`, and `stop` with `all` validate the entire live set before
changing anything. An ineligible session produces exit 8 and identifies the
offending ID. An empty live set succeeds with an empty result.

`hourglass-linux` continues to launch or activate the GUI and retains its
existing timer-expression launch syntax.
