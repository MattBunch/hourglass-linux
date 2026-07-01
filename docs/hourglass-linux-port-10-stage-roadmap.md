# Hourglass Linux Port Roadmap

This document expands the initial 10-point implementation plan for porting Hourglass to Linux. It assumes the project has just been forked and that the first objective is to create a safe, maintainable Linux-port path without breaking or rewriting the existing Windows/WPF application.

## Guiding Principles

- Keep the original Windows/WPF `.NET Framework 4.8` app intact during early port work.
- Build a separate modern Linux solution beside the legacy solution.
- Use C#/.NET and Avalonia for the native Linux UI path.
- Target `.NET 10` for new Linux projects, assuming the .NET 10 SDK is installed.
- Extract reusable, platform-neutral logic before porting UI.
- Treat Linux notifications as required, tray support as optional, and wake-from-suspend as post-MVP.
- Prefer small, reviewable stages over one large automated rewrite.
- Keep every stage buildable or clearly document why it cannot build yet.

---

## Stage 1: Scaffold the Linux Port Architecture

### Goal

Create a separate modern Linux solution that can evolve independently from the original Windows app.

### Work Items

- Add `Hourglass.Linux.sln`.
- Add new SDK-style projects:
  - `src/Hourglass.Core`
  - `src/Hourglass.Platform`
  - `src/Hourglass.Linux.Services`
  - `src/Hourglass.Linux.Avalonia`
  - `tests/Hourglass.Core.Tests`
- Add scoped `Directory.Build.props` files under `src/` and `tests/`.
- Add placeholder service interfaces.
- Add a minimal Avalonia app window showing `Hourglass Linux Port`.
- Add one placeholder passing unit test.
- Add documentation:
  - `README-LINUX-PORT.md`
  - `docs/linux-port/architecture.md`
  - `docs/linux-port/roadmap.md`
  - `docs/linux-port/feature-parity.md`
  - `docs/linux-port/decisions.md`
  - `docs/linux-port/risks.md`

### Do Not Touch

- `Hourglass.sln`
- `Hourglass/Hourglass.csproj`
- `Hourglass.Test/Hourglass.Test.csproj`
- `Hourglass.Setup/*`
- `Hourglass.Bundle/*`
- `hourglass-linux-deep-research-report.md`

### Acceptance Criteria

- Existing Windows project remains unchanged.
- New Linux scaffold exists beside the original solution.
- New projects target `net10.0`.
- If .NET 10 is missing locally, build/test status is reported as blocked rather than downgraded.
- Documentation clearly states what is intentionally not implemented yet.

### Suggested Branch

```bash
git checkout -b linux-port/scaffold
```

### Suggested Commit Message

```text
Scaffold native Linux port architecture
```

---

## Stage 2: Inventory Reusable Source Files

### Goal

Identify which parts of the original Hourglass codebase can be safely reused in the Linux port.

### Work Items

- Review current source folders and categorize files into:
  - Platform-neutral candidates
  - Windows/WPF-specific code
  - Installer/update-specific code
  - Code needing refactor before reuse
- Produce a source inventory document:
  - `docs/linux-port/source-inventory.md`
- Identify parser, tokenizer, serialization, timer-state, settings, theme, and model code that can move into `Hourglass.Core`.
- Identify Windows-specific dependencies:
  - WPF windows
  - `DispatcherTimer`
  - WinForms `NotifyIcon`
  - Windows power APIs
  - WiX setup
  - Windows update manager
  - Windows single-instance startup logic

### Acceptance Criteria

- No source files are moved yet unless clearly safe.
- Inventory document lists recommended extraction order.
- Every reusable candidate has a risk level: low, medium, or high.
- Windows-only areas are explicitly marked as non-reusable or replacement-required.

### Suggested Branch

```bash
git checkout -b linux-port/source-inventory
```

### Suggested Commit Message

```text
Document reusable source inventory for Linux port
```

---

## Stage 3: Extract Parser and Token Logic into `Hourglass.Core`

### Goal

Move the safest platform-neutral logic into the new core library first.

### Work Items

- Extract or copy parser/token logic into `Hourglass.Core`.
- Preserve original behavior.
- Avoid UI and Windows dependencies.
- Add modern tests in `Hourglass.Core.Tests`.
- Port existing parser tests from the legacy MSTest project where practical.
- Keep namespaces clean and future-facing.

### Candidate Areas

- Date/time parsing
- Time span parsing
- Token models
- Parser helpers
- Formatting helpers, if platform-neutral

### Acceptance Criteria

- `Hourglass.Core` builds without WPF/WinForms/Windows dependencies.
- Parser tests cover existing behavior.
- Legacy Windows project remains untouched unless a shared-code strategy is explicitly chosen.
- No Linux UI work is mixed into this stage.

### Suggested Branch

```bash
git checkout -b linux-port/extract-parser-core
```

### Suggested Commit Message

```text
Extract parser logic into Linux core library
```

---

## Stage 4: Add Modern Unit Tests for Parser Behavior

### Goal

Build confidence that extracted parsing behavior matches the original app.

### Work Items

- Add test coverage for common timer inputs:
  - `5 minutes`
  - `1 hour`
  - `1h 30m`
  - `90 seconds`
  - absolute time inputs, if supported by current Hourglass behavior
- Add edge-case tests:
  - empty input
  - malformed input
  - mixed units
  - whitespace/casing variants
  - invalid dates/times
- Compare with existing `Hourglass.Test` behavior where applicable.
- Add test naming conventions and folder organization.

### Acceptance Criteria

- Tests are deterministic.
- Tests run under `dotnet test Hourglass.Linux.sln`.
- Existing behavior is preserved unless a difference is documented.
- Parser behavior is documented enough for future UI integration.

### Suggested Branch

```bash
git checkout -b linux-port/parser-tests
```

### Suggested Commit Message

```text
Add parser coverage for Linux core
```

---

## Stage 5: Design and Implement a Monotonic `CountdownEngine`

### Goal

Replace the old WPF/`DateTime.Now`-driven timer model with a testable platform-neutral timer engine.

### Work Items

- Add an `IClock` abstraction:
  - wall-clock time for display
  - monotonic time for elapsed countdown truth
- Add `CountdownEngine`.
- Support:
  - start
  - pause
  - resume
  - reset
  - complete
  - remaining time
  - elapsed time
- Add tests for:
  - normal countdown
  - pause/resume
  - completion
  - wall-clock jumps
  - monotonic elapsed behavior
  - zero/negative durations
- Document design in `docs/linux-port/timer-engine.md`.

### Example Shape

```csharp
public interface IClock
{
    DateTimeOffset WallNow { get; }
    TimeSpan MonotonicNow { get; }
}
```

### Acceptance Criteria

- Timer logic is independent of Avalonia.
- Timer logic is independent of WPF `DispatcherTimer`.
- Tests prove wall-clock changes do not break countdown state.
- UI refresh is treated as presentation, not timer truth.

### Suggested Branch

```bash
git checkout -b linux-port/countdown-engine
```

### Suggested Commit Message

```text
Add monotonic countdown engine
```

---

## Stage 6: Create Minimal Avalonia Timer UI

### Goal

Turn the placeholder Avalonia app into a small working timer UI backed by `Hourglass.Core`.

### Work Items

- Add a simple main window with:
  - timer input field
  - start button
  - pause/resume button
  - reset button
  - remaining time display
  - completion state
- Connect UI to `CountdownEngine`.
- Use Avalonia timers only for UI refresh ticks.
- Keep styling minimal.
- Avoid full Hourglass visual parity in this stage.
- Add basic view model structure if appropriate.

### Acceptance Criteria

- User can start, pause, resume, and reset a timer.
- Countdown state comes from `Hourglass.Core`.
- UI tick does not own timing truth.
- App runs as a basic native Linux desktop app.
- No tray, notifications, packaging, or settings yet.

### Suggested Branch

```bash
git checkout -b linux-port/minimal-timer-ui
```

### Suggested Commit Message

```text
Build minimal Avalonia timer UI
```

---

## Stage 7: Add Notification Abstraction and Linux Implementation

### Goal

Notify the user when a timer completes using Linux-appropriate desktop notification behavior.

### Work Items

- Define `INotificationService` in `Hourglass.Platform`.
- Add a Linux implementation in `Hourglass.Linux.Services`.
- Prefer freedesktop-compatible notification paths.
- Support graceful fallback if notifications are unavailable.
- Add documentation for GNOME/KDE/XFCE behavior.
- Ensure notifications are not the only recoverable state; app UI should still show completed timers.

### Suggested Interface Shape

```csharp
public interface INotificationService
{
    Task NotifyTimerExpiredAsync(string title, string body, CancellationToken cancellationToken = default);
}
```

### Acceptance Criteria

- Timer completion can trigger notification service.
- Missing notification backend does not crash the app.
- Notification implementation is isolated from core timer logic.
- UI remains usable without notifications.

### Suggested Branch

```bash
git checkout -b linux-port/notifications
```

### Suggested Commit Message

```text
Add Linux notification service abstraction
```

---

## Stage 8: Add Settings Storage Abstraction

### Goal

Introduce platform-appropriate Linux settings storage without depending on the Windows settings model directly.

### Work Items

- Define settings models in `Hourglass.Core`.
- Define `ISettingsStore` or similar in `Hourglass.Platform`.
- Add Linux file-based settings storage.
- Use XDG-appropriate config/data paths where practical.
- Store:
  - recent timer inputs
  - user preferences
  - window state, if needed
  - notification preferences
  - theme preference later
- Do not carry over the Windows updater UUID behavior.
- Document privacy decisions.

### Suggested Interface Shape

```csharp
public interface ISettingsStore
{
    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}
```

### Acceptance Criteria

- App can persist and reload basic settings.
- Storage location is documented.
- No persistent tracking UUID is introduced.
- Settings layer is testable and replaceable.

### Suggested Branch

```bash
git checkout -b linux-port/settings-storage
```

### Suggested Commit Message

```text
Add Linux settings storage abstraction
```

---

## Stage 9: Add Keep-Awake / Session Inhibition Abstraction

### Goal

Support the Linux equivalent of “keep the machine awake while timers are running,” without using Windows power APIs.

### Work Items

- Define `ISessionInhibitor` in `Hourglass.Platform`.
- Add a Linux implementation path.
- Prefer portal/session-manager style inhibition where feasible.
- Support:
  - inhibit idle
  - inhibit suspend
  - release inhibition when timer stops/completes
- Handle unavailable inhibition gracefully.
- Document limitations by desktop environment.

### Suggested Interface Shape

```csharp
public interface ISessionInhibitor
{
    ValueTask<IAsyncDisposable?> AcquireAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default);
}
```

### Acceptance Criteria

- Keep-awake behavior is optional and failure-safe.
- Core timer logic does not know about Linux session APIs.
- App releases inhibition correctly.
- Limitations are documented.

### Suggested Branch

```bash
git checkout -b linux-port/session-inhibition
```

### Suggested Commit Message

```text
Add session inhibition abstraction
```

---

## Stage 10: Packaging Prototype

### Goal

Prepare the app for Linux distribution after the MVP app can run locally.

### Work Items

- Add `dotnet publish` instructions.
- Decide initial runtime identifiers:
  - `linux-x64`
  - optionally `linux-arm64`
- Prototype AppImage packaging.
- Draft Flatpak manifest.
- Document packaging permissions.
- Disable or omit Windows-style in-app update behavior for Linux builds.
- Add packaging docs:
  - `docs/linux-port/packaging.md`
- Do not pursue distro-native `.deb`, RPM, or AUR packaging until the app is stable.

### Suggested Publish Command

```bash
dotnet publish src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true
```

### Acceptance Criteria

- Release build can be published locally.
- Packaging docs explain Flatpak-first, AppImage-second strategy.
- Required permissions are documented.
- Packaging does not require Windows installer/update code.
- CI packaging can be planned from this point.

### Suggested Branch

```bash
git checkout -b linux-port/packaging-prototype
```

### Suggested Commit Message

```text
Prototype Linux packaging workflow
```

---

## Post-MVP Work

These items should not block the initial Linux MVP.

### Optional Tray / Status Notifier Support

- Add `IStatusIconService`.
- Support KDE/StatusNotifierItem-friendly environments.
- Treat GNOME tray absence as normal.
- Avoid making tray required for completion alerts.

### Single-Instance Behavior

- Add `ISingleInstanceService`.
- Decide between Avalonia-supported app lifetime behavior, D-Bus activation, lock file, or local socket.
- Support command-line handoff later.

### Startup / Background Behavior

- Add `IStartupService`.
- Respect user choice.
- Avoid hidden always-on background behavior without clear UI.

### Sound Alerts

- Add `IAudioAlertService`.
- Support bundled/default alert sounds.
- Add user preference for sound on/off.
- Keep sound separate from notifications.

### Settings Migration

- Decide whether importing Windows settings is worth supporting.
- If implemented, make it explicit and one-way.
- Do not silently import persistent identifiers.

### Theming and UI Polish

- Bring over Hourglass visual identity gradually.
- Add light/dark theme behavior.
- Test high-DPI scaling.
- Add keyboard navigation and accessibility pass.

### Wake From Suspend

- Keep out of MVP.
- Track as an advanced feature.
- Investigate distro-specific or service-specific backends.
- Never promise cross-desktop parity until proven.

---

## Suggested Overall Branch Sequence

```text
linux-port/scaffold
linux-port/source-inventory
linux-port/extract-parser-core
linux-port/parser-tests
linux-port/countdown-engine
linux-port/minimal-timer-ui
linux-port/notifications
linux-port/settings-storage
linux-port/session-inhibition
linux-port/packaging-prototype
```

---

## Suggested Milestone Structure

### Milestone 1: Buildable Scaffold

- Linux solution exists.
- Placeholder Avalonia app runs.
- Placeholder tests pass.
- Documentation exists.

### Milestone 2: Core Logic Extraction

- Parser/token logic extracted.
- Modern tests cover existing parsing behavior.
- Core library has no UI/platform dependencies.

### Milestone 3: Timer Engine MVP

- Monotonic countdown engine exists.
- Timer state is tested.
- UI can run a real countdown.

### Milestone 4: Minimal Linux App

- User can create and run timers.
- Notifications work.
- Settings persist.
- Basic keep-awake behavior exists.

### Milestone 5: Release Prototype

- Release build works.
- AppImage prototype exists.
- Flatpak manifest drafted.
- Packaging/update/privacy decisions documented.

---

## General Codex Working Rules

Use these rules for every future Codex prompt:

- Work in one stage at a time.
- Do not rewrite unrelated files.
- Preserve the legacy Windows app unless explicitly instructed.
- Keep changes small and reviewable.
- Run build/test commands when the installed SDK allows it.
- If the SDK/tooling is missing, report exact blockers instead of changing architectural decisions.
- Prefer adding tests before or alongside moved logic.
- Document any behavior change from original Hourglass.
- Do not implement wake-from-suspend without a separate design review.
- Do not add tracking IDs or Windows-style updater behavior to Linux builds.

---

## Recommended Next Step

After the scaffold commit lands, the next task should be:

```text
Create docs/linux-port/source-inventory.md by inspecting the existing Hourglass source tree. Categorize source files into reusable core candidates, Windows-specific implementation, installer/update-specific code, and refactor-required code. Do not move files yet.
```
