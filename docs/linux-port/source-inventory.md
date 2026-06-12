# Linux Source Inventory

This inventory backfills the Stage 2 source review for the Linux port. It records what has already been extracted into the modern solution and what should remain behind platform-specific boundaries.

## Already Extracted

| Area | Legacy Source | Linux/Core Target | Risk | Notes |
|---|---|---|---|---|
| Timer parsing and tokens | `Hourglass/Parsing/*` | `src/Hourglass.Core/Parsing/*` | Low | Platform-neutral parser and token logic has been copied into `Hourglass.Core` with modern tests. |
| Parser resources | `Hourglass/Properties/Resources.resx` | `src/Hourglass.Core/Properties/Resources.resx` | Low | Core owns the resources required by parser behavior. Future UI strings should stay outside core. |
| Core parsing extensions | `Hourglass/Extensions/CultureInfoExtensions.cs`, `DateTimeExtensions.cs`, `DayOfWeekExtensions.cs`, `MathExtensions.cs`, `ResourceManagerExtensions.cs`, `TimeSpanExtensions.cs` | `src/Hourglass.Core/Extensions/*` | Low | These helpers are platform-neutral and support parser and timing behavior. |
| Timer start serialization | `Hourglass/Serialization/TimerStartInfo.cs`, `TimerStartInfoList.cs` | `src/Hourglass.Core/Serialization/*` | Low | XML DTOs used by timer start state are now available to core tests. |
| Timer state serialization | `Hourglass/Serialization/TimerInfo.cs`, `TimerInfoList.cs`, `TimerOptionsInfo.cs`, `WindowSizeInfo.cs` | `src/Hourglass.Core/Serialization/*` | Medium | DTO shape is reusable, but final Linux settings/storage semantics still need a platform plan. |
| Timer start model | `Hourglass/Timing/TimerStart.cs` | `src/Hourglass.Core/Timing/TimerStart.cs` | Low | Reuses parser/token behavior and remains independent of WPF. |
| Window title mode model | `Hourglass/Timing/TimerOptions.cs` | `src/Hourglass.Core/Timing/WindowTitleMode.cs` | Low | Extracted as a small platform-neutral enum needed by timer options state. |
| Countdown engine | Replacement for UI-owned timer truth in `Hourglass/Timing/Timer.cs` and `Hourglass/Windows/TimerWindow.xaml.cs` | `src/Hourglass.Core/Timing/CountdownEngine.cs` | Medium | New monotonic-clock engine is testable and independent of Avalonia/WPF. |

## Pending Reusable Candidates

| Area | Legacy Source | Recommended Target | Risk | Notes |
|---|---|---|---|---|
| Timer options model | `Hourglass/Timing/TimerOptions.cs` | `Hourglass.Core` | Medium | Extract only platform-neutral option data. UI bindings, settings persistence, and notification behavior should stay out of core. |
| Theme model data | `Hourglass/Timing/Theme.cs`, `Hourglass/Serialization/ThemeInfo.cs`, `ThemeInfoList.cs` | `Hourglass.Core` | Medium | Theme data can be reused, but WPF brush/color handling and Avalonia styling need a separate mapping layer. |
| Sound model data | `Hourglass/Timing/Sound.cs` | `Hourglass.Core` plus platform audio service | Medium | Keep identifiers and user choices in core/settings; playback belongs in `Hourglass.Platform` and Linux services. |
| Settings shape | `Hourglass/Properties/Settings.*`, `Hourglass/Managers/SettingsManager.cs`, `TimerOptionsManager.cs`, `TimerStartManager.cs`, `ThemeManager.cs` | `Hourglass.Core` data plus `Hourglass.Platform` storage abstractions | High | Legacy .NET Framework settings should not be copied directly. Extract stable data contracts first, then add Linux config/data path handling. |
| Timer list/session state | `Hourglass/Managers/TimerManager.cs`, `Hourglass/Timing/Timer.cs`, serialization DTOs | `Hourglass.Core` | High | Keep domain state reusable, but avoid carrying over WPF dispatcher, window, and manager coupling. |
| Command-line timer inputs | `Hourglass/CommandLineArguments.cs` | `Hourglass.Core` or app composition layer | Medium | Parsing may be reusable, but app activation and single-instance behavior are platform-specific. |

## Replacement Required

| Area | Legacy Source | Linux Direction | Risk | Notes |
|---|---|---|---|---|
| WPF windows and controls | `Hourglass/Windows/*`, `*.xaml` | Avalonia UI in `src/Hourglass.Linux.Avalonia` | High | Do not port directly. Rebuild minimal timer workflows against core models first. |
| UI-owned timing | `Hourglass/Windows/TimerWindow.xaml.cs`, `DispatcherTimer` usage | Avalonia refresh ticks backed by `CountdownEngine` | High | UI ticks should refresh presentation only; core owns elapsed/remaining truth. |
| Notification area icon | `Hourglass/Managers/NotificationAreaIconManager.cs`, `Hourglass/Windows/NotificationAreaIcon.cs` | `INotificationService` first, optional tray service later | High | GNOME may not expose a tray by default. Linux MVP must work notification-first. |
| Keep-awake behavior | `Hourglass/Managers/KeepAwakeManager.cs`, `NativeMethods.cs` | `ISessionInhibitor` implementation | High | Windows execution-state APIs need Linux desktop/session-specific replacements. |
| Wake timers | `Hourglass/Managers/WakeUpManager.cs`, `NativeMethods.cs` | Defer behind `IWakeAlarmService` | High | Wake-from-suspend is post-MVP and may require distro-specific or privileged backends. |
| Audio playback | `Hourglass/Managers/SoundManager.cs`, `Hourglass/Windows/SoundPlayer.cs` | `IAudioAlertService` implementation | Medium | Core can preserve sound choices, but playback implementation is platform-specific. |
| Single instance and startup | `Hourglass/App.cs`, `AppEntry.cs`, Windows Forms application-base behavior | `ISingleInstanceService` and `IStartupIntegrationService` | High | Linux activation and startup integration need separate implementation and Wayland-aware behavior. |
| Updates | `Hourglass/Managers/UpdateManager.cs`, `Hourglass/Serialization/UpdateInfo.cs` | Disable or replace for Linux packaging | Medium | Flatpak/AppImage/package channels should own updates where possible. |
| Installers and bundles | `Hourglass.Setup/*`, `Hourglass.Bundle/*` | Flatpak first, AppImage second | Medium | Windows installer projects are not reusable for Linux packaging. |

## Recommended Extraction Order

1. Finish timer option and session-state data contracts in `Hourglass.Core`, keeping persistence and UI concerns out of the model.
2. Extract theme and sound data models only after deciding the minimal UI settings surface needed for the Linux MVP.
3. Add Linux settings storage through `Hourglass.Platform` and `Hourglass.Linux.Services` before migrating user-facing settings behavior.
4. Build the minimal Avalonia timer UI against `CountdownEngine` and the existing parser before adding advanced parity features.
5. Add notifications, audio, session inhibition, and single-instance behavior as platform services instead of direct ports from Windows managers.

## Stage 2 Outcome

Stage 2 is now documented after the fact. The repo has already moved beyond inventory into parser extraction, serialization extraction, tests, and the monotonic countdown engine. This document should be treated as the source map for the next cleanup and extraction steps, not as a request to reorder already-completed work.
