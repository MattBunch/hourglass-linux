# Linux Packaging Prototype

Stage 10 prepares Hourglass Linux for local publishing and future package work. It does not create release artifacts in the repository.

## Runtime Targets

The initial runtime target is `linux-x64`.

`linux-arm64` remains future work after the first publish path is stable.

## Local Publish

Restore the app project for the target runtime before publishing:

```bash
dotnet restore src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj --runtime linux-x64
dotnet publish src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --no-restore \
  --output /tmp/hourglass-linux-publish
```

The publish output is generated content and should not be committed.

## Package Strategy

- Flatpak is the primary future package format because it provides cross-distro delivery and portal-oriented desktop integration.
- AppImage is the secondary portable package format for users who want a single-file app outside a store or repository.
- Snap, `.deb`, RPM, AUR, release signing, auto-update metadata, and CI release jobs are out of scope for this prototype.

## Prototype Files

- `packaging/flatpak/io.github.MattBunch.Hourglass.yml` is a draft Flatpak manifest.
- `packaging/linux/io.github.MattBunch.Hourglass.desktop` is desktop launcher metadata.
- `packaging/linux/io.github.MattBunch.Hourglass.metainfo.xml` is draft AppStream metadata.
- `packaging/appimage/build-appdir.sh` assembles an AppDir from a `dotnet publish` output.

## Permissions

The app currently needs:

- desktop notifications for timer completion
- bundled timer audio at `Assets/Sounds/BeepNormal.wav`
- user configuration storage under XDG config paths
- session inhibition for keep-awake behavior

The current `systemd-inhibit` backend is suitable for unpackaged developer builds. A portal backend should be evaluated before Flatpak is treated as production-ready.

Audio playback uses command-line players in developer and AppImage-style builds: `pw-play`, `paplay`, then `aplay --quiet`. The AppImage prototype copies the full publish directory, so the bundled WAV file is included automatically.

The Flatpak prototype installs the WAV asset beside the app binary. The command-line player backend is not guaranteed to work inside a Flatpak sandbox unless the runtime exposes the required tools and audio session access. Treat Flatpak audio as a packaging validation item, not proven production behavior.

Single-instance behavior uses a per-user XDG lock file. Native developer builds and AppImage builds share the host user's XDG runtime/cache namespace. Flatpak builds may use a sandbox-specific namespace, so this phase does not guarantee single-instance ownership across native/AppImage and Flatpak package boundaries. Desktop-file activation forwarding is not implemented yet.

## Updates And Privacy

Linux packages must not reuse the legacy Windows in-app updater or its persistent UUID behavior. Package channels should own updates where possible, and any future AppImage update metadata must be designed separately from the Windows updater.
