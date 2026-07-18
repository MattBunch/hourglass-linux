# Linux Wake Alarms

Milestone 9.1 adds a disabled-by-default wake-alarm service behind `Hourglass.Platform.IWakeAlarmService`.

The first Linux backend targets `/sys/class/rtc/rtc0/wakealarm`. It is deliberately conservative:

- wake scheduling is separate from session inhibition;
- the app schedules only the earliest running timer;
- the requested alarm is 15 seconds before timer expiry, but never less than 15 seconds in the future;
- an existing future RTC alarm is treated as owned by something else and is not overwritten;
- a scheduled alarm is read back before support is claimed;
- cancellation clears only the exact alarm value scheduled by Hourglass.

## Behavior

Wake from suspend remains off by default through `WakeFromSuspendEnabled = false` in `app.json`. Unsupported systems, permission failures, existing RTC alarms, and verification failures leave normal timer behavior unchanged. Notifications, sound, active-session restore, and completion state still handle timer expiry after resume.

The initial backend is intended for native, unsandboxed Linux environments where the user has permission to write the RTC wakealarm file. Flatpak, AppImage, systemd timer, and policy-helper behavior still need separate validation before wake support can be exposed in the UI.
