# Linux Audio Alerts

Phase 11 adds best-effort timer-expiry audio through `Hourglass.Platform.IAudioAlertService`.

## Built-In Sounds

The Linux app supports `None` plus the packaged built-in alert sounds:

- `resource:Loud beep`, published as `Assets/Sounds/BeepLoud.wav`
- `resource:Normal beep`, published as `Assets/Sounds/BeepNormal.wav`
- `resource:Quiet beep`, published as `Assets/Sounds/BeepQuiet.wav`

The WAV files are copied into the modern Avalonia project so installed Linux builds do not depend on the legacy Windows project directory. Arbitrary user-selected audio files are deferred until Flatpak, portal, and file-permission behavior can be made consistent.

## Playback Backend

`LinuxAudioAlertService` distinguishes three states:

- the packaged sound asset exists;
- a supported playback executable is available;
- a playback attempt succeeds.

Sound selection and preview require both a sound asset and a selected backend, except `None`, which remains selectable without a backend.

At service construction the native backend probe selects the first available command-line player in this order:

1. `pw-play`
2. `paplay`
3. `aplay --quiet`

Playback uses that selected backend rather than probing every command on every alert. Missing executables, missing audio devices, muted sessions, unavailable audio servers, non-zero exits, and missing sound files are treated as best-effort failures and do not crash the app.

Cancellation from the service cancellation token is still propagated as `OperationCanceledException`.

## Desktop Behavior

The visible `Timer complete` state remains the source of truth. Audio is an additional expiry effect, independent from notifications.

- GNOME: playback depends on the active PipeWire/PulseAudio session and user volume or mute state.
- KDE Plasma: playback depends on the active PipeWire/PulseAudio session and per-application volume policy.
- XFCE: playback depends on the installed player commands and the active PulseAudio/PipeWire/ALSA setup.

Desktop mute state, per-application volume, missing output devices, and session audio-server policy are outside Hourglass control for this MVP.

## Packaging

AppImage builds copy the full publish directory, including every packaged WAV under `Assets/Sounds/`.

The Flatpak prototype installs the bundled WAV files, but copied assets alone do not prove audio support. Command-based playback inside a Flatpak sandbox depends on the selected command existing in the runtime and on sandbox access to the host audio service. Until that runtime dependency and permission set are validated, Flatpak audio support must be treated as best-effort/unsupported when no backend is detected.

## Settings

Linux settings include:

```json
{
  "AudioAlertsEnabled": true,
  "AudioAlertSoundId": "resource:Normal beep"
}
```

Audio alerts default to `true` and the Normal beep. Existing settings JSON that omits `AudioAlertsEnabled` or `AudioAlertSoundId` continues to load with audio alerts enabled and the default sound. Selecting `None` persists audio alerts as disabled with `AudioAlertSoundId` set to `none`.
