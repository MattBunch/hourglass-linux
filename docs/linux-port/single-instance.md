# Linux Single-Instance Behavior

Phase 12 adds Linux MVP single-instance startup through `Hourglass.Platform.ISingleInstanceService`.

## Boundary

`Hourglass.Linux.Avalonia` asks `ISingleInstanceService` for ownership before Avalonia is initialized. If ownership is acquired, the normal desktop lifetime starts. If another process already owns the lock, the second launch exits cleanly with code `0`.

The Linux implementation lives in `Hourglass.Linux.Services` and keeps filesystem locking out of the core timer logic.

## Lock Path

The lock file path is per Linux user and is resolved in this order:

1. `$XDG_RUNTIME_DIR/hourglass-linux/hourglass-linux.lock` when `XDG_RUNTIME_DIR` is non-empty and absolute.
2. `$XDG_CACHE_HOME/hourglass-linux/hourglass-linux.lock` when `XDG_CACHE_HOME` is non-empty and absolute.
3. `~/.cache/hourglass-linux/hourglass-linux.lock` when a home directory is available.

Relative XDG paths are ignored. If no safe per-user path can be resolved, startup writes a concise error to stderr and exits with code `1`.

## Ownership

Ownership is an advisory one-byte file-region lock held by an open file handle for the complete desktop lifetime. The lock file may remain after exit; file existence is not ownership.

The file is intentionally not deleted during disposal. On Unix, deleting a locked path can let another process create and lock a new inode while the first process still owns the old one.

A stale lock file is harmless because the operating-system lock is released when the owning process exits or crashes.

## Scope And Limitations

- Single-instance behavior is per user, not machine-wide.
- The mechanism is independent of GNOME, KDE Plasma, XFCE, Wayland, and X11.
- A secondary launch does not forward command-line arguments.
- A secondary launch does not raise, focus, or activate the existing window.
- Native developer and AppImage builds use the host XDG lock namespace.
- Flatpak builds may use a sandbox-specific namespace, so native/AppImage and Flatpak builds may run at the same time.

## Troubleshooting

Expected lock-file locations:

```bash
echo "$XDG_RUNTIME_DIR/hourglass-linux/hourglass-linux.lock"
echo "$XDG_CACHE_HOME/hourglass-linux/hourglass-linux.lock"
echo "$HOME/.cache/hourglass-linux/hourglass-linux.lock"
```

The lock file can remain after Hourglass exits. Do not treat the file alone as evidence that Hourglass is running.
