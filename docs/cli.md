# Hourglass CLI (DEV-18 Stage G)

Build with `dotnet build Hourglass.Linux.sln -c Release`; the executable in
`src/Hourglass.Cli/bin/Release/net10.0/` is `hourglass`. For a development run:

```sh
dotnet run --project src/Hourglass.Cli -- start "25 minutes" --title "Focus"
dotnet run --project src/Hourglass.Cli -- list --json
```

`hourglass` with no arguments prints help. The standalone CLI accepts:

```text
hourglass start <expression...> [-t|--title <title>] [--set key=value ...] [--wait]
hourglass <expression...> [-t|--title <title>]
hourglass list
hourglass status <session-id>
hourglass pause|resume|stop <session-id|all>
hourglass restart|dismiss <session-id>
hourglass update <session-id> [--input <expression>] [--title <title>] [--revision <number>] [--set key=value ...]
hourglass unlock <session-id>
hourglass saved list|clear
hourglass saved add <expression...> [--title <title>] [--name <name>] [--set key=value ...]
hourglass saved run <name-or-id>|--all [--wait]
hourglass saved remove <name-or-id>
hourglass recent list|clear
hourglass config list|get <key>|set <key> <value>
hourglass sounds list|preview <sound-id>
hourglass doctor|about|gui
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

The CLI connects to an active GUI or terminal runtime using versioned local
control. Commands from another shell operate on the same session IDs. With no
owner, a foreground start owns a local runtime; queries remain short transactions
and do not restore saved sessions. Preserved GUI recovery records still require
the GUI before a terminal can become the owner.

The owning process remains responsible for runtime lifetime during Stage G.
Its exit disconnects attached clients; the command outcome can be uncertain if a
response was lost. Mutations are never automatically replayed. Reopen the
frontend to reconnect explicitly. The on-demand host and detached lifetime arrive
in Stage H; terminal release tarballs arrive in Stage I.

All information commands accept `--json` or `--plain`, including before the
command. The flags are exclusive. Successful data goes to stdout. The CLI
does not prompt. `--json` emits exactly one document with `schemaVersion: 1`,
`command`, and `result`. Session results contain a `sessions` array; each entry
has `sessionId`, `revision`, stable lowercase `state`, `input`, `title`, and
`remainingMilliseconds`, `elapsedMilliseconds`, and `totalMilliseconds`.
Results also include `progressPercent`, effective `options`, and
`preferences`.
Unavailable time values are `null`. Foreground results also carry `outcome`:
`expired`, `stopped`, or `dismissed`. List order and successful bulk result
order are ascending by ID. `version --json` returns a `version` string.

`--plain` emits a header followed by tab-separated session rows:

```text
sessionId  state  title  input  remainingMilliseconds  elapsedMilliseconds  totalMilliseconds  progressPercent  revision
```

The actual separators are tabs. Cells escape backslashes, tabs, newlines,
carriage returns, and other control characters. Numbers use invariant decimal
formatting. Help remains conventional human-readable text. Notification and
audio subprocess diagnostics are captured by the service boundary so they do
not add raw terminal output to JSON or plain results.

Saved, recent, config, sound, and diagnostic results have dedicated `result`
fields in JSON. Plain catalog rows are tab-separated and escape control
characters. Best-effort service diagnostics are separate JSON diagnostic lines
on stderr in JSON mode; they never enter the one stdout result document.

Saved selectors try exact ID first, then an unambiguous display name/header.
Ambiguous matches list their IDs and return 8. `saved run --all` starts every
template concurrently, waits for all of them, and stops only that batch on
interruption. A looping template keeps the foreground command running.
Existing saved-template options override current defaults when run.

`config` edits future-session defaults. `update --set` edits one live session
at its current revision or the supplied `--revision`; conflicts return 8.
Shared keys are `notifications-enabled`, `audio-alerts-enabled`,
`audio-alert-sound-id`, `prompt-on-exit`, `reverse-progress-bar`,
`show-time-elapsed`, `loop-timer`, `loop-sound`, `close-when-expired`,
`lock-interface`, and `do-not-keep-computer-awake`. Boolean values are
`true`/`false`. GUI-only preferences retain their existing values.
`doctor` inspects local prerequisites without playing audio, notifying, or
starting a timer; a reported prerequisite does not prove a desktop service
works. `gui` starts the normal GUI executable, which handles activation.

Expected failures write one structured JSON error document to stderr in JSON
mode, with `schemaVersion`, `command`, and `error` containing numeric `code`,
application `kind`, and human-readable `message`. In other modes the error
message goes to stderr. Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Unexpected internal failure |
| 2 | Usage or validation error |
| 3 | Unknown session or saved timer |
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
