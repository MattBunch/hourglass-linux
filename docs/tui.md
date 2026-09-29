# Hourglass TUI (DEV-18 Stage E)

Build the modern solution in Release, then run `src/Hourglass.Tui/bin/Release/net10.0/hourglass-tui`. `hourglass tui` launches the same interface from the CLI build. `--help` and `--version` work without a terminal.

The TUI shows live timer snapshots from the shared application runtime. At 80×24 or larger it displays a session list and timer detail; smaller terminals show one timer at a time. Below 40×12 it asks for a resize and keeps help and quit available. Tab and Shift+Tab select timers. The default refresh is 5 Hz; it does not change countdown timing.

| Key | Dashboard action |
| --- | --- |
| `n` | New timer |
| `e` | Edit selected timer |
| Enter | Start stopped timer |
| Space or Ctrl+P | Pause or resume |
| `s` or Ctrl+S | Stop |
| `r` or Ctrl+R | Restart |
| Tab / Shift+Tab | Select next / previous timer |
| Escape | Dismiss eligible stopped or expired timer |
| `u` | Unlock selected timer |
| `?` or F1 | Help |
| `q` or Ctrl+Q | Quit |

In the new/edit screen, Tab moves between expression and title; Enter submits and Escape cancels. Printable dashboard shortcuts are inactive while typing. Editing a title leaves the timer running. If another command changes the session while the editor is open, `l` reloads current values and `c` retains the draft.

The TUI acquires the same per-user runtime authority as the GUI and CLI. If another runtime is active, it exits 4. Existing GUI recovery sessions are preserved and must be handled in the GUI before this interim TUI starts. On ordinary quit it closes only timers created by this TUI; running or paused timers follow the existing prompt-on-exit preference. SIGTERM/Ctrl+C closes owned timers and returns 130. A save failure returns 7 after terminal restoration. An unexpected exception returns 1 and leaves recovery checkpoints for the GUI.

Saved timers, recents, and settings screens are Stage F. Cross-process control and detached timer ownership are Stage G/H. The Stage E binary is a development build; terminal release tarballs arrive in Stage I.

The bounded PTY smoke test is `python3 tests/Hourglass.Tui.Tests/verify-terminal.py` after a Release build. It checks keyboard workflows, resize, interruption, exception cleanup, and terminal mode restoration. Attended terminal emulators, SSH, screen readers, and physical desktop services require separate validation.
