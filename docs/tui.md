# Hourglass TUI (DEV-18 Stage H)

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
| `d` | Confirm detachment of selected GUI/TUI timer |
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

The TUI connects to GUI authority or starts an on-demand host. Ordinary quit closes
only its non-detached timers using the existing prompt preference; observed and
detached sessions remain alive. Ctrl+C/SIGTERM closes ordinary owned timers and
returns 130. A save failure returns 7 after terminal restoration; unexpected
exceptions return 1. Connection loss closes ordinary TUI-owned sessions.

Authority loss is displayed, disables mutations and permits quit. Reopen the TUI
to reconnect. Connection errors return 5 and incompatible protocols return 6.
Terminal startup preserves GUI recovery records until shared GUI initialization.
Terminal release tarballs arrive in Stage I.

The bounded PTY smoke test is `python3 tests/Hourglass.Tui.Tests/verify-terminal.py` after a Release build. It checks keyboard workflows, resize, interruption, exception cleanup, and terminal mode restoration. Attended terminal emulators, SSH, screen readers, and physical desktop services require separate validation.

## Session lifetime (Stage H)

The TUI attaches to GUI authority or an on-demand host. Dashboard `d` confirms
detachment of the selected GUI/TUI timer; the detail and session list show its
lifetime. Detached and observed sessions survive TUI quit. Exit prompts and cleanup
apply only to the TUI’s ordinary sessions, including cleanup after connection loss.
Detachment is one-way and preserves countdown state; locked timers require unlocking.
See [runtime control](linux-port/runtime-control.md) for host and crash recovery rules.
