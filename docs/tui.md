# Hourglass TUI (DEV-18 Stage G)

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
| `v` | Saved timers |
| `c` | Recent inputs |
| `,` | Shared settings |
| `o` | Selected-session options |
| `?` or F1 | Help |
| `q` or Ctrl+Q | Quit |

In the new/edit screen, Tab moves between expression and title; Enter submits and Escape cancels. Printable dashboard shortcuts are inactive while typing. Editing a title leaves the timer running. If another command changes the session while the editor is open, `l` reloads current values and `c` retains the draft.

In saved timers, Enter runs the selected template, `A` runs all, `a` opens a
draft to add a template, `d` asks to remove the selection, and `x` asks to
clear the catalog. Recent inputs open a new-timer draft with Enter; `x` asks
to clear the list. Settings and selected-session options use Up/Down or Tab
to select a key, Enter to toggle/cycle its value, and `p` to preview the
chosen sound. Session edits use the latest revision and report conflicts.
Escape returns to the dashboard. Service errors remain visible in status.

The TUI connects to an active GUI/terminal authority, or acquires the same
per-user authority lock to own a local runtime. Observed sessions remain alive
when this TUI quits. Ordinary quit closes only TUI-created timers, using the
existing prompt-on-exit preference. Ctrl+C/SIGTERM closes owned timers and returns
130. A save failure returns 7 after terminal restoration; an unexpected exception
returns 1 and preserves recovery checkpoints.

Owner exit disconnects attached frontends. The TUI displays the disconnection,
disables mutations, and permits quit; reopen it to reconnect. Connection errors
return 5 and incompatible protocols return 6. With no authority, existing GUI
recovery sessions still require the GUI before the TUI can become the owner.
The on-demand host and detached lifetime arrive in Stage H. Terminal release
tarballs arrive in Stage I.

The bounded PTY smoke test is `python3 tests/Hourglass.Tui.Tests/verify-terminal.py` after a Release build. It checks keyboard workflows, resize, interruption, exception cleanup, and terminal mode restoration. Attended terminal emulators, SSH, screen readers, and physical desktop services require separate validation.
