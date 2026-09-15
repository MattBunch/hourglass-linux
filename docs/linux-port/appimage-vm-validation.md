# AppImage VM Validation Runbook

Use this runbook to validate the published `v0.2.0-beta.9` AppImage in a
clean, attended GNOME Wayland VM. It applies to Ubuntu, Fedora, and Arch.
It creates evidence files only; it does not mark a release-checklist row as
passed. Record the observed results in
[`release-validation-checklist.md`](release-validation-checklist.md).

## 1. Download and verify the release candidate

Run this in a terminal inside the VM. It deliberately uses a directory with a
space in its name.

```bash
set -euo pipefail

release=v0.2.0-beta.9
asset=Hourglass-0.2.0-beta.9-x86_64.AppImage
evidence_root="$HOME/Hourglass AppImage Validation/$release"

mkdir -p "$evidence_root"
cd "$evidence_root"

curl --fail --location --remote-name \
  "https://github.com/MattBunch/hourglass-linux/releases/download/$release/$asset"
curl --fail --location --remote-name \
  "https://github.com/MattBunch/hourglass-linux/releases/download/$release/SHA256SUMS"

sha256sum --check --ignore-missing SHA256SUMS
chmod +x "$asset"
```

If the VM has no FUSE support, use `--appimage-extract-and-run` in the launch
command below. Do not install a separate .NET runtime: the AppImage is meant to
run self-contained.

## 2. Capture environment evidence and launch

```bash
{
  echo '=== date ==='
  date --iso-8601=seconds
  echo '=== OS ==='
  cat /etc/os-release
  echo '=== desktop ==='
  gnome-shell --version || true
  printf 'XDG_SESSION_TYPE=%s\n' "$XDG_SESSION_TYPE"
  printf 'XDG_CURRENT_DESKTOP=%s\n' "$XDG_CURRENT_DESKTOP"
  printf 'WAYLAND_DISPLAY=%s\n' "$WAYLAND_DISPLAY"
  printf 'DISPLAY=%s\n' "$DISPLAY"
  echo '=== .NET runtime ==='
  if command -v dotnet >/dev/null 2>&1; then dotnet --info; else echo 'dotnet: not installed'; fi
} | tee environment.txt

HOURGLASS_STARTUP_DIAGNOSTICS=1 \
  ./Hourglass-0.2.0-beta.9-x86_64.AppImage \
  > hourglass-startup.log 2>&1 &
app_pid=$!
printf 'HOURGLASS_PID=%s\n' "$app_pid" | tee launch.txt

sleep 5
grep -E 'stage=(CoordinatorCreated|MainWindowOpened|MainWindowActivated)' \
  hourglass-startup.log | tee -a launch.txt || true
ps -fp "$app_pid" | tee -a launch.txt
```

If the normal launch reports a FUSE error, replace the launch command with:

```bash
HOURGLASS_STARTUP_DIAGNOSTICS=1 \
  ./Hourglass-0.2.0-beta.9-x86_64.AppImage --appimage-extract-and-run \
  > hourglass-startup.log 2>&1 &
app_pid=$!
```

## 3. Perform the attended checks

Use the visible Hourglass window. For every test, write `Pass`, `Fail`,
`Unsupported`, or `Skipped` and a short observation in `manual-results.txt`.

```bash
: > manual-results.txt
```

1. Start a 10-second timer; pause, resume, and let it expire.
2. Confirm the completion notification and bundled sound.
3. Stop a running timer and start it again.
4. Test both a duration input and an absolute-time input.
5. Minimize, restore, and restart the app to confirm settings persistence.
6. Enable always-on-top, switch focus to another app, and confirm the observed
   compositor behavior.
7. At expiry, record whether the window receives completion attention.
8. While a timer runs, inspect `systemd-inhibit --list`; stop the timer and record
   whether the Hourglass inhibition entry clears.
9. Start a second copy of the same AppImage. Record whether it hands off to the
   first instance rather than leaving two independent windows/processes.
10. Close the app normally and confirm `ps -fp "$app_pid"` no longer finds the
    original process.
11. Record dock/taskbar progress and status-icon behavior, including whether
    stock GNOME provides the necessary capability. A missing platform capability
    is `Unsupported`, not `Fail`.

The AppImage embeds desktop metadata, but a downloaded AppImage does not
automatically install a desktop launcher. Record desktop-file launch and
installed icon identity as `Skipped` unless the VM explicitly installs and
launches the packaged desktop entry using a supported AppImage integration
method.

## 4. Preserve the evidence

```bash
{
  echo '=== final process state ==='
  ps -fp "$app_pid" || true
  echo '=== startup log ==='
  cat hourglass-startup.log
} > final-state.txt

tar -czf hourglass-appimage-validation-evidence.tar.gz \
  environment.txt launch.txt hourglass-startup.log final-state.txt manual-results.txt
```

Attach or transcribe the evidence into the matching row of the release
validation checklist. Do not change the row's aggregate result to `Pass` until
all required core checks for that environment have been observed.
