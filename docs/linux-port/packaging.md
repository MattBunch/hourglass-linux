# Linux Packaging Prototype

Stage 10 prepares Hourglass Linux for local publishing and future package work. It does not create release artifacts in the repository.

## Runtime Targets

The initial runtime target is `linux-x64`.

`linux-arm64` remains future work after the first publish path is stable.

## Local Publish

Use the release publish script for the supported runtime target:

```bash
scripts/publish-linux-release.sh --runtime linux-x64 --output /tmp/hourglass-linux-publish
packaging/appimage/build-appdir.sh /tmp/hourglass-linux-publish /tmp/hourglass-linux.AppDir
scripts/validate-linux-packaging.sh /tmp/hourglass-linux-publish /tmp/hourglass-linux.AppDir
```

The publish output is generated content and should not be committed.

Release publishes pass the current Git commit to the Avalonia project as `SourceRevisionId` when the source tree is inside a Git repository. About dialog diagnostics display that revision for packaged builds and fall back to `Local build` for source archives or non-Git local builds.

## Package Strategy

- Flatpak is the primary future package format because it provides cross-distro delivery and portal-oriented desktop integration.
- AppImage is the secondary portable package format for users who want a single-file app outside a store or repository.
- Snap, `.deb`, RPM, AUR, release signing, auto-update metadata, and CI release jobs are out of scope for this prototype.

## Prototype Files

- `packaging/flatpak/io.github.MattBunch.Hourglass.yml` is a draft Flatpak manifest.
- `packaging/linux/io.github.MattBunch.Hourglass.desktop` is desktop launcher metadata.
- `packaging/linux/io.github.MattBunch.Hourglass.metainfo.xml` is draft AppStream metadata.
- `packaging/appimage/build-appdir.sh` assembles an AppDir from a publish output directory.
- `scripts/publish-linux-release.sh` produces the self-contained `linux-x64` publish directory.
- `scripts/validate-linux-packaging.sh` checks publish and AppDir layout plus package metadata.

## Permissions

The app currently needs:

- desktop notifications for timer completion
- bundled timer audio at `Assets/Sounds/BeepNormal.wav`
- user configuration storage under XDG config paths
- session inhibition for keep-awake behavior

The current `systemd-inhibit` backend is suitable for unpackaged developer builds. A portal backend should be evaluated before Flatpak is treated as production-ready.

Audio playback uses command-line players in developer and AppImage-style builds: `pw-play`, `paplay`, then `aplay --quiet`. The AppImage prototype copies the full publish directory, so the bundled WAV file is included automatically.

The Flatpak prototype installs the packaged WAV assets beside the app binary. The command-line player backend is not guaranteed to work inside a Flatpak sandbox unless the runtime exposes the required tools and audio session access. Treat Flatpak audio as a packaging validation item, not proven production behavior.

Single-instance behavior uses a per-user XDG lock file. Native developer builds and AppImage builds share the host user's XDG runtime/cache namespace. Flatpak builds may use a sandbox-specific namespace, so this phase does not guarantee single-instance ownership across native/AppImage and Flatpak package boundaries. Desktop-file activation forwarding is not implemented yet.

## Updates And Privacy

Linux packages must not reuse the legacy Windows in-app updater or its persistent UUID behavior. Package channels should own updates where possible, and any future AppImage update metadata must be designed separately from the Windows updater. The detailed update policy lives in [update-strategy.md](update-strategy.md).

## CI Artifacts

The test workflow builds release packaging artifacts for pull requests and pushes after restore, Release build, full tests, and `dotnet format --verify-no-changes` succeed. Modern Linux builds run with warnings treated as errors in CI. GitHub Actions are pinned to immutable SHAs with comments showing the corresponding action release tag.

The packaging job uploads:

- a self-contained `linux-x64` publish tarball;
- an AppDir tarball assembled from that publish output.

CI artifacts are validation outputs, not public releases.

CI package validation checks static publish/AppDir layout, desktop metadata, and AppStream metadata. It does not validate a real desktop session, Flatpak runtime audio capability, notification delivery, status notifier availability, system suspend, or RTC hardware behavior.
