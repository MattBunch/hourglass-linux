# Linux Port Risks

## Avalonia And .NET 10 Compatibility

The scaffold targets `net10.0` and uses Avalonia packages. If Avalonia package restore or build fails specifically because of `net10.0` compatibility, evaluate the latest compatible stable Avalonia package before considering any target framework downgrade.

## GNOME Tray Absence

GNOME does not reliably expose a traditional tray by default. The Linux UX must work without tray support. Notifications and recoverable app state should be the first path.

## Wayland Activation And Focus

Wayland compositors limit arbitrary window activation and focus stealing. The app should request attention through supported toolkit mechanisms and notifications instead of assuming it can always force itself to the foreground.

## Wake From Suspend Parity Gap

The Windows app can schedule wake-from-suspend through Windows waitable timers. A portable Linux equivalent is not established for the MVP and may require distro-specific or privileged backends.

## Timer Drift And Suspend/Resume

The legacy app uses wall-clock time and a UI dispatcher timer. The Linux port needs a monotonic-clock timer engine with explicit tests for clock changes, long-running timers, suspend/resume, and timer expiry while minimized or backgrounded.

## Linux Packaging Permissions

Flatpak, AppImage, Snap, and distro packages have different permission models. Notification, audio, settings, startup, inhibition, and background behavior must be validated against the selected package model rather than assumed from an unpackaged developer build.

## In-App Updater And UUID Privacy

The Windows app has an in-app update model and persistent UUID behavior. Linux packaging should not carry this forward blindly. Package channels should own updates where possible, and any persistent identifier should be removed, disabled, or made explicit and privacy-reviewed.
