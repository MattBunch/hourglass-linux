# Linux Single-Instance Behavior

Phase 12 added Linux MVP single-instance startup through `Hourglass.Platform.ISingleInstanceService`. Milestone 7 extends it with per-user command handoff and existing-window activation.

## Boundary

`Hourglass.Linux.Avalonia` asks `ISingleInstanceService` for ownership before Avalonia is initialized. If ownership is acquired, the normal desktop lifetime starts and the process listens for launch requests. If another process already owns the lock, the second launch validates its local command line, sends a structured request to the running instance, and exits.

The Linux implementation lives in `Hourglass.Linux.Services` and keeps filesystem locking out of the core timer logic.

## Lock Path

The lock file path is per Linux user and is resolved in this order:

1. `$XDG_RUNTIME_DIR/hourglass-linux/hourglass-linux.lock` when `XDG_RUNTIME_DIR` is non-empty and absolute.
2. `$XDG_CACHE_HOME/hourglass-linux/hourglass-linux.lock` when `XDG_CACHE_HOME` is non-empty and absolute.
3. `~/.cache/hourglass-linux/hourglass-linux.lock` when a home directory is available.

Relative XDG paths are ignored. If no safe per-user path can be resolved, startup writes a concise error to stderr and exits with code `1`.

The handoff socket uses the same directory and replaces the filename with `hourglass-linux.sock`.

## Ownership

Ownership is an advisory one-byte file-region lock held by an open file handle for the complete desktop lifetime. The lock file may remain after exit; file existence is not ownership.

The file is intentionally not deleted during disposal. On Unix, deleting a locked path can let another process create and lock a new inode while the first process still owns the old one.

A stale lock file is harmless because the operating-system lock is released when the owning process exits or crashes.

## Command Handoff

The owner process removes any stale socket file only after it owns the lock, then binds a Unix domain socket. Secondary launches never delete the socket file. Requests are local to the same user/session namespace; no TCP listener is opened.

Supported command forms:

```bash
hourglass
hourglass "10 minutes"
hourglass --title "Tea" "5 minutes"
hourglass -t "Tea" 5 minutes
```

No arguments requests activation of the most relevant existing timer window. Timer arguments are parsed with the same core timer parser used by the UI. A valid timer launch creates a new independent timer window in the existing process and starts it. `--title` and `-t` set the new timer title.

Malformed local command lines, such as missing title values, duplicate title options, unknown switches, or invalid timer expressions, return a non-zero exit code before any handoff. IPC delivery failures also return a non-zero exit code with a concise stderr message.

## Scope And Limitations

- Single-instance behavior is per user, not machine-wide.
- The mechanism is independent of GNOME, KDE Plasma, XFCE, Wayland, and X11.
- A secondary launch forwards activation or timer-start requests to the existing process.
- A secondary launch can request show/restore/activation, but compositor focus behavior remains best-effort.
- The Linux command-line surface currently supports timer input and optional title only; it does not port the full legacy Windows switch set.
- Native developer and AppImage builds use the host XDG lock namespace.
- Flatpak builds may use a sandbox-specific namespace, so native/AppImage and Flatpak builds may run at the same time.

## Troubleshooting

Expected lock-file locations:

```bash
echo "$XDG_RUNTIME_DIR/hourglass-linux/hourglass-linux.lock"
echo "$XDG_RUNTIME_DIR/hourglass-linux/hourglass-linux.sock"
echo "$XDG_CACHE_HOME/hourglass-linux/hourglass-linux.lock"
echo "$XDG_CACHE_HOME/hourglass-linux/hourglass-linux.sock"
echo "$HOME/.cache/hourglass-linux/hourglass-linux.lock"
echo "$HOME/.cache/hourglass-linux/hourglass-linux.sock"
```

The lock or socket file can remain after Hourglass exits. Do not treat either path alone as evidence that Hourglass is running.
