# Linux Port Architecture

Hourglass uses a functional core and imperative shell. The legacy Windows WPF
solution remains reference material; Linux work uses `Hourglass.Linux.sln`.

## Project boundaries

- Core: immutable countdown transitions, parsing, domain/settings snapshots and serialization mappings.
- Platform: narrow interfaces for clocks, storage and desktop/OS capabilities.
- Application: one TimerSession per engine; logical sessions, serialized commands/ticks, effects, persistence and client leases.
- Linux.Services: platform implementations, XDG paths, runtime election, local protocol and executable discovery.
- Avalonia: GUI bindings, windows, geometry, tray and desktop projections.
- CLI: scriptable commands, plain/JSON output and frontend launch adapters.
- TUI: terminal presentation, input, focus and cleanup; Terminal.Gui stays here.
- Host: headless composition, isolated descriptors, lifetime and shutdown.

References flow from frontends through Application and services to Core/Platform.
Application references Core/Platform, never Linux.Services or a presentation toolkit.
CLI and Host have no Avalonia or Terminal.Gui dependency. Architecture tests
protect direct and resolved dependency boundaries.

## Session and effect ownership

Exactly one internal TimerSession owns each CountdownEngine. The authoritative
GUI or on-demand Host runtime advances engines; clients consume immutable
snapshots and issue commands. Closing a presentation does not transfer engines.
Client leases and Gui/Foreground/Tui/Detached lifetime rules are described in
[runtime control](runtime-control.md). Detached paused/stopped/expired records
remain retained until dismissed or configured removal.

The authority is the shared-state writer. Existing XDG settings, saved timers,
recents, custom themes and active-session documents remain shared across all
frontends. Recovery preserves GUI records until GUI initialization and restores
eligible detached records in Host. Optional lifetime metadata defaults legacy
records to GUI ownership. Frontend edits remain local until a revision-checked
application command commits them.

Notifications, audio, inhibition and wake scheduling use platform interfaces.
Slow effects execute outside the serialized mutation queue and validate session
revisions before completing. Resources, subscriptions and pending saves are
owned and disposed explicitly. See [notifications](notifications.md),
[audio](audio-alerts.md), [inhibition](session-inhibition.md) and [wake alarms](wake-alarms.md).

## Presentation and distribution

Avalonia publications cross its UI dispatcher. Terminal views marshal to their
own event loop. TUI repaint frequency and accessible mode never change engine
timing. CLI plain/JSON output provides a noninteractive accessibility fallback.

The GUI keeps its existing archive/AppImage and desktop integration. Terminal
archives contain separate self-contained CLI/TUI/Host payloads for x64 and arm64.
Shared application version metadata is imported from `build/Hourglass.Version.props`.
See [packaging](packaging.md) and [terminal installation](../terminal-installation.md).

CI exercises solution gates, native terminal archives, PTYs, Xvfb GUI lifetimes
and GUI packaging. These do not establish attended terminal, screen-reader,
notification/audio-server, suspend/resume or RTC hardware behavior.
