# Windows Feature Parity Roadmap

This document describes the remaining work required to bring the Linux port closer to the legacy Windows Hourglass application. It is based on the state of the `develop` branch reviewed on 21 June 2026.

The Linux app already provides the essential countdown experience: natural-language timer parsing, monotonic countdown timing, start/pause/resume/stop behavior, editable timer titles, responsive timer text, in-window progress, notifications, a basic sound alert, always-on-top, XDG-aware settings, session inhibition, and basic single-instance locking.

The remaining gap is primarily in advanced timer options, completion behavior, multi-window and session management, and desktop-environment integration.

This roadmap does not require identical Windows internals. Linux implementations should preserve the user-facing intent while respecting differences between GNOME, KDE Plasma, XFCE, Cinnamon, MATE, Wayland, X11, Flatpak, AppImage, and distro-native packages.

## Goals

- Preserve the simple, lightweight Hourglass interaction model.
- Reach practical feature parity without copying Windows-only architecture into the Linux app.
- Keep platform integrations behind interfaces in `Hourglass.Platform`.
- Keep timer state and option transitions testable outside Avalonia.
- Use capability detection and graceful fallback for desktop-specific features.
- Avoid making tray support, focus stealing, privileged wake alarms, or automatic shutdown mandatory for the core timer experience.

## Priority Definitions

| Priority | Meaning |
|---|---|
| P0 | Essential parity or a highly visible usability gap. |
| P1 | Important Windows behavior that materially improves daily use. |
| P2 | Advanced customization or desktop integration. |
| P3 | Optional, privileged, packaging-specific, or difficult to support consistently. |

Complexity ratings are relative implementation complexity, not delivery estimates.

| Complexity | Meaning |
|---|---|
| Small | Localized UI/view-model work with limited new infrastructure. |
| Medium | New persistent state, abstractions, or several coordinated components. |
| Large | Cross-process, multi-window, or desktop-specific integration. |
| Extra large | Privileged or highly desktop-dependent behavior requiring multiple backends. |

## Recommended Delivery Order

1. Completion attention and basic command parity.
2. Expanded timer options and persistence.
3. Taskbar/dock progress and optional tray integration.
4. Recent-input management, saved timers, and session restoration.
5. Multi-window support and single-instance command handoff.
6. Theme, sound, and title-mode customization.
7. Wake-from-suspend and other privileged or packaging-specific features.

The order deliberately delivers visible single-window improvements before introducing multi-window and cross-process complexity.

---

## Milestone 1: Completion Attention and Window Recovery

**Status:** Implemented on Linux.

### 1.1 Pop Up When Expired

**Priority:** P0  
**Complexity:** Medium

The Windows app can restore and raise a timer window when it expires. The Linux app currently updates its state, sends a notification, and plays an alert, but it does not restore or raise a minimized or obscured window.

#### Required behavior

- Add a persistent `PopUpWhenExpired` preference.
- When enabled and a timer expires:
  - restore the window when minimized;
  - show it if hidden by a future tray implementation;
  - request activation;
  - make a best-effort request to raise it above other windows;
  - preserve the user's existing `AlwaysOnTop` preference after the operation.
- When disabled, do not change window state or focus.
- Continue sending the desktop notification regardless of whether the compositor permits activation.
- Do not repeatedly raise the window on subsequent UI refresh ticks.

#### Linux implementation notes

- Put activation and restoration behavior behind an `IWindowAttentionService` or equivalent Avalonia-shell boundary.
- Keep the expiry decision in the view model or application coordinator, but keep direct `Window` calls in the Avalonia layer.
- Treat focus activation as best-effort. Wayland compositors may reject applications that attempt to steal focus.
- Consider a brief topmost toggle only as a fallback and restore the configured topmost state immediately afterward.
- Do not make compositor-specific behavior part of the core countdown engine.

#### Acceptance criteria

- A minimized timer is restored on expiry when the option is enabled on at least one supported X11 session and one supported Wayland session where the compositor permits it.
- Disabling the option leaves the minimized window untouched.
- Only one restore/attention request is issued per expiry event.
- Notification and sound behavior still work if activation fails.
- The setting survives application restart.

#### Tests

- View-model tests for one attention effect per expiry.
- Tests proving no attention effect occurs when disabled.
- Avalonia integration tests for restore/show/activate calls through an injected window service.
- Manual GNOME Wayland, GNOME X11 where available, and KDE Plasma smoke tests.

### 1.2 Expiration Flash and Persistent Completion Emphasis

**Priority:** P1  
**Complexity:** Medium

The Windows app flashes and then glows using the active theme when a timer expires. The Linux app currently changes completion text without an equivalent visual alert.

#### Required behavior

- Add a short expiry flash sequence followed by a persistent, restrained completion glow or border state.
- Stop the effect when the user starts a new timer, stops/resets the timer, dismisses completion, or closes the window.
- Respect reduced-motion preferences where detectable.
- Ensure the animation remains legible in light and dark desktop themes.
- Avoid rapid flashing that could create an accessibility problem.

#### Acceptance criteria

- Expiry produces a visible state change even with notifications and sound disabled.
- The completion emphasis remains until the timer is dismissed or replaced.
- Starting a new timer fully clears the expired visual state.
- Reduced-motion mode uses a static emphasis rather than repeated animation.

#### Tests

- State-transition tests for entering and leaving the expired visual state.
- Avalonia resource/style tests confirming the expired class and animation resources exist.
- Manual accessibility review for flash rate and contrast.

### 1.3 Invalid Input Feedback

**Priority:** P1  
**Complexity:** Small

The Windows app provides a visual validation flash. Linux currently only replaces the status text with an error message.

#### Required behavior

- Preserve the current validation message.
- Add a brief visual validation state around the timer input.
- Return focus to the timer input and keep the user's text selected or editable.
- Do not clear an invalid expression automatically.
- Expose validation state in a testable view-state property rather than driving it exclusively from code-behind.

#### Acceptance criteria

- Invalid input never starts a timer.
- The user can immediately correct the existing expression.
- Consecutive invalid submissions replay or refresh the feedback cleanly.
- The validation state clears when the expression changes or a valid timer starts.

---

## Milestone 2: Core Command and Keyboard Parity

**Status:** Implemented on Linux.

The Linux implementation uses the existing immutable countdown restart transition for duration timers. Absolute-time timers intentionally do not expose Restart because replaying an absolute target after it has passed is ambiguous. Window shortcuts are routed through the same commands as buttons and menu items, while full-screen state and close confirmation remain Avalonia-shell concerns.

### 2.1 Restart Timer

**Priority:** P0  
**Complexity:** Medium

Windows supports restarting the current timer from its original duration or target definition. Linux currently requires stopping and manually starting again.

#### Required behavior

- Add `RestartCommand` to the Linux view model.
- Preserve the original `TimerStart` used for the active timer.
- For duration timers, restart using the original duration from the current wall-clock time.
- For absolute-time timers, define and document behavior when the original target time is already in the past. The preferred behavior is to disable restart unless a safe repeated interpretation exists.
- Release and reacquire session inhibition correctly.
- Reset expiry notification, sound, and visual state.
- Expose Restart in the timer controls when valid and in the context menu.

#### Acceptance criteria

- Restarting a duration timer restores its original full duration.
- Restart is disabled when no valid previous start definition exists.
- Restarting an expired timer produces exactly one new active timer cycle.
- No duplicate notification or audio event leaks from the previous cycle.

#### Tests

- Core tests for restart semantics on duration and absolute-time starts.
- View-model command availability tests for stopped, running, paused, and expired states.
- Session-inhibition acquisition/release tests.

### 2.2 Keyboard Shortcuts

**Priority:** P1  
**Complexity:** Small

Add Windows-compatible shortcuts where they do not conflict with normal text editing.

#### Target shortcuts

| Shortcut | Action |
|---|---|
| `Space` | Pause or resume while focus is not editing text. |
| `Ctrl+P` | Pause or resume. |
| `Ctrl+S` | Stop the active timer. |
| `Ctrl+R` | Restart when supported. |
| `Escape` | Cancel editing, dismiss completion/sound, or leave full screen as appropriate. |
| `Alt+Enter` | Toggle full-screen mode. |
| `Enter` / numpad Enter | Start from timer input. |

#### Required behavior

- Do not interpret Space as pause/resume while the user is entering text.
- Prevent disabled commands from executing through key bindings.
- Keep shortcut definitions close to commands and cover them with XAML or input-routing tests.
- Document shortcuts in an About/help surface or README section once stable.

#### Acceptance criteria

- Every shortcut invokes the same command path as the corresponding UI action.
- Text input behavior remains normal while editing timer expressions or titles.
- Escape follows a deterministic priority order.

### 2.3 Full-Screen Mode

**Priority:** P1  
**Complexity:** Medium

#### Required behavior

- Toggle full-screen using `Alt+Enter` and a context-menu item.
- Preserve the prior window state and restore it on exit.
- Hide normal window chrome while full-screen.
- Scale timer and title text using the existing responsive controls.
- Exit full-screen with Escape only after higher-priority cancel/dismiss behavior has been considered.
- Persist full-screen state only if deliberate session restoration is later enabled; do not persist it as a global default initially.

#### Acceptance criteria

- Full screen works on a single monitor and restores the prior state correctly.
- Moving between monitors does not permanently corrupt the saved normal window bounds.
- Always-on-top and completion attention do not leave the window stuck above all others after exiting full screen.

### 2.4 Prompt on Exit and Close Current Timer

**Priority:** P1  
**Complexity:** Medium

#### Required behavior

- Add persistent `PromptOnExit` behavior.
- Prompt only when closing would discard a running or paused timer.
- Allow closing an individual timer window after multi-window support exists.
- Distinguish closing one timer from exiting the entire application.
- Do not prompt for a stopped or already completed timer unless unsaved state would otherwise be lost.
- Keep the confirmation dialog native to Avalonia and keyboard accessible.

#### Acceptance criteria

- Cancel leaves the timer running unchanged.
- Confirm closes only the intended window.
- Application shutdown handles multiple active timers with one clear decision flow rather than repeated confusing prompts.

The current single-window application prompts once for a running or paused timer. Multi-window shutdown coordination remains part of the later multi-window milestone rather than this implementation.

---

## Milestone 3: Expanded Timer Options

**Status:** Implemented on Linux.

The existing Linux settings record should evolve carefully. Avoid turning `LinuxAppSettings` into a mutable collection of UI concerns. Prefer immutable records with explicit defaults and backward-compatible JSON deserialization.

### 3.1 Reverse Progress Bar

**Priority:** P1  
**Complexity:** Small

- Add a persistent option to display elapsed percentage rather than remaining percentage.
- Apply the same direction to in-window and future taskbar/dock progress.
- Preserve the correct terminal value in expired state.
- Add converter or view-state tests for clamping and timers without meaningful duration progress.

### 3.2 Show Time Elapsed

**Priority:** P1  
**Complexity:** Medium

- Add an option to display elapsed time instead of remaining time.
- Preserve timer completion semantics and status text.
- Ensure title modes and accessibility names use the selected presentation.
- Decide how elapsed time behaves after expiry; the preferred behavior is to continue showing the completed duration rather than counting indefinitely unless Windows parity testing proves otherwise.

### 3.3 Loop Timer

**Priority:** P1  
**Complexity:** Medium

- Restart supported duration timers automatically when they expire.
- Keep each expiry observable while avoiding overlapping notification and audio tasks.
- Define whether the loop restarts immediately or after completion alert delivery; prefer immediate state restart with one alert per completed cycle.
- Disable the option for timer definitions that cannot be repeated safely.
- Track cycle count only if useful for diagnostics; do not add visible complexity without a product need.

### 3.4 Loop Sound Until Dismissed

**Priority:** P1  
**Complexity:** Large

The current audio abstraction is fire-and-forget. Looping requires controllable playback lifetime.

- Replace or extend `IAudioAlertService` with a playback handle that supports stop/dispose.
- Stop playback when the user presses Escape, starts another timer, restarts, stops, closes the window, or disables sound.
- Ensure one timer window cannot accidentally stop another timer's sound after multi-window support.
- Handle missing players and Flatpak sandbox restrictions gracefully.

### 3.5 Close When Expired

**Priority:** P2  
**Complexity:** Medium

- Close the timer window after completion effects and non-looping sound finish.
- If no sound is configured, close after the visual flash sequence or a short deterministic completion delay.
- Never close the entire application if other timer windows remain.
- Do not combine this option with loop timer.
- Define precedence with pop-up-on-expiry; close-on-expiry should win and should not visibly raise a window just before closing.

### 3.6 Lock Interface

**Priority:** P2  
**Complexity:** Medium

- Disable editing, stop, restart, context-menu options, and other timer modification while locked.
- Keep only safe close behavior available according to the Windows model.
- Make the locked state visually understandable without adding clutter.
- Starting a fresh timer should default back to unlocked unless explicitly selected again.
- Ensure keyboard shortcuts cannot bypass the lock.

### 3.7 Per-Timer Keep-Awake Preference

**Priority:** P2  
**Complexity:** Small

- Add `DoNotKeepComputerAwake` or an equivalent positive `KeepAwakeWhileRunning` option.
- Acquire session inhibition only when the timer is running and the option permits it.
- Apply changes immediately to an active timer.
- Preserve the current best-effort systemd-inhibit backend and future portal backend compatibility.

### 3.8 Automatic Shutdown on Expiry

**Linux status:** Wired through an unsupported no-op `ISystemPowerService`; the context-menu option remains disabled until a safe Linux backend with capability detection, confirmation, and cancellation is implemented.

**Priority:** P3  
**Complexity:** Extra large

This is a privileged and potentially destructive feature. It should not block general parity work.

- Keep shutdown behind a dedicated `ISystemPowerService`.
- Require an explicit confirmation when enabling it.
- Detect whether the desktop/session permits shutdown before exposing the option.
- Never invoke shell commands constructed from user input.
- Provide a visible warning and a cancellation path while the timer is running.
- Consider omitting this feature from Flatpak unless an appropriate portal or host permission model exists.
- Add extensive tests around option precedence, cancellation, and service failure.

---

## Milestone 4: Taskbar, Dock, and Tray Integration

### 4.1 Taskbar/Dock Progress

**Status:** Implemented for the first Unity LauncherEntry-compatible backend with conservative desktop detection. Unsupported or unverified desktops continue through the safe no-op backend until separately tested or given dedicated backends.

**Priority:** P0  
**Complexity:** Large

The Windows app displays normal, paused, and error progress states on its taskbar icon. Linux desktop environments do not share one universal API, so this must be capability-based.

#### Architecture

- Add an `IDesktopProgressService` to `Hourglass.Platform`.
- Use a no-op backend when the current desktop does not support progress.
- Keep the service API small:
  - set progress fraction;
  - set running, paused, error, or hidden state;
  - clear progress;
  - report whether the backend is supported.
- Keep timer-state-to-progress-state mapping in the application layer.
- Do not couple `Hourglass.Core` to D-Bus, Avalonia, launcher APIs, or a specific desktop environment.

#### Required behavior

- Running: normal progress state with a clamped fraction.
- Paused: paused progress state where supported, otherwise retain the fraction without misleading animation.
- Expired: error/attention state where supported.
- Stopped: clear launcher progress.
- Respect `ShowProgressInTaskbar`.
- Respect reverse-progress behavior.
- Clear stale progress during orderly shutdown and best-effort disposal.

#### Backend strategy

1. The first visible backend targets Unity LauncherEntry-compatible docks.
2. D-Bus session availability alone does not prove launcher progress support.
3. Select the Unity LauncherEntry backend only for deliberately recognized compatible desktops/docks or an explicit `HOURGLASS_DESKTOP_PROGRESS_BACKEND=unity` override.
4. Support `HOURGLASS_DESKTOP_PROGRESS_BACKEND=auto`, `unity`, and `none`.
5. Parse desktop identifiers case-insensitively and handle colon-separated values such as `ubuntu:GNOME`.
6. Default unknown desktops, missing desktop values, failed D-Bus sender initialization, and explicit disablement to the unsupported no-op backend.
7. Keep the no-op path fully functional so the timer never depends on launcher progress.
8. Keep all environment-variable and desktop-name handling inside `Hourglass.Linux.Services`.
9. Keep Milestone 4.2 tray/status icon support and future desktop-specific backends out of this task.

#### Acceptance criteria

- At least one confirmed desktop/dock combination visibly displays running progress.
- Fedora GNOME validates the unsupported no-op path without errors.
- At least one additional desktop environment is smoke-tested and documented.
- Paused and expired states degrade sensibly where distinct launcher states are unavailable.
- Unsupported desktops do not impair timer behavior.
- Stopping, disabling progress, and application shutdown clear stale launcher state.
- Multiple timers have a documented aggregation strategy before multi-window support ships. The recommended initial strategy is to show the active/focused timer, then revisit an aggregate policy.

#### Cross-Desktop Manual Validation

Exact tested combinations belong in `docs/linux-port/desktop-progress-compatibility.md`. Do not broadly mark Milestone 4.1 as supported across Linux desktops. The intended wording is:

> Milestone 4.1 is implemented for the explicitly documented Unity LauncherEntry-compatible desktop and dock combinations. Unsupported or unverified desktops continue through the safe no-op backend until separately tested or given dedicated backends.

Fedora GNOME stock is an intentional fallback validation environment. KDE Plasma is tested and documented as an exploratory compatibility check, but support is not claimed unless the backend visibly and repeatably works. Additional desktop-specific backends are follow-up work and do not block this milestone. Multi-window aggregation remains deferred until multi-window support exists.

### 4.2 Optional Tray/Status Icon

**Status:** Implemented for Avalonia-supported status icon environments with conservative desktop detection. Unsupported or unverified desktops continue through the safe no-op backend.

**Priority:** P2  
**Complexity:** Large

Tray support is optional because some desktop environments, particularly stock GNOME configurations, do not expose legacy tray icons without extensions.

The Linux implementation keeps tray support optional. It adds an `IStatusIconService` boundary, an Avalonia status icon backend selected only for recognized tray-capable desktop identifiers or the explicit `HOURGLASS_STATUS_ICON_BACKEND=avalonia` override, and an unsupported no-op backend everywhere else. The persistent **Show in notification area** option is disabled by default and masked off when no backend is supported.

The current Avalonia backend does not enable hiding the only timer window. It can initialize a status icon, but it cannot prove that the desktop visibly rendered that icon or that the user has a reliable recovery path after hiding the window. Hide-to-notification-area should remain disabled until a backend can verify recoverability or relaunch activation can raise the existing instance.

#### Required behavior

- Add an `IStatusIconService` capability abstraction.
- Do not make application startup fail if no tray backend exists.
- Provide actions for:
  - show/restore timer window;
  - create a new timer after multi-window support;
  - pause/resume the selected timer where unambiguous;
  - stop and restart the selected timer where supported by the current timer state;
  - exit the application.
- Add a persistent `ShowInNotificationArea` option only when a supported backend is available.
- Allow hiding to the notification area only when the status icon backend is initialized, the option is enabled, and the backend or launch path provides a verified recovery path.
- If minimizing to tray is enabled later, ensure the user always has a recovery path through notifications or application relaunch.

#### Acceptance criteria

- The app remains fully usable without a tray.
- Hiding a window is not allowed unless the status icon backend has initialized successfully and can recover the hidden window.
- Re-launching the app raises the hidden existing instance after command handoff is implemented.
- Tray menu state reflects the actual timer state.

The current single-window implementation exposes show, hide, pause/resume, stop, restart, and exit. New Timer remains deferred until multi-window support exists, and relaunch recovery remains deferred until single-instance command handoff exists.

Exact tested combinations belong in `docs/linux-port/status-icon-compatibility.md`. Do not broadly claim tray support across Linux desktops until an exact distribution, desktop, session type, panel or extension, backend selection, and validation date have been recorded.

---

## Milestone 5: Recent Inputs, Saved Timers, and Session Restoration

**Status:** Implemented for the current single-window Linux application. Recent inputs, saved timer definitions, and active-session restoration persist through separate JSON documents. Multi-window actions such as opening all saved timers as independent windows remain deferred until Milestone 6.

### 5.1 Recent Inputs Menu

**Priority:** P1  
**Complexity:** Medium

Linux already persists recent input strings but exposes only the most recent expression on startup.

- Add a dynamic Recent Inputs submenu.
- Selecting an item places it into input mode without starting automatically.
- Add Clear Recent Inputs with confirmation only when needed.
- Preserve de-duplication and maximum-entry rules.
- Keep title history separate from timer-expression history unless a future UX explicitly pairs them.

#### Acceptance criteria

- Recent items survive restart.
- Duplicate expressions do not appear multiple times.
- Clearing the list updates the menu and settings immediately.
- Corrupt or old settings files continue to fall back safely.

### 5.2 Saved Timer Definitions

**Priority:** P1  
**Complexity:** Large

Saved timer definitions are reusable presets, distinct from currently running timer sessions.

- Introduce a versioned saved-timer DTO containing at minimum:
  - timer expression or normalized `TimerStart` representation;
  - timer title;
  - supported per-timer options;
  - stable identifier;
  - optional display name.
- Add Save Current Timer, Open Saved Timer, Open All Saved Timers, Remove, and Clear actions.
- Avoid serializing Avalonia controls, platform service objects, or WPF legacy types.
- Store saved timers separately from global application preferences so future schema migration remains manageable.

#### Acceptance criteria

- A saved duration timer can be reopened after restart with its title and supported options intact.
- Invalid or partially migrated entries are skipped without preventing the app from opening.
- Open All creates independent timer windows only after multi-window support exists.

The current Linux implementation keeps **Open All saved timers** disabled because there is only one timer window. It records saved definitions in a separate versioned document so Milestone 6 can add independent window creation without changing the saved-timer schema.

### 5.3 Active Session Persistence

**Priority:** P1  
**Complexity:** Large

- Persist active timer sessions separately from reusable saved definitions.
- Record enough data to reconstruct running, paused, stopped, or expired state without relying on process uptime.
- For running timers, persist the target wall-clock time and original start definition.
- For paused timers, persist remaining duration and paused state.
- Store title, supported options, and window identity.
- Write session changes atomically and tolerate an interrupted write.
- Define behavior after clock changes, timezone changes, reboot, and a target time passing while the app is closed.

#### Recommended restoration behavior

- Running timer with a future target: resume counting down.
- Running timer whose target passed while closed: restore as expired and notify once after startup.
- Paused timer: restore paused with the saved remaining duration.
- Stopped input window: restore only if the user enables startup restoration.

### 5.4 Open Saved Timers on Startup

**Priority:** P2  
**Complexity:** Medium after session persistence

- Add a global preference to restore saved or active timers at startup.
- Avoid opening duplicate windows when a second process hands off to the existing instance.
- Cap or warn about unusually large restored window counts.
- Restore windows on visible displays if saved monitor geometry is no longer valid.

The current Linux implementation exposes the startup preferences and restores the active single-window session. Opening multiple saved timers on startup is persisted as a preference but does not create additional windows until Milestone 6 supplies multi-window behavior.

---

## Milestone 6: Multi-Window Timer Management

**Status:** Implemented for native Linux timer windows. The Avalonia app now uses an application-level timer-window coordinator, supports multiple independent timer windows, opens saved timers as separate windows, persists active sessions in a versioned multi-session document, and coordinates shared keep-awake, status-icon, and desktop-progress behavior. Command-line handoff is implemented in Milestone 7, and durable active-session window geometry is implemented in Milestone 8.5.

### 6.1 Multiple Timer Windows

**Priority:** P1  
**Complexity:** Extra large

The Windows version can manage several independent timers. The Linux app currently owns one `MainWindow` and one view model.

#### Architecture

- Add an application-level timer-window coordinator rather than placing global behavior inside `MainWindow`.
- Give every timer window:
  - its own `CountdownEngine` or timer session object;
  - its own view model and window-scoped services;
  - a stable session identifier;
  - independent title, options, notification, sound, and inhibition lifetime.
- Keep shared services such as settings storage and desktop integration application-scoped.
- Change application shutdown mode so closing one window does not terminate the process while other timers remain.
- Define which timer is selected for global tray or launcher actions.

#### Required behavior

- Add New Timer to the context menu and application/tray actions.
- Allow timers to run, pause, expire, and close independently.
- Ensure one window's completion sound and visual state does not modify another window.
- Keep session inhibition active while at least one eligible timer is running.
- Aggregate or reference-count shared services rather than starting conflicting processes per window where possible.

#### Acceptance criteria

- Two timers can run concurrently with different durations and titles.
- Pausing, restarting, or closing one does not change the other.
- The app remains alive while any timer window is open or a tray policy explicitly keeps it alive.
- All timers are disposed cleanly at application exit.

### 6.2 Application-Level Notification Routing

**Priority:** P1  
**Complexity:** Medium after multi-window support

- Include the timer title in notification content when available.
- Route notification activation back to the correct timer window where the notification backend permits actions.
- Ensure two simultaneous expiries each produce one correctly identified notification.
- Avoid reusing a static notification identifier that causes one timer to replace another unintentionally.

### 6.3 Shared Keep-Awake Coordination

**Priority:** P1  
**Complexity:** Medium after multi-window support

- Replace per-window acquire/release races with a coordinator or reference-counted lease model.
- Hold inhibition while at least one running timer requests it.
- Release inhibition only after the last eligible timer pauses, stops, expires, or closes.
- Recover safely if an inhibition backend process exits unexpectedly.

---

## Milestone 7: Single-Instance Handoff and Command-Line Activation

**Status:** Implemented on Linux using the existing per-user file lock for ownership plus a Unix domain socket for local launch handoff. A no-argument secondary launch requests activation of the most relevant timer window. A timer-expression launch creates and starts a new independent timer window in the existing process. Optional `--title` / `-t` support is documented and tested. The broader legacy Windows command-line switch surface remains out of scope for this milestone.

### 7.1 Existing-Instance Activation

**Priority:** P1  
**Complexity:** Large

The current advisory file lock prevents a second instance but the second process exits without activating the existing window.

- Add a per-user IPC channel separate from the ownership lock.
- Prefer a Linux-native local mechanism such as a Unix domain socket or a well-scoped D-Bus service.
- Authenticate by filesystem permissions or user-session scoping; do not expose a network listener.
- When no arguments are supplied, request that the existing instance show and activate its most relevant timer window.
- If all windows are hidden, restore one deterministically.
- Handle stale endpoints and application crashes.

### 7.2 Command-Line Timer Handoff

**Priority:** P1  
**Complexity:** Large

- Parse command-line timer expressions using shared application/core parsing behavior.
- Forward the original structured request to the existing instance.
- Open a new timer window when multi-window support exists.
- Support an optional title argument only after argument syntax is documented and tested.
- Return a non-zero exit code for malformed local command syntax, but do not expose internal IPC errors as crashes.

#### Acceptance criteria

- Running `hourglass "10 minutes"` while Hourglass is open creates or starts the requested timer in the existing process.
- Running Hourglass with no timer argument raises an existing hidden/minimized window.
- Concurrent launches do not create duplicate ownership or corrupt the IPC socket.
- Requests from another OS user are rejected by normal filesystem/session permissions.

---

## Milestone 8: Themes, Sounds, and Window Titles

**Status:** Implemented on Linux across built-in theme selection, custom theme management, sound selection and preview, window title modes, and active-session window geometry persistence. The next documented roadmap area is Milestone 9: Wake From Suspend.

### 8.1 Built-In Light and Dark Themes

**Status:** Implemented on Linux with explicit System, Light, and Dark theme choices. The selected theme is persisted in Linux settings and saved-timer options, and the Avalonia shell maps the preference to platform theme variants without introducing Windows color dependencies in `Hourglass.Core`.

**Priority:** P2  
**Complexity:** Medium

- Add explicit System, Light, and Dark choices.
- Map reusable theme data to Avalonia brushes in the UI layer.
- Keep completion flash, progress, primary text, secondary text, hints, and buttons accessible in each theme.
- Persist the selected theme globally or per timer according to the saved-timer model.
- Avoid hard dependencies on Windows color types in `Hourglass.Core`.

### 8.2 Custom Theme Management

**Status:** Implemented on Linux with app-local JSON custom themes, simple in-app create/rename/duplicate/edit/delete, and single-theme import/export. Imported themes are data-only color definitions; the Linux app never loads XAML or executable resources from theme files.

**Priority:** P3  
**Complexity:** Large

- Introduce a versioned platform-neutral theme model.
- Provide create, rename, duplicate, edit, delete, import, and export only after built-in themes are stable.
- Validate colors and fall back to a built-in theme if a custom theme is invalid.
- Ensure custom themes cannot inject XAML or arbitrary resources.

### 8.3 Sound Selection and Preview

**Status:** Implemented on Linux for `None` plus packaged built-in Loud, Normal, and Quiet beep sounds. The Avalonia menu persists the selected sound, exposes preview and stop-preview commands, and uses service-level availability checks so missing packaged assets fail safely. Arbitrary user-selected audio files are deferred until Flatpak, portal, and file-permission behavior can be made consistent.

**Priority:** P2  
**Complexity:** Large

- Port reusable sound identifiers and metadata without copying Windows playback code.
- Provide None and the built-in sound choices supported by the package.
- Add preview and stop-preview actions.
- Decide whether arbitrary user-selected audio files are supported; defer them if Flatpak permissions make the UX inconsistent.
- Include per-sound availability detection and a safe fallback.

### 8.4 Window Title Modes

**Status:** Implemented on Linux for application name, time left, time elapsed, timer title, and the supported combined time/title ordering modes. Formatting lives in `Hourglass.Core` and is covered by pure formatter tests; the Avalonia view model persists the selected mode and updates dynamic title modes on timer ticks. The optional title-bar-hidden mode remains deferred because it is desktop-dependent.

**Priority:** P2  
**Complexity:** Medium

Support the Windows display modes where they make sense on Linux:

- application name;
- time left;
- time elapsed;
- timer title;
- time left plus timer title;
- time elapsed plus timer title;
- timer title plus time left;
- timer title plus time elapsed;
- optional title-bar-hidden mode.

#### Requirements

- Keep title formatting in a pure, testable formatter.
- Update the title on timer ticks only when the selected mode includes changing time.
- Preserve an accessible application/window name even if visual chrome is hidden.
- Treat title-bar removal as desktop-dependent and test resizing, dragging, and close recovery carefully.

### 8.5 Window Geometry Persistence

**Status:** Implemented on Linux for active timer sessions and multi-window restore. Normal bounds, maximized state, last non-minimized geometry, per-window active-session snapshots, work-area validation, off-screen recovery, window cascading, and debounced geometry saves are implemented and tested. Broader saved-timer geometry semantics remain future work unless a later workflow requires them.

**Priority:** P2  
**Complexity:** Large with multi-window support

- Persist normal window bounds, maximized state, and last non-minimized state.
- Do not restore minimized state on normal application startup.
- Validate saved geometry against current monitor work areas.
- Reposition windows that would otherwise be entirely off-screen after monitor changes.
- Store geometry per active/saved window rather than as one global rectangle once multi-window support lands.
- Avoid writing geometry continuously on every pixel of a resize; debounce persistence.

---

## Milestone 9: Wake From Suspend

### 9.1 Wake Alarm Service

**Priority:** P3  
**Complexity:** Extra large
**Status:** Initial disabled-by-default service/backend implemented. Physical hardware, permission, and packaging validation remain before user-facing enablement.

Linux wake scheduling varies by distro, hardware, permissions, power manager, packaging format, and whether the system supports RTC wake alarms.

#### Architecture

- Add an optional `IWakeAlarmService` capability interface.
- Keep wake scheduling independent from normal session inhibition.
- Expose support status and clear failure reasons to the UI.
- Never claim wake support if the backend cannot confirm that an alarm was scheduled.
- Cancel or replace the wake alarm when the timer is paused, stopped, restarted, or changed.

#### Investigation order

1. Document target environments and available unprivileged APIs.
2. Prototype outside the main UI behind the platform abstraction.
3. Test suspend/resume on physical hardware, not only virtual machines.
4. Evaluate Flatpak constraints separately from AppImage and native packages.
5. Ship disabled by default until reliability is demonstrated.

#### Acceptance criteria

- Supported systems schedule and cancel a wake alarm predictably.
- Unsupported systems continue normal countdown behavior and explain that wake is unavailable.
- A failed wake scheduling attempt does not stop the timer or disable notifications after resume.

---

## Milestone 10: Updates and Distribution Parity

**Status:** Implemented for Linux update policy, reproducible `linux-x64` publish/AppDir scripts, package metadata validation, and CI packaging artifacts. Public release signing, package repository publication, Flathub submission, and AppImage update metadata remain future work.

### 10.1 Linux Update Strategy

**Status:** Implemented as a no-in-app-updater policy for Linux builds. Package channels own updates, and Linux builds do not add updater UUIDs, silent downloads, or runtime network checks.

**Priority:** P2  
**Complexity:** Medium

The Windows in-app updater should not be copied directly.

- Flatpak builds should rely on the configured Flatpak repository/update channel.
- Distro-native packages should rely on the system package manager.
- AppImage builds may expose a release page or later adopt an AppImage-compatible update mechanism after signing and release infrastructure exist.
- The application may show the installed version and a release-information link, but should not silently download and execute replacement binaries.
- Keep update checks optional and document network/privacy behavior before enabling them.

### 10.2 Packaging Completion

**Status:** Implemented for the first CI-backed package artifact workflow. The Flatpak and AppImage prototypes install shared app metadata, icons, and bundled sound assets, while final signing and distribution-channel submission remain deferred.

**Priority:** P1 for release readiness  
**Complexity:** Large

- Complete Flatpak manifest, permissions, desktop file, icons, AppStream metadata, and notification/audio validation.
- Complete AppImage AppDir construction and desktop integration.
- Ensure all bundled sounds and assets are included in published artifacts.
- Add reproducible release commands and CI artifacts.
- Verify configuration and data paths under sandboxed and unsandboxed packages.
- Sign release artifacts where the chosen distribution channel supports it.

---

## Post-Milestone-10 Cross-Cutting Plan

Milestones 1 through 10 now cover the main practical parity features. The next work is release-hardening: reduce settings coupling, make optional platform integrations easier to reason about, and prove that the app remains usable across the supported Linux packaging and desktop combinations.

This section is intentionally not Milestone 11. These workstreams cut across implemented features rather than adding one new user-facing capability.

### Workstream 1: Settings Architecture Split

**Goal:** Break the growing `LinuxAppSettings` surface into focused immutable models without breaking existing JSON files.

**Status:** Implemented for global application preferences, timer defaults, app settings merge behavior, active-session snapshots, multi-window active-session collections, and active-session window placement. The persisted JSON document names and DTO shapes remain compatible.

**Implementation plan:**

1. Inventory every persisted setting by owner: global app preference, timer default, saved-timer option, active-session snapshot, custom-theme reference, or window placement.
2. Introduce focused records in `Hourglass.Core.Settings` while keeping the current `app.json`, saved-timer, active-session, and custom-theme documents readable.
3. Add mapper methods that convert old document shapes into the new records immediately after deserialization and convert the new records back to the current storage documents when saving.
4. Move merge logic out of `MainWindowViewModel` into pure settings merge functions that accept previous, requested, and latest snapshots.
5. Keep `MainWindowViewModel` responsible only for replacing a settings snapshot, raising property notifications, and queuing persistence.
6. Update `docs/linux-port/settings.md` after the split so the document names, owners, and compatibility rules match the implementation.

**Acceptance criteria:**

- Existing `app.json`, saved timers, active sessions, and custom themes still deserialize with defaults for absent fields.
- Saved timer options and active-session options continue to preserve per-timer behavior.
- Concurrent settings saves still merge independent option changes without losing newer persisted data.
- Round-trip, old-schema, and concurrent-save tests cover every new settings model.

### Workstream 2: Platform Boundary Consolidation

**Goal:** Make every optional Linux integration explicit, injectable, and safe to run when unsupported.

**Status:** Implemented for shared unsupported platform-service fallbacks, Linux backend selection, Avalonia window-attention routing, and source/test coverage that keeps `Hourglass.Core` free of desktop side effects.

**Implementation plan:**

1. Audit all optional effects: notifications, controllable audio, session inhibition, desktop progress, status icon, single-instance IPC, window attention, wake alarms, system power actions, and future startup/autostart.
2. Ensure each effect has one narrow `Hourglass.Platform` interface, one supported Linux implementation where available, and one unsupported/no-op implementation in the platform or services layer.
3. Move private no-op implementations out of view-model internals when they represent platform behavior rather than test scaffolding.
4. Keep Avalonia-only behavior in the Avalonia shell, but expose any direct desktop side effect through a small boundary before it reaches core logic.
5. Document capability meanings clearly: `IsSupported` must mean the backend was selected and initialized, not that the desktop visibly rendered an effect.

**Acceptance criteria:**

- `Hourglass.Core` remains free of Avalonia, Linux APIs, D-Bus, filesystem, clock, notification, audio, and power dependencies.
- Unsupported integrations do not throw during normal timer workflows.
- Tests cover supported and unsupported behavior for each platform service.
- Adding a future backend does not require changing timer transition logic.

### Workstream 3: Error Handling and Diagnostics

**Goal:** Make optional-integration failures predictable and diagnosable without corrupting timers or overwhelming users.

**Status:** Implemented for structured diagnostic events, default Trace-based runtime diagnostics, in-memory duplicate suppression, and representative best-effort/data-recovery/user-requested failure coverage across platform service boundaries and Avalonia shell controllers.

**Implementation plan:**

1. Classify failures as best-effort, user-requested, startup/configuration, or data-corruption recovery.
2. Keep best-effort failures such as notifications, audio fallback, launcher progress, status icon updates, attention requests, and session inhibition from stopping or mutating the timer.
3. Surface user-visible errors only when the user explicitly requested an action, such as wake scheduling or a future shutdown backend.
4. Add structured diagnostic context at service boundaries: backend name, command or capability used, document key, and whether fallback was attempted.
5. Suppress repeated noisy failures after the first useful diagnostic unless the user changes the relevant setting or backend.

**Acceptance criteria:**

- Timer start, pause, resume, stop, restart, expiry, save, restore, and close paths continue after optional effect failures.
- Malformed settings and session documents fall back to defaults or valid subsets without crashing startup.
- User-requested unsupported actions remain disabled or report a clear reason.
- Tests cover representative failures for every optional integration path.

### Workstream 4: Accessibility Pass

**Goal:** Verify the implemented parity UI remains usable by keyboard and assistive technologies.

**Status:** Implemented for the primary timer surface, owned shell dialogs, custom-theme controls, and context-menu toggle characterization. The Avalonia UI now declares stable automation names/help text for timer fields, primary command buttons, About/link/copy feedback, exit confirmation, and custom-theme edit/delete controls. Source-inspection tests cover keyboard command paths, focus-return entry points, dialog keyboard defaults, and menu checked-state exposure. Full assistive-technology desktop certification remains part of the release validation matrix.

**Implementation plan:**

1. Review all timer commands, menu items, dialogs, and dynamic controls for keyboard reachability and stable focus order.
2. Confirm expiry, invalid-input, locked-interface, disabled-command, and selected-option states do not rely on color alone.
3. Add accessible names or automation properties for icon-like buttons, context-menu check states, and custom-theme controls where Avalonia defaults are insufficient.
4. Keep validation and completion effects short, non-repeating, and below unsafe flashing rates; document the reduced-motion fallback when Avalonia exposes a dependable desktop signal.
5. Verify timer and title text remain legible at minimum window size, maximized/full-screen size, and common desktop scale factors.

**Acceptance criteria:**

- Every primary timer action has a keyboard path.
- Focus returns to a predictable control after dialogs close, timers expire, and edit mode is canceled.
- Context-menu toggles expose correct checked state and labels.
- Automated tests or targeted UI characterization tests cover the most important accessibility regressions.

### Workstream 5: Localization Prep - Implemented

**Goal:** Prepare the Linux UI for localization before a public parity release.

**Status:** Implemented. Runtime Avalonia UI text now flows through the Linux Avalonia
`ApplicationStrings`/`.resx` boundary, while packaging/AppStream metadata and protocol
identifiers remain separate.

**Implementation plan:**

1. Inventory direct user-facing English strings in Avalonia views, view models, notifications, dialogs, menu labels, status text, validation messages, and packaging-visible metadata.
2. Move localizable runtime UI strings into resource files; keep packaging metadata localized separately through AppStream-supported mechanisms if needed.
3. Avoid concatenating translated fragments. Prefer resource strings with placeholders for timer title, time text, sound name, and backend names.
4. Keep deterministic formatting logic in `Hourglass.Core` for timer titles, notification titles, and status text that must be tested independently.
5. Add tests for resource-backed title/status/notification formatting using representative blank-title, custom-title, elapsed-time, and time-left modes.

**Acceptance criteria:**

- No new user-facing runtime string is hard-coded outside the localization boundary unless it is a protocol value, file name, command name, or diagnostic-only implementation detail.
- Title, status, notification, and dialog text remain testable without starting Avalonia.
- Existing English behavior remains unchanged after extraction.

### Workstream 6: Release Validation Matrix - Native/AppImage and Flatpak matrices recorded; remaining validation pending

**Goal:** Turn the existing smoke-test notes into a repeatable release-readiness checklist.

**Status:** Checklist implemented in `docs/linux-port/release-validation-checklist.md`.
The native/AppImage and Flatpak desktop smoke matrices have recorded results or
skipped-with-reason entries for every seeded row. The local Fedora GNOME Wayland
native/AppDir run produced native publish and AppDir prototype evidence, but
full attended GUI smoke coverage was skipped and final `.AppImage` support
remains gated by the distribution roadmap's Milestone 4 because the current
CI/package flow produces an AppDir artifact, not a final `.AppImage`. The local
Fedora GNOME Wayland Flatpak prototype initially built and installed but failed
to launch because the manifest installed only the apphost plus selected assets
instead of the full publish output required by the runtime-managed app. The
manifest now installs the complete publish output under `/app/lib/hourglass-linux`,
adds a `/app/bin/hourglass-linux` launcher, and grants the X11 socket required
by the current Avalonia desktop backend; the fixed prototype built, installed,
and reached a bounded desktop launch. Flathub `org.flatpak.Builder` supplied
`flatpak-builder-lint`; after permission cleanup, manifest lint still reports
`appid-url-not-reachable` because the current app ID maps to a GitHub repository
name that differs from `hourglass-linux`. Full attended Flatpak smoke coverage,
multi-monitor validation, and physical wake validation runs are still pending
and must not be treated as completed until exact validation rows record their
results.

**Implementation plan:**

1. Create a release validation checklist under `docs/linux-port/` that records exact desktop, session type, package type, app version, and tester notes.
2. Cover native/AppImage and Flatpak paths separately because settings paths, audio, portals, notifications, single-instance behavior, and wake support can differ.
3. Keep the minimum desktop matrix: Fedora GNOME Wayland, Fedora GNOME X11 where available, KDE Plasma Wayland, KDE Plasma X11 where available, XFCE X11, and Cinnamon or MATE X11.
4. For each environment, test start, pause, resume, stop, restart, absolute-time parsing, duration parsing, minimize and expiry, notification, sound, always-on-top, completion attention, session inhibition, desktop progress when supported, status-icon recovery when supported, clean shutdown, and settings persistence.
5. Add a separate multi-monitor checklist for mixed scale factors, monitor disconnect, restored placement, full-screen on secondary displays, and off-screen recovery.
6. Record wake-from-suspend as a special hardware validation track. It must remain disabled by default until physical hardware, permissions, native/AppImage, and Flatpak behavior are proven.

**Acceptance criteria:**

- Every release candidate has an attached validation record or an explicit skipped-with-reason entry.
- Package artifacts are produced by the CI-backed publish/AppDir workflow and pass strict metadata validation.
- Known unsupported desktop capabilities are documented as unsupported rather than treated as failures.
- Any failed release-check item either blocks release or has a documented deferral with user impact.

---

## Testing Strategy

### Automated Tests

Maintain or add coverage for:

- timer transitions and restart semantics;
- looping behavior and one-notification-per-cycle rules;
- option defaults and JSON migration;
- title-mode formatting;
- reverse and elapsed progress calculations;
- command availability in every timer state;
- interface-lock enforcement across buttons, menus, and shortcuts;
- multi-window isolation;
- shared keep-awake coordination;
- session save/restore around future, expired, paused, and corrupt sessions;
- single-instance IPC framing and stale-endpoint recovery;
- no-op behavior for unavailable platform capabilities.

### Desktop Smoke-Test Matrix

At minimum, release candidates should be checked on:

| Environment | Session focus |
|---|---|
| Fedora GNOME | Wayland primary; X11 where available. |
| KDE Plasma | Wayland and launcher/taskbar integration. |
| XFCE | X11, notifications, optional tray. |
| Cinnamon or MATE | X11, notification and tray behavior. |
| Flatpak sandbox | Notifications, sound, settings, portals, and file access. |
| AppImage/native publish | Desktop file, icons, sound assets, and config paths. |

For every environment, smoke-test:

- start, pause, resume, stop, restart;
- absolute-time and duration parsing;
- minimize and expiry;
- notification and sound;
- always-on-top;
- completion attention;
- session inhibition;
- taskbar/dock progress if supported;
- tray recovery if supported;
- clean shutdown and settings persistence.

### Manual Multi-Monitor Tests

- Move timers between monitors with different scale factors.
- Disconnect a monitor while timers are open.
- Restore saved window placement after display topology changes.
- Enter and leave full screen on secondary monitors.
- Confirm no restored window is entirely off-screen.

---

## Suggested Post-Milestone-10 Issue Breakdown

Create one focused issue or Codex task for each of the following rather than attempting one large hardening pull request:

1. Implemented: Inventory settings ownership and define the target settings model split.
2. Implemented: Extract timer defaults and global application preferences from `LinuxAppSettings`.
3. Implemented: Extract active-session and window-placement persistence models with legacy JSON compatibility.
4. Implemented: Move settings merge behavior into pure core functions and cover concurrent-save scenarios.
5. Implemented: Audit platform service boundaries and relocate no-op implementations that represent real platform behavior.
6. Implemented: Add or tighten unsupported-backend tests for every optional desktop integration.
7. Implemented: Define the optional-effect failure taxonomy and align service diagnostics with it.
8. Implemented: Add regression tests for timer continuity after optional integration failures.
9. Implemented: Run keyboard/focus/accessibility characterization for the main timer window and dialogs.
10. Implemented: Add accessible names and checked-state coverage where Avalonia defaults are insufficient.
11. Implemented: Inventory direct user-facing Linux UI strings and choose the runtime resource structure.
12. Implemented: Move timer status, command, dialog, and notification strings behind resources.
13. Implemented: Add localization-safe formatter tests for title, status, and notification text.
14. Implemented: Create the release validation checklist document.
15. Implemented: Run and record the native/AppImage desktop smoke matrix.
16. Implemented: Run and record the Flatpak sandbox smoke matrix.
17. Run and record multi-monitor placement validation.
18. Run and record physical wake-from-suspend validation before enabling any user-facing wake option.

## Definition of Practical Feature Parity

The Linux port can be considered to have practical parity when:

- the common Windows timer workflow is available without relying on Windows-only APIs;
- users can run, restart, customize, save, restore, and manage multiple timers;
- expiry remains noticeable when the window is minimized or obscured;
- launcher progress and tray behavior work where the desktop supports them and degrade safely elsewhere;
- settings and active sessions survive restarts without corruption;
- secondary launches activate or send timer requests to the running process;
- unsupported privileged features are clearly identified rather than silently pretending to work;
- the app passes automated tests and the documented Linux desktop smoke-test matrix.

Post-Milestone-10 work is the hardening path to make those criteria reliable enough for a public Linux parity release.

Wake-from-suspend, automatic shutdown, custom themes, and universal tray behavior should be treated as optional advanced parity. Their absence should not prevent a stable release if the core and practical parity criteria above are satisfied.
