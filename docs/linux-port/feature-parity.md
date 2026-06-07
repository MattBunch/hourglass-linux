# Linux Feature Parity

| Feature | Windows Status | Linux Scaffold Status | Linux Plan |
|---|---|---|---|
| Timer parsing | Implemented in legacy app | Not extracted | Move reusable parsing into `Hourglass.Core`. |
| Timer state | Implemented in legacy app | Not extracted | Replace UI-bound timer logic with testable core state. |
| Countdown timing | Uses WPF `DispatcherTimer` and `DateTime.Now` | Not implemented | Use a monotonic-clock timer engine later. |
| Timer UI | WPF | Placeholder Avalonia window | Build native Avalonia UI after core extraction. |
| Notifications | Windows notification area balloon behavior | Not implemented | Use Linux notifications first. |
| Tray/status icon | WinForms `NotifyIcon` | Not implemented | Optional; abstract behind platform interfaces. |
| Keep awake | Windows execution state APIs | Not implemented | Add Linux session inhibition later. |
| Wake from suspend | Windows waitable timer resume behavior | Out of scope | Defer until after MVP. |
| Audio alerts | Windows-focused implementation | Not implemented | Add Linux audio alert service later. |
| Settings | .NET Framework settings | Not migrated | Add Linux settings paths and migration plan later. |
| Single instance | Windows Forms application base | Not implemented | Add Linux single-instance service later. |
| Updates | Windows in-app update check | Not implemented | Disable or replace for Linux packaging. |
| Packaging | MSI, bundle, portable Windows build | Not implemented | Flatpak first, AppImage second. |

The Linux MVP should not promise complete feature parity. It should prioritize a reliable native timer experience with notifications, sound, settings, and testable timing behavior.
