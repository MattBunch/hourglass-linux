# Linux Port Decisions

## Native Linux Port

Use a native Linux C#/.NET port rather than Wine or a full rewrite in another language.

## .NET 10

Use `net10.0` for all new Linux scaffold projects. .NET 10 is the current LTS line for a new 2026 port. Do not target .NET 9 because it is STS and does not provide a useful support-lifetime advantage here.

## Avalonia UI

Use Avalonia for the native Linux UI shell. It keeps the implementation in C#/.NET and provides a practical migration path from the existing WPF/XAML application model without rewriting the whole app in another toolkit.

## Preserve Windows App

Keep the existing Windows WPF `.NET Framework 4.8` app intact during early Linux work. Do not rewrite or delete the original source tree.

## Extract Core First

Extract parsing, serialization, timer state, and settings models before trying to build complete UI parity.

## Timer Engine

Replace the existing WPF `DispatcherTimer` and `DateTime.Now` timing model later with a testable monotonic-clock timer engine.

## Notifications Before Tray

Use Linux notifications first. Treat tray/status notifier support as optional for the first Linux UI.

## Session Inhibition

Use Linux session inhibition later for keep-awake behavior.

## Wake From Suspend

Defer wake-from-suspend support until after MVP. It is not required for the initial Linux port.

## Linux Updates And Packaging

Disable or replace the Windows in-app update model for Linux packaging. Plan Flatpak as the primary future Linux package and AppImage as a secondary portable artifact.
