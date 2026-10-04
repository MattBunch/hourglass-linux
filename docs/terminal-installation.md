# Terminal installation

DEV-18 adds self-contained terminal archives for glibc-based Linux x64 and arm64.
The release workflow is prepared to publish them with a future tagged release;
this feature branch does not make them available on an existing release.

Choose `hourglass-terminal-<version>-linux-x64.tar.gz` for x86-64 or
`hourglass-terminal-<version>-linux-arm64.tar.gz` for AArch64. Verify the release
`SHA256SUMS`, extract the archive into a dedicated directory, and add its `bin`
directory to PATH. Keep `bin`, `lib`, `licenses` and `docs` together. The launchers
also work through symlinks and from paths containing spaces. No installed .NET
runtime is required. The internal host is not a public command or system service.

For example, after obtaining an archive and its checksum file:

```sh
sha256sum --check SHA256SUMS
mkdir -p "$HOME/.local/opt/hourglass-terminal"
tar -xzf hourglass-terminal-<version>-linux-x64.tar.gz -C "$HOME/.local/opt/hourglass-terminal"
export PATH="$HOME/.local/opt/hourglass-terminal/bin:$PATH"
hourglass version
hourglass start 25m --detach --title Focus
hourglass list --plain
hourglass tui --accessible
```

Use a fresh installation directory for upgrades, verify it, then update PATH or
your launcher symlinks. Close ordinary terminal views before removing an old
installation; detached sessions can keep its host process alive until dismissed.
Do not delete shared XDG configuration when replacing binaries.

## Requirements and optional services

The archive supplies .NET and terminal package dependencies, including native
assets. The OS still needs the .NET 10 Linux runtime prerequisites: glibc,
libgcc, libstdc++, zlib, OpenSSL and ICU. Packages are native glibc builds, not
musl/Alpine binaries. The launchers use POSIX `sh` and `readlink`.

A full-screen TUI requires an interactive xterm-compatible terminal. Use
`hourglass --help`, `--plain` or `--json` for noninteractive use and accessibility
fallbacks. `--accessible` reduces TUI refresh to 1 Hz and uses monochrome output;
`--no-color` or nonempty `NO_COLOR` uses monochrome at the normal 5 Hz rate.

Desktop notifications, sound playback and suspend inhibition depend on the
existing Linux service backends and session availability. Missing capabilities
are reported by `hourglass doctor`; terminal operation does not require a desktop.

The GUI is installed separately as `hourglass-linux`. `hourglass gui` finds it
through existing sibling/development/PATH discovery. Terminal archives contain
no Avalonia assemblies. Native GUI and terminal installations share the same
XDG settings and per-user authoritative runtime. Flatpak can use a different
filesystem/socket namespace; do not assume host-shell control of a sandboxed GUI.

See [CLI](cli.md), [TUI](tui.md), and [runtime ownership](linux-port/runtime-control.md).
