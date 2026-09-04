# Changelog

All notable changes to Hourglass Linux are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0-beta.8] - 2026-09-04

### Fixed

- Defer Unity LauncherEntry desktop-progress discovery to a background task
  after the first progress request, so D-Bus setup cannot block window startup.

## [0.2.0-beta.7] - 2026-09-04

### Added

- Add per-service startup diagnostics to isolate blocked Linux desktop startup.

## [0.2.0-beta.6] - 2026-09-04

### Added

- Add opt-in stderr startup diagnostics for investigating Linux desktop launch
  failures.

## [0.2.0-beta.5] - 2026-09-01

### Fixed

- Select framebuffer software rendering for X11 without a native DRI3
  preflight probe.

## [0.2.0-beta.4] - 2026-08-26

### Fixed

- Probe X11 DRI3 availability with a standalone XCB connection so the probe
  cannot disrupt Avalonia's application display connection.

## [0.2.0-beta.3] - 2026-08-24

### Fixed

- Select framebuffer software rendering when the current X server does not
  support DRI3, rather than attempting an unusable accelerated renderer.

## [0.2.0-beta.2] - 2026-08-24

### Fixed

- Prefer EGL and GLX rendering while retaining an explicit software fallback
  for Linux virtual-machine environments where accelerated rendering is
  unavailable.

## [0.2.0-beta.1] - 2026-08-16

### Added

- First public beta distribution through GitHub Releases, including a
  self-contained `linux-x64` archive and `SHA256SUMS`.
- Release archives bundle the applicable project, .NET runtime, HarfBuzz, and
  Skia license notices, and omit portable PDB debug symbols.

### Known limitations

- The final AppImage, production Flatpak/Flathub package, and other
  package-manager channels are not available yet.
- Attended cross-distribution validation and physical multi-monitor and
  wake-from-suspend coverage remain in progress.

## [0.1.0] - 2026-07-18

### Added

- Native Linux Avalonia port of the Hourglass countdown timer.
- Self-contained `linux-x64` publish and AppDir prototype, including desktop
  metadata, icons, AppStream metadata, and bundled alert sounds.

### Known limitations

- No final AppImage, public release pipeline, or production Flatpak/Flathub
  package is available yet.
- Attended desktop integration coverage remains incomplete.
- Physical multi-monitor and wake-from-suspend validation remain unavailable.
