# Linux Feature Parity

| Feature | Windows Status | Linux Scaffold Status | Linux Plan |
|---|---|---|---|
| Timer parsing | Implemented in legacy app | Extracted to `Hourglass.Core` with tests | Keep behavior stable while integrating the Linux UI. |
| Timer state | Implemented in legacy app | Core serialization DTOs and timer-start model extracted | Continue extracting platform-neutral timer/session state before settings migration. |
| Countdown timing | Uses WPF `DispatcherTimer` and `DateTime.Now` | Monotonic `CountdownEngine` implemented in `Hourglass.Core` | Use Avalonia timers only for UI refresh ticks. |
| Timer UI | WPF | Avalonia timer UI backed by `Hourglass.Core`, including restart, full-screen mode, persistent completion emphasis, and replayable invalid-input feedback | Continue adding parity features without moving window or animation effects into Core. |
| Notifications | Windows notification area balloon behavior | `INotificationService` with Linux `notify-send` backend | Keep notification delivery best-effort; app UI remains recoverable state. |
| Tray/status icon | WinForms `NotifyIcon` | Not implemented | Optional; abstract behind platform interfaces. |
| Keep awake | Windows execution state APIs | `ISessionInhibitor` with Linux `systemd-inhibit` backend | Add portal backend later if packaging requires it. |
| Wake from suspend | Windows waitable timer resume behavior | Out of scope | Defer until after MVP. |
| Audio alerts | Windows-focused implementation | `IAudioAlertService` with best-effort Linux process backend | Supports the built-in Normal beep. |
| Settings | .NET Framework settings | Linux JSON settings store for recent inputs, notifications, sound, always-on-top, pop-up-on-expiry, and prompt-on-exit | Add broader preferences with backward-compatible defaults and explicit migration decisions. |
| Single instance | Windows Forms application base with command-line handoff | `ISingleInstanceService` with Linux advisory file lock | Secondary launches exit cleanly; handoff and window activation are deferred. |
| Updates | Windows in-app update check | Not implemented | Disable or replace for Linux packaging. |
| Packaging | MSI, bundle, portable Windows build | Publish docs plus Flatpak and AppImage prototype files | Flatpak first, AppImage second. |

The Linux MVP should not promise complete feature parity. It should prioritize a reliable native timer experience with notifications, sound, settings, and testable timing behavior.

The **Pop up when expired** context-menu option is enabled by default and persists across restarts. On expiry, Hourglass shows a short visual flash, retains a completion border until the timer is dismissed, and makes one best-effort request to show, restore, and activate the window. If the first restore request is refused, the Avalonia shell performs one hide/show remap before activating; it does not loop or alter the persisted always-on-top setting. Wayland compositors may still reject application-initiated activation or focus changes; notification, sound, and visual completion behavior continue independently when that happens.

Invalid timer submissions preserve the entered expression, keep the editor active, and replay a brief validation treatment on every attempt. Avalonia 12 does not currently expose a dependable cross-desktop reduced-motion preference, so these effects are deliberately short, conservative, and non-repeating, with durable static error and completion states.

Duration timers can be restarted from their original full duration using the timer controls, context menu, or `Ctrl+R`. Restart is unavailable for absolute-time expressions because repeating a past wall-clock target is ambiguous. Restarting uses the same countdown transition and session-inhibition boundaries as the other timer commands.

Keyboard command parity includes Space outside editable text and `Ctrl+P` for pause/resume, `Ctrl+S` for stop, `Ctrl+R` for restart, Escape for cancel-edit then completion dismissal then full-screen exit, and `Alt+Enter` for full-screen mode. Enter and Return continue to start a timer from the expression editor. Full-screen state is temporary and restores the prior normal or maximized state.

The **Prompt on exit** context-menu option is enabled by default and persists across restarts. Closing through the title bar, context menu, or direct window close uses one coordinated path: a running or paused timer receives a native confirmation prompt, cancellation leaves it unchanged, and approval waits for queued settings writes before final cleanup. Stopped and expired timers close without a prompt.
