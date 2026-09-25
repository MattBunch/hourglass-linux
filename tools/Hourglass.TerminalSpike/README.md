# DEV-18 toolkit spike

Isolated feasibility checks using System.CommandLine 2.0.0 and Terminal.Gui 2.5.0 on .NET 10. This tool has no Hourglass domain dependencies and is not a distributed frontend.

```sh
dotnet build tools/Hourglass.TerminalSpike -c Release -warnaserror
dotnet run --project tools/Hourglass.TerminalSpike -c Release --no-build -- --verify-parser
python3 tools/Hourglass.TerminalSpike/verify-terminal.py
dotnet format tools/Hourglass.TerminalSpike/Hourglass.TerminalSpike.csproj --verify-no-changes --no-restore
```

The parser check verifies injected stdout/stderr, invalid arguments, successful invocation and cancellation returning 130. Cancellation is driven by a token and completion source, without sleeping.

The Linux PTY harness supplies input and resize events with a 15-second process deadline. It checks normal runs at 4/5/10 Hz, an intentional exception at 10 Hz (exit 70), and typing dashboard shortcut letters into an editor. It checks OS terminal attributes and emitted cursor, mouse-reporting and alternate-screen mode restoration independently of the evidence flag. Rates are requested repaint rates, not measured real-time guarantees.

## Results — 2026-09-26

All parser and PTY scenarios passed. Dashboard delivery includes Space, Escape, Ctrl+P/S/R, Tab, Shift+Tab, Enter and q. Editor text `qsren tea` survives unchanged; Ctrl+Q exits. Both 80x24 and 40x12 sizes are observed.

Legacy terminal bytes Escape + carriage return (Alt+Enter) arrive as `Ctrl+Alt+M`. Plain carriage return arrives as `Enter`. The TUI adapter should recognize the former as an Alt+Enter alias only in the relevant command context; keep ordinary Enter available. Do not globally intercept printable keys in text controls.

The recovered earlier tmux run also showed this Alt+Enter encoding. These automated PTY checks do not establish attended terminal-emulator, SSH, screen-reader or desktop/hardware behavior. Those remain Not run.
