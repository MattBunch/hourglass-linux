# Linux Port Architecture

The Linux port will be a native C#/.NET application using Avalonia for the Linux UI. The original Windows WPF project stays intact during early development.

## Project Boundaries

- `Hourglass.Core` contains platform-neutral logic: parsing, timer state, timer models, settings models, serialization models, and testable countdown logic.
- `Hourglass.Platform` contains interfaces for services that vary by operating system or desktop environment.
- `Hourglass.Linux.Services` contains Linux-specific implementations of platform services.
- `Hourglass.Linux.Avalonia` contains the native Linux UI shell.
- `Hourglass.Core.Tests` contains modern unit tests for platform-neutral behavior.

## Dependency Direction

`Hourglass.Core` has no dependency on UI or platform services. `Hourglass.Platform` can reference core models, but not Linux implementation details. `Hourglass.Linux.Services` implements platform interfaces. `Hourglass.Linux.Avalonia` composes the UI and Linux services.

## Extraction Strategy

The first real porting phase should extract reusable parsing, serialization, timer state, and settings models from the existing app where practical. The extraction should happen before attempting full UI parity.

The current Windows implementation relies on WPF `DispatcherTimer` and `DateTime.Now`. The Linux port now has a testable core countdown engine built around a monotonic clock, with wall-clock time reserved for display and absolute scheduling semantics.

## Platform Services

The platform layer should eventually cover notifications, optional tray/status notifier support, session inhibition, audio alerts, settings paths, single-instance behavior, startup integration, and wake alarms.

Timer-expiry notifications are routed through `INotificationService`; the Linux implementation is documented in [notifications.md](notifications.md). Notification delivery is best-effort and must not be the only recoverable timer-completion state.

Linux settings storage is routed through `ISettingsStore`; the storage location and privacy decisions are documented in [settings.md](settings.md).

Keep-awake behavior is routed through `ISessionInhibitor`; the Linux implementation is documented in [session-inhibition.md](session-inhibition.md). Inhibition is best-effort and is separate from wake-from-suspend scheduling.

Wake-from-suspend scheduling is explicitly out of scope for the Linux MVP. It may require distro-specific or privilege-sensitive backends and should be evaluated after the core Linux app is usable.

Packaging prototypes live under `packaging/`; publishing and permissions are documented in [packaging.md](packaging.md).
