# Linux Feature Parity

| Feature | Windows Status | Linux Scaffold Status | Linux Plan |
|---|---|---|---|
| Timer parsing | Implemented in legacy app | Extracted to `Hourglass.Core` with tests | Keep behavior stable while integrating the Linux UI. |
| Timer state | Implemented in legacy app | Core serialization DTOs and timer-start model extracted | Continue extracting platform-neutral timer/session state before settings migration. |
| Countdown timing | Uses WPF `DispatcherTimer` and `DateTime.Now` | Monotonic `CountdownEngine` implemented in `Hourglass.Core` | Use Avalonia timers only for UI refresh ticks. |
| Timer UI | WPF | Minimal Avalonia timer UI backed by `Hourglass.Core` | Expand behavior after MVP platform services are in place. |
| Notifications | Windows notification area balloon behavior | `INotificationService` with Linux `notify-send` backend | Keep notification delivery best-effort; app UI remains recoverable state. |
| Tray/status icon | WinForms `NotifyIcon` | Not implemented | Optional; abstract behind platform interfaces. |
| Keep awake | Windows execution state APIs | Not implemented | Add Linux session inhibition later. |
| Wake from suspend | Windows waitable timer resume behavior | Out of scope | Defer until after MVP. |
| Audio alerts | Windows-focused implementation | Not implemented | Add Linux audio alert service later. |
| Settings | .NET Framework settings | Storage not migrated; some timer option DTOs extracted | Add Linux settings paths and migration plan later. |
| Single instance | Windows Forms application base | Not implemented | Add Linux single-instance service later. |
| Updates | Windows in-app update check | Not implemented | Disable or replace for Linux packaging. |
| Packaging | MSI, bundle, portable Windows build | Not implemented | Flatpak first, AppImage second. |

The Linux MVP should not promise complete feature parity. It should prioritize a reliable native timer experience with notifications, sound, settings, and testable timing behavior.
