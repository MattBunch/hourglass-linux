# README Demo Recorder

## Architecture

The README demo recorder is a dedicated .NET tool in `tools/Hourglass.DemoRecorder`. It runs a deterministic C# scenario against the real Hourglass Avalonia `MainWindow` and `MainWindowViewModel`, renders numbered PNG frames with Avalonia headless and Skia, then invokes FFmpeg to create `docs/assets/demo.gif` and `docs/assets/demo.mp4`.

The default command is:

```bash
./scripts/record-readme-demo.sh
```

## Why not Node.js?

The Digital Rain project uses Node.js because it is a browser application and Playwright directly automates Chromium.

Hourglass is a native Avalonia desktop application. A Node.js helper would mostly supervise `dotnet` and `ffmpeg`, and would not provide a reliable cross-desktop Linux UI automation layer.

C# provides direct access to the real application, reuse of view models and services, deterministic clock injection, Avalonia headless APIs, fewer repository toolchains, and compile-time integration.

## Scenario Timeline

The `readme` scenario lasts 16 seconds at 12 FPS. It shows the idle window, enters `10 sec` with title `Demo timer`, starts through the existing start command path, shows countdown, pauses, resumes, opens the real context menu and toggles always-on-top, advances to completion, then opens the real About window.

## Deterministic Clock

`DemoClock` implements `IMonotonicClock` and carries a fixed wall-clock instant. The scenario advances the clock by exact frame steps, calls the view model tick path, flushes the Avalonia dispatcher, and captures a frame. The visible timer does not depend on wall-clock waits.

Production code continues to use `SystemMonotonicClock` and `DateTime.Now`.

## Service Isolation

The recorder uses in-memory or recording implementations for settings, notifications, audio, session inhibition, desktop progress, status icon integration, external URL launching, and power actions. These services record that an effect would have happened without touching DBus, browsers, audio devices, active-session files, or the user's normal config directory.

The wrapper also sets an isolated temporary `XDG_CONFIG_HOME` as an additional safety boundary.

## Headless Rendering

The recorder configures Avalonia headless with Skia drawing enabled. Frames are captured from the active top-level window. In the current implementation, the About scene is captured as its own top-level frame. Context-menu opening is attempted headlessly; if a compositor or Avalonia headless behavior does not include popups in the captured frame, the scenario still records the corresponding option change on the real view model.

This limitation is intentionally documented rather than hidden. A future improvement can composite all active visual roots if Avalonia exposes stable popup-root positions for this app.

## FFmpeg

FFmpeg remains an external command-line dependency. `FfmpegEncoder` uses `ProcessStartInfo.ArgumentList`, captures stdout and stderr, fails on non-zero exit, and verifies that expected output files exist and are non-empty.

GIF output uses a two-pass palette workflow. MP4 output prefers `libx264` H.264 with `yuv420p` and fast-start metadata. Some Fedora FFmpeg builds omit `libx264`; when FFmpeg reports that encoder is unavailable, the recorder falls back to `libopenh264` so the one-command workflow still creates an H.264 MP4.

## Adding Another Scenario

Add a new `IDemoScenario` implementation under `tools/Hourglass.DemoRecorder/Scenarios`, keep it expressed in user-visible `DemoContext` operations, then register it in `Program.CreateScenario`. Add tests for expected frame count and side-effect isolation.

## Troubleshooting

If FFmpeg is missing, install it and rerun:

```bash
sudo dnf install ffmpeg
```

Use `--keep-frames` to preserve generated PNGs under `.tmp/readme-demo/frames` for inspection. Use `--frames-dir <path>` to write frames elsewhere.

If headless rendering fails in a restricted environment, run the unit tests first to separate recorder logic failures from native rendering availability:

```bash
dotnet test tests/Hourglass.DemoRecorder.Tests/Hourglass.DemoRecorder.Tests.csproj --configuration Release
```

## Native Desktop Fallback

A native desktop recording mode should remain optional. Wayland capture is compositor- and permission-dependent, so the default README workflow should not require GNOME screen recording, Xvfb, or desktop automation unless headless rendering becomes insufficient for an essential visual.
