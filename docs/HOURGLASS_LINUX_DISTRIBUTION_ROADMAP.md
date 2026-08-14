---
title: "Hourglass Linux Distribution Roadmap"
project: "Hourglass Linux Port"
repository: "https://github.com/MattBunch/hourglass-linux"
default_branch: "develop"
document_type: "implementation-roadmap"
status: "active"
last_updated: "2026-08-13"
application_id: "io.github.MattBunch.Hourglass"
executable: "hourglass-linux"
initial_release_version: "0.1.0"
---

# Hourglass Linux Distribution Roadmap

## Purpose

This roadmap defines the work required to publish Hourglass Linux through major Linux software channels. It is structured for both human tracking and execution by Codex or another coding agent.

The intended distribution order is:

1. GitHub Releases with portable Linux artifacts.
2. AppImage.
3. Flathub / Flatpak.
4. Fedora COPR / RPM.
5. Arch User Repository.
6. Upstream Nix flake.
7. Nixpkgs.
8. Debian and Ubuntu `.deb`.
9. Signed APT repository.
10. Official Fedora and Debian repositories.

---

# 1. Roadmap Conventions

## Checkbox meanings

- `[ ]` Not started.
- `[x]` Complete and validated.
- `[-]` In progress.
- `[!]` Blocked.
- `[~]` Deferred.
- `[?]` Requires investigation.

Only mark an item complete after its acceptance criteria have been met.

## Codex implementation rules

- Work from `develop` unless explicitly instructed otherwise.
- Create a dedicated branch for each milestone or isolated workstream.
- Do not make unrelated refactors during packaging work.
- Keep packaging changes under `packaging/`, `scripts/`, `.github/workflows/`, `docs/`, or `nix/` unless application changes are genuinely required.
- Preserve the existing Windows solution and legacy Windows projects.
- Use `Hourglass.Linux.sln` for Linux packaging work.
- Do not commit publish directories, package artifacts, NuGet caches, build outputs, AppDirs, repository indexes, or private signing material.
- Pin external GitHub Actions to full commit hashes.
- Pin package inputs to immutable Git tags, commit hashes, and checksums.
- Keep package names, application IDs, executable names, desktop metadata, icons, and AppStream metadata consistent.
- Run tests and package validators before completing each milestone.
- Update this roadmap as work progresses.
- Do not introduce a Linux in-app updater. Package channels own updates.
- Do not silently download or execute replacement binaries.
- Preserve original Hourglass attribution and MIT license notices.

## Pull request expectations

Each milestone should normally be implemented in its own pull request. Every packaging pull request should include:

- Summary of changes.
- Package formats affected.
- Commands run.
- Test and validator results.
- Known limitations.
- Screenshots when graphical metadata changed.
- Remaining follow-up work.

Recommended branch names:

```text
feature/release-pipeline
feature/appimage-release
feature/flathub-packaging
feature/rpm-copr
feature/aur-package
feature/nix-packaging
feature/debian-package
feature/apt-repository
docs/distribution-guide
```

---

# 2. Current Repository Baseline

## Existing application metadata

- [x] Application ID is `io.github.MattBunch.Hourglass`.
- [x] Linux executable is `hourglass-linux`.
- [x] Linux application targets .NET 10.
- [x] Application version is defined in the Linux `.csproj`.
- [x] Repository URL is included in assembly metadata.
- [x] Developer website is included in assembly metadata.
- [x] Original Hourglass attribution is included.
- [x] Source revision metadata can be embedded into release builds.

## Existing Linux desktop integration

- [x] Desktop entry exists under `packaging/linux/`.
- [x] Desktop entry launches `hourglass-linux`.
- [x] Desktop entry uses the Utility category.
- [x] PNG application icon exists.
- [x] SVG application icon exists.
- [x] Initial AppStream metadata exists.
- [x] AppStream metadata includes project and metadata licenses.
- [x] AppStream metadata includes homepage, repository, and issue tracker URLs.
- [x] AppStream metadata declares the desktop launchable and binary.
- [x] AppStream metadata includes an initial release entry.
- [x] AppStream metadata includes an OARS content rating.

## Existing publish and validation infrastructure

- [x] Self-contained `linux-x64` publish script exists.
- [x] Publish script rejects dirty Git working trees.
- [x] Publish script embeds source revision when available.
- [x] Publish script currently restricts releases to `linux-x64`.
- [x] AppDir assembly script exists.
- [x] AppDir includes application files, desktop metadata, icons, metainfo, and `AppRun`.
- [x] Packaging validation script exists.
- [x] CI installs AppStream and desktop-file validators.
- [x] CI validates publish and AppDir layouts.
- [x] CI uploads a self-contained Linux publish tarball.
- [x] CI uploads an AppDir tarball.
- [ ] CI produces a final `.AppImage`.
- [ ] CI creates public GitHub Releases.
- [ ] CI produces `.deb` packages.
- [ ] CI produces RPM packages.
- [ ] CI produces checksums.
- [ ] CI signs release artifacts.

## Existing Flatpak prototype

- [x] Initial Flatpak manifest exists.
- [x] Manifest installs binary, sounds, desktop metadata, AppStream metadata, and icons.
- [x] Manifest declares Wayland and fallback X11 support.
- [x] Manifest declares notification access.
- [ ] Manifest builds from an immutable Git tag and commit.
- [ ] Manifest supports Flathub's offline build environment.
- [ ] Manifest includes generated offline NuGet sources.
- [ ] Manifest uses a currently supported Flathub runtime.
- [ ] Flatpak audio is proven to work inside the sandbox.
- [ ] Flathub metadata requirements are fully satisfied.
- [ ] Flathub submission has been opened.
- [ ] Flathub submission has been accepted.

---

# 3. Target Distribution Matrix

| Channel | Package type | Target user command | Priority | Initial architecture |
|---|---|---|---:|---|
| GitHub Releases | `.tar.gz` | Manual download | P0 | x86_64 |
| AppImage | `.AppImage` | Run downloaded executable | P0 | x86_64 |
| Flathub | Flatpak | `flatpak install flathub io.github.MattBunch.Hourglass` | P0 | x86_64 initially |
| Fedora COPR | RPM repository | `sudo dnf install hourglass-linux` | P1 | x86_64 |
| Arch AUR | `PKGBUILD` | `yay -S hourglass-linux-bin` | P1 | x86_64 |
| Upstream Nix flake | Nix package | `nix run github:MattBunch/hourglass-linux` | P1 | x86_64-linux |
| Nixpkgs | Nix package | `nix profile install nixpkgs#hourglass-linux` | P2 | x86_64-linux |
| Direct Debian package | `.deb` | `sudo apt install ./hourglass-linux_*.deb` | P1 | amd64 |
| Hosted APT repository | Debian repository | `sudo apt install hourglass-linux` | P2 | amd64 |
| Official Fedora | RPM | `sudo dnf install hourglass-linux` | P3 | Distribution-managed |
| Official Debian/Ubuntu | Debian source package | `sudo apt install hourglass-linux` | P3 | Distribution-managed |

---

# 4. Milestone 0 — Release Readiness Audit

## Goal

Confirm that the application is technically and legally ready for public distribution.

Use `docs/linux-port/release-validation-checklist.md` as the execution record
for this audit. Keep unrun manual desktop, package, multi-monitor, and
wake-from-suspend coverage as `Not run` or `Skipped` with a reason until an
exact environment has been validated.

## Checklist

### Application stability

- [ ] Run `dotnet restore Hourglass.Linux.sln`.
- [ ] Run Release build with warnings as errors.
- [ ] Run all Linux solution tests in Release configuration.
- [ ] Run formatting verification.
- [ ] Launch from a clean self-contained publish directory.
- [ ] Confirm timer creation works.
- [ ] Confirm start, pause, resume, restart, and stop work.
- [ ] Confirm timer expiry works.
- [ ] Confirm notifications work under GNOME Wayland.
- [ ] Confirm bundled sounds work in unpackaged builds.
- [ ] Confirm saved settings survive restart.
- [ ] Confirm active timer restoration works.
- [ ] Confirm multi-window behavior works.
- [ ] Confirm single-instance behavior works.
- [ ] Confirm About dialog version and build metadata are correct.
- [ ] Confirm clean application shutdown.
- [ ] Confirm no absolute development paths are embedded.

### Legal and attribution

- [ ] Confirm root `LICENSE.md` exists.
- [ ] Confirm the Linux port may be distributed under MIT.
- [ ] Confirm original Hourglass attribution is visible.
- [ ] Review third-party license obligations.
- [ ] Generate or review a third-party dependency license report.
- [ ] Confirm bundled sounds may be redistributed.
- [ ] Confirm application icons may be redistributed.
- [ ] Add `docs/THIRD_PARTY_NOTICES.md` if required.

### Metadata consistency

- [ ] Confirm application ID is consistently `io.github.MattBunch.Hourglass`.
- [ ] Confirm desktop filename matches the application ID.
- [ ] Confirm AppStream component ID matches the application ID.
- [ ] Confirm Flatpak application ID matches the application ID.
- [ ] Confirm `Exec=hourglass-linux`.
- [ ] Confirm binary name is `hourglass-linux`.
- [ ] Confirm icon names resolve in native and sandboxed packages.
- [ ] Confirm version and release date are correct.
- [ ] Confirm homepage, repository, and bug tracker URLs are correct.
- [ ] Confirm application description matches current features.

## Acceptance criteria

- [ ] All Release tests pass.
- [ ] Metadata validators pass.
- [ ] No unresolved legal or redistribution issue remains.
- [ ] The application runs from a clean self-contained publish directory.
- [ ] The intended first public version is documented.

---

# 5. Milestone 1 — Versioning and Release Source of Truth

## Goal

Use one reliable version model across binaries, AppStream metadata, package recipes, Git tags, and artifacts.

## Checklist

### Version model

- [x] Decide that the Linux `.csproj` is the canonical version source.
- [x] Document versioning in `docs/releasing.md`.
- [x] Use semantic versions such as `0.1.0`.
- [x] Use Git tags such as `v0.1.0`.
- [x] Define prerelease format such as `v0.2.0-beta.1`.
- [x] Define RPM release-number handling.
- [x] Define Debian revision handling.
- [x] Define AUR `pkgrel` handling.
- [x] Define Nix source-hash update handling.

### Automated validation

- [x] Add a script that reads the version from the Linux `.csproj`.
- [x] Add a validator that reads the latest AppStream release version.
- [x] Validate that the Git tag equals `v` plus the `.csproj` version.
- [x] Validate that AppStream version matches the `.csproj` version.
- [~] Validate package recipe versions when package recipes exist.
- [x] Fail CI version validation when existing metadata differs.
- [x] Add tests for version validation.

### Changelog

- [x] Create `CHANGELOG.md`.
- [x] Choose a consistent changelog format.
- [x] Add a `0.1.0` entry.
- [x] List features, packaging support, architecture support, and known limitations.

## Acceptance criteria

- [x] One command returns the intended release version.
- [x] CI rejects mismatched tags and metadata.
- [x] `CHANGELOG.md` contains the release entry.
- [x] Release documentation explains the version update process.

---

# 6. Milestone 2 — Storefront Metadata and Screenshots

## Goal

Prepare professional metadata for Flathub, GNOME Software, KDE Discover, and native package managers.

## Checklist

### AppStream metadata

- [ ] Validate component type and application ID.
- [ ] Validate metadata and project licenses.
- [ ] Review summary for clarity.
- [ ] Expand description into useful paragraphs.
- [ ] Confirm developer ID and name.
- [ ] Confirm homepage, repository, bug tracker, and help URLs.
- [ ] Add release history and release descriptions.
- [ ] Confirm content rating.
- [ ] Run `appstreamcli validate --no-net`.

### Screenshots

- [ ] Create `docs/screenshots/`.
- [ ] Capture main timer window.
- [ ] Capture a timer in progress.
- [ ] Capture context menu.
- [ ] Capture saved timers.
- [ ] Capture About dialog.
- [ ] Use a clean Linux desktop environment.
- [ ] Avoid unrelated desktop clutter and private information.
- [ ] Ensure screenshots remain readable at storefront scale.
- [ ] Add at least one default screenshot to AppStream metadata.
- [ ] Add captions.
- [ ] Pin screenshot URLs to an immutable tag or commit.
- [ ] Confirm screenshot URLs are public.

### Icons and desktop entry

- [ ] Confirm SVG and PNG icons render correctly.
- [ ] Confirm launcher padding is appropriate.
- [ ] Run `desktop-file-validate`.
- [ ] Confirm categories are appropriate.
- [ ] Confirm StartupWMClass behavior on X11.
- [ ] Confirm launcher grouping under GNOME Wayland.
- [ ] Confirm appearance in GNOME app search.
- [ ] Confirm appearance in KDE application menus when available.

## Acceptance criteria

- [ ] AppStream validation succeeds without errors.
- [ ] Desktop-entry validation succeeds without errors.
- [ ] At least two professional screenshots are published.
- [ ] Name, icon, summary, description, and screenshots are consistent.

---

# 7. Milestone 3 — Public GitHub Release Pipeline

## Goal

Produce public, immutable release artifacts whenever an approved version tag is pushed.

## Deliverables

```text
.github/workflows/release.yml
docs/releasing.md
SHA256SUMS
GitHub Release assets
```

## Checklist

### Workflow trigger and permissions

- [ ] Add `.github/workflows/release.yml`.
- [ ] Trigger only for tags matching `v*`.
- [ ] Set minimum required permissions.
- [ ] Grant `contents: write` only to the release job.
- [ ] Pin Actions to full commit hashes.
- [ ] Add concurrency controls.
- [ ] Prevent duplicate releases for one tag.

### Build validation

- [ ] Check out tagged source.
- [ ] Install .NET 10 SDK.
- [ ] Run version consistency validation.
- [ ] Restore dependencies.
- [ ] Build with warnings as errors.
- [ ] Run tests.
- [ ] Run formatting verification.
- [ ] Install metadata validators.
- [ ] Validate desktop and AppStream metadata.

### Publish artifacts

- [ ] Publish self-contained `linux-x64`.
- [ ] Embed source revision.
- [ ] Use deterministic versioned filenames.
- [ ] Create `hourglass-linux-<version>-linux-x64.tar.gz`.
- [ ] Confirm archive extracts cleanly.
- [ ] Confirm extracted executable launches.
- [ ] Generate `SHA256SUMS`.
- [ ] Store release assets under `dist/`.

### GitHub Release

- [ ] Create release from pushed tag.
- [ ] Use changelog content or generated release notes.
- [ ] Mark prerelease tags correctly.
- [ ] Upload tarball.
- [ ] Upload AppImage after Milestone 4.
- [ ] Upload `.deb` after Milestone 10.
- [ ] Upload RPM after Milestone 6 if desired.
- [ ] Upload checksums.
- [ ] Fail when expected artifacts are missing.

### Supply chain

- [ ] Consider GitHub artifact attestations.
- [ ] Record source revision.
- [ ] Avoid mutable URLs in downstream recipes.
- [ ] Decide whether to sign Git tags.
- [ ] Decide whether to sign release artifacts.
- [ ] Document signature verification if enabled.

## Acceptance criteria

- [ ] A test tag creates a prerelease successfully.
- [ ] Assets are publicly downloadable.
- [ ] Checksums verify.
- [ ] Release can be reproduced from tagged source.
- [ ] A clean Fedora or Ubuntu VM runs the tarball.

---

# 8. Milestone 4 — AppImage Release

## Goal

Create a portable `.AppImage` for direct download from GitHub Releases.

## Deliverables

```text
packaging/appimage/build-appimage.sh
dist/Hourglass-<version>-x86_64.AppImage
AppImage validation in CI
```

## Checklist

### AppDir review

- [ ] Confirm `AppRun` exists and uses relative paths.
- [ ] Confirm application files and sounds are included.
- [ ] Confirm desktop entry, metainfo, and icons are included.
- [ ] Confirm root desktop-entry symlink and `.DirIcon` exist.
- [ ] Confirm executable permissions.

### AppImage tooling

- [ ] Add `packaging/appimage/build-appimage.sh`.
- [ ] Pin AppImage tooling version and checksum.
- [ ] Avoid unpinned latest downloads.
- [ ] Pass `ARCH=x86_64` explicitly.
- [ ] Generate a versioned filename.
- [ ] Mark final AppImage executable.
- [ ] Generate checksum.

### Compatibility testing

- [ ] Test Fedora GNOME Wayland.
- [ ] Test Ubuntu GNOME.
- [ ] Test Arch Linux.
- [ ] Test KDE Plasma if available.
- [ ] Test without .NET installed.
- [ ] Confirm icon, notifications, sound, settings, and single-instance behavior.
- [ ] Confirm running from a read-only directory.
- [ ] Confirm paths containing spaces do not break launch.
- [ ] Confirm multiple versions do not corrupt settings.

### Release integration

- [ ] Add AppImage build to release workflow.
- [ ] Upload AppImage directly, not inside ZIP.
- [ ] Include AppImage in checksums.
- [ ] Add README instructions.
- [ ] Document executable permissions and FUSE troubleshooting.
- [ ] Defer AppImageUpdate until signing and update hosting are designed.

## Acceptance criteria

- [ ] AppImage launches on at least three distributions.
- [ ] No separate .NET installation is required.
- [ ] Notifications and audio work.
- [ ] GitHub Release includes AppImage and checksum.

---

# 9. Milestone 5 — Production Flatpak and Flathub

## Goal

Publish Hourglass Linux on Flathub so it appears in GNOME Software and KDE Discover when Flathub is enabled.

## Deliverables

```text
packaging/flatpak/io.github.MattBunch.Hourglass.yml
packaging/flatpak/nuget-sources.json
packaging/flatpak/flathub.json
Flathub submission pull request
```

## Checklist

### Immutable source model

- [ ] Replace local `type: dir` source.
- [ ] Fetch source from public Git repository.
- [ ] Pin release tag.
- [ ] Pin exact commit hash.
- [ ] Verify tag and commit match.
- [ ] Avoid mutable branches.

### Runtime and SDK

- [ ] Select the current supported Freedesktop runtime.
- [ ] Select matching SDK.
- [ ] Add .NET 10 SDK extension.
- [ ] Configure .NET SDK and library paths.
- [ ] Confirm x86_64 support.
- [ ] Add `flathub.json` architecture restriction if arm64 is unsupported.

### Offline NuGet restore

- [ ] Add or document `flatpak-dotnet-generator`.
- [ ] Generate `nuget-sources.json`.
- [ ] Include direct and transitive packages.
- [ ] Confirm checksums.
- [ ] Test with network disabled.
- [ ] Document regeneration command.
- [ ] Regenerate when package versions change.

### Flatpak build

- [ ] Build inside Flatpak environment.
- [ ] Use Release configuration.
- [ ] Install application under `/app/lib/hourglass-linux`.
- [ ] Create `/app/bin/hourglass-linux`.
- [ ] Install desktop entry, metainfo, icons, and sounds.
- [ ] Preserve executable permissions.
- [ ] Avoid development files and host-path writes.

### Sandbox permissions

- [ ] Keep Wayland and fallback X11 access.
- [ ] Keep shared IPC only if required.
- [ ] Add audio socket access.
- [ ] Confirm notification access.
- [ ] Prefer portals.
- [ ] Avoid broad filesystem access.
- [ ] Review and document every finish argument.
- [ ] Confirm settings use the Flatpak XDG namespace.
- [ ] Confirm single-instance behavior inside sandbox.

### Audio compatibility

- [ ] Determine whether runtime audio commands exist.
- [ ] Do not assume host `pw-play`, `paplay`, or `aplay` is available.
- [ ] Prefer in-process audio for Flatpak.
- [ ] Confirm preview, looping, stopping, mute, and device failure behavior.
- [ ] Add Flatpak-specific tests where practical.

### Notification and window behavior

- [ ] Confirm notifications on GNOME.
- [ ] Confirm notifications on KDE if available.
- [ ] Confirm expiry behavior while minimized.
- [ ] Confirm pop-up-on-expiry behavior under Wayland.
- [ ] Document compositor limitations.

### Local validation

- [ ] Build from clean checkout.
- [ ] Install locally.
- [ ] Run with final application ID.
- [ ] Run `flatpak-builder-lint manifest`.
- [ ] Run `flatpak-builder-lint repo`.
- [ ] Validate AppStream metadata.
- [ ] Inspect effective permissions.
- [ ] Test uninstall, reinstall, and upgrade.

### Flathub submission

- [ ] Fork Flathub repository.
- [ ] Create branch from required submission branch.
- [ ] Add manifest, NuGet sources, and `flathub.json` at required locations.
- [ ] Open pull request titled `Add io.github.MattBunch.Hourglass`.
- [ ] Resolve automated build feedback.
- [ ] Resolve reviewer feedback.
- [ ] Prove ownership where requested.
- [ ] Accept maintainer invitation.
- [ ] Confirm stable build is published.
- [ ] Confirm application appears on Flathub and GNOME Software.
- [ ] Document future update process.

## Acceptance criteria

- [ ] Flatpak builds offline from tagged source.
- [ ] Flathub lint checks pass.
- [ ] Audio and notifications work in sandbox.
- [ ] Flathub submission is accepted.
- [ ] Storefront page shows correct metadata and screenshots.

---

# 10. Milestone 6 — Fedora RPM and COPR

## Goal

Provide a Fedora-native package installable through DNF after enabling COPR.

## Deliverables

```text
packaging/rpm/hourglass-linux.spec
COPR project
RPM and SRPM validation
Fedora installation documentation
```

## Checklist

### Package strategy

- [ ] Decide source build versus repackaged release binary.
- [ ] Prefer source build where practical.
- [ ] Determine Fedora build dependencies.
- [ ] Determine native runtime requirements.
- [ ] Use Fedora naming and version conventions.
- [ ] Use a stable source archive.
- [ ] Include MIT license and changelog.

### Spec file

- [ ] Create `packaging/rpm/hourglass-linux.spec`.
- [ ] Define Name, Version, Release, Summary, License, URL, and Source0.
- [ ] Define build and runtime requirements.
- [ ] Implement `%prep`, `%build`, `%install`, `%check`, and `%files`.
- [ ] Install application under `/usr/lib/hourglass-linux`.
- [ ] Install launcher under `/usr/bin`.
- [ ] Install desktop entry, AppStream metadata, icons, license, and docs.
- [ ] Avoid build caches and duplicate files.

### Validation

- [ ] Build SRPM.
- [ ] Build binary RPM.
- [ ] Run `rpmlint` on spec, SRPM, and RPM.
- [ ] Build in Fedora Mock.
- [ ] Test current stable Fedora.
- [ ] Test Rawhide if practical.
- [ ] Install on clean Fedora VM.
- [ ] Launch from terminal and GNOME menu.
- [ ] Confirm icon, sound, notifications, uninstall, and upgrade.

### COPR

- [ ] Create Fedora Account System account.
- [ ] Create and configure COPR API token.
- [ ] Create `hourglass-linux` COPR project.
- [ ] Enable supported Fedora chroots.
- [ ] Submit SRPM.
- [ ] Confirm builds succeed.
- [ ] Test DNF enable, install, upgrade, and removal.
- [ ] Add README instructions.
- [ ] Define future COPR automation.
- [ ] Keep credentials outside the repository.

## Acceptance criteria

- [ ] RPM builds in Mock.
- [ ] No unresolved serious `rpmlint` errors.
- [ ] COPR succeeds for supported Fedora releases.
- [ ] A clean Fedora system installs and launches through DNF.

---

# 11. Milestone 7 — Arch Linux AUR

## Goal

Publish an AUR package for Arch Linux users.

## Initial package

Use `hourglass-linux-bin` for the first package because it installs a prebuilt self-contained release artifact. A source-built `hourglass-linux` package may be added later.

## Deliverables

```text
packaging/arch/PKGBUILD
packaging/arch/.SRCINFO
AUR repository: hourglass-linux-bin
```

## Checklist

### Package design

- [ ] Confirm package name `hourglass-linux-bin`.
- [ ] Define `provides=('hourglass-linux')`.
- [ ] Define `conflicts=('hourglass-linux')`.
- [ ] Use `arch=('x86_64')` initially.
- [ ] Use tagged GitHub Release URL and checksum.
- [ ] Avoid mutable release or branch URLs.
- [ ] Include MIT license, description, and homepage.

### PKGBUILD

- [ ] Create `packaging/arch/PKGBUILD`.
- [ ] Define `pkgname`, `pkgver`, `pkgrel`, `pkgdesc`, `arch`, `url`, and `license`.
- [ ] Define `source_x86_64` and `sha256sums_x86_64`.
- [ ] Install application under `/usr/lib/hourglass-linux`.
- [ ] Install launcher under `/usr/bin`.
- [ ] Install desktop entry, metainfo, icons, and license.
- [ ] Avoid `sudo`, host writes, and downloads during `package()`.
- [ ] Quote paths correctly.

### Local testing

- [ ] Run `makepkg --cleanbuild`.
- [ ] Install with `makepkg -si`.
- [ ] Run `namcap PKGBUILD`.
- [ ] Run `namcap` on built package.
- [ ] Launch from terminal and desktop menu.
- [ ] Confirm uninstall and `pkgrel` upgrade.
- [ ] Confirm declared dependencies.
- [ ] Test in clean Arch environment.

### AUR publication

- [ ] Create or confirm AUR account.
- [ ] Add AUR SSH key.
- [ ] Clone AUR package repository.
- [ ] Copy `PKGBUILD`.
- [ ] Generate and review `.SRCINFO`.
- [ ] Commit and push to AUR `master`.
- [ ] Confirm AUR page.
- [ ] Install through an AUR helper.
- [ ] Document update procedure.
- [ ] Keep private SSH keys outside repository.

## Acceptance criteria

- [ ] Clean `makepkg` build succeeds.
- [ ] No unresolved serious `namcap` errors.
- [ ] AUR package is public.
- [ ] Clean Arch system installs and runs Hourglass.
- [ ] Version and checksum match GitHub Release.

---

# 12. Milestone 8 — Upstream Nix Flake

## Goal

Allow users to build, run, and install Hourglass directly from the upstream repository using Nix.

## Deliverables

```text
flake.nix
flake.lock
nix/package.nix
nix/deps.json
```

## Checklist

### Package design

- [ ] Use `buildDotnetModule`.
- [ ] Use .NET 10 SDK and runtime as required.
- [ ] Define package name, version, source, and project file.
- [ ] Generate NuGet dependency lock data.
- [ ] Define native runtime dependencies.
- [ ] Define main program and supported platforms.
- [ ] Define license, homepage, and maintainer metadata.
- [ ] Install desktop entry, metainfo, icons, and sounds.
- [ ] Wrap executable if native library paths require it.

### Flake interface

- [ ] Create `flake.nix`.
- [ ] Pin nixpkgs input.
- [ ] Export `packages.x86_64-linux.hourglass-linux`.
- [ ] Export default package and app.
- [ ] Add checks.
- [ ] Commit `flake.lock`.

### Dependency generation

- [ ] Generate `nix/deps.json`.
- [ ] Document regeneration.
- [ ] Commit dependency hashes.
- [ ] Confirm no build-time network access.
- [ ] Regenerate when NuGet versions change.

### Validation

- [ ] Run `nix build .#hourglass-linux`.
- [ ] Run `nix run .#hourglass-linux`.
- [ ] Run `nix flake check`.
- [ ] Test `nix profile install`.
- [ ] Confirm terminal and desktop launch.
- [ ] Confirm icons, sound, notifications, and settings.
- [ ] Test NixOS.
- [ ] Test non-NixOS Linux with Nix if practical.
- [ ] Confirm no impure host paths.

### Documentation

- [ ] Add Nix run command to README.
- [ ] Add Nix profile installation command.
- [ ] Document supported architecture and known caveats.

## Acceptance criteria

- [ ] `nix flake check` passes.
- [ ] Clean clone builds successfully.
- [ ] `nix run github:MattBunch/hourglass-linux/<tag>` launches.
- [ ] Dependency hashes are committed.

---

# 13. Milestone 9 — Nixpkgs Submission

## Goal

Add Hourglass Linux to the official nixpkgs repository.

## Checklist

- [ ] Adapt upstream package to nixpkgs conventions.
- [ ] Place package under required `pkgs/by-name/` path.
- [ ] Use approved source fetcher with pinned tag and hash.
- [ ] Use nixpkgs .NET helpers.
- [ ] Add package metadata and maintainer.
- [ ] Build through nixpkgs.
- [ ] Run formatting and linting.
- [ ] Test on NixOS.
- [ ] Confirm no network access during build.
- [ ] Fork `NixOS/nixpkgs`.
- [ ] Open package-addition pull request.
- [ ] Include test commands.
- [ ] Address review feedback.
- [ ] Confirm CI passes and PR merges.
- [ ] Update README with official nixpkgs command.
- [ ] Document future update process.

## Acceptance criteria

- [ ] nixpkgs pull request is merged.
- [ ] Users can install `nixpkgs#hourglass-linux`.
- [ ] Maintenance process is documented.

---

# 14. Milestone 10 — Debian and Ubuntu `.deb`

## Goal

Produce a directly downloadable Debian package for Debian and Ubuntu systems.

## Deliverables

```text
packaging/deb/build-deb.sh
packaging/deb/control.template
dist/hourglass-linux_<version>_amd64.deb
```

## Checklist

### Package layout

- [ ] Install application under `/usr/lib/hourglass-linux`.
- [ ] Install launcher under `/usr/bin/hourglass-linux`.
- [ ] Install desktop entry under `/usr/share/applications`.
- [ ] Install AppStream metadata under `/usr/share/metainfo`.
- [ ] Install icons under hicolor paths.
- [ ] Install license and changelog under `/usr/share/doc/hourglass-linux`.
- [ ] Avoid placing the entire self-contained application directly in `/usr/bin`.

### Debian metadata

- [ ] Set package name, version, section, priority, architecture, maintainer, and homepage.
- [ ] Add short and long descriptions.
- [ ] Determine native runtime dependencies.
- [ ] Generate or verify shared-library dependencies.
- [ ] Set installed size.
- [ ] Add conflicts or replaces only if required.

### Build script

- [ ] Create and clean temporary package root.
- [ ] Copy tagged publish output.
- [ ] Install metadata with correct permissions.
- [ ] Generate `DEBIAN/control`.
- [ ] Use root ownership in final package.
- [ ] Build with `dpkg-deb`.
- [ ] Use deterministic filename.
- [ ] Generate checksum.
- [ ] Clean temporary files.

### Validation

- [ ] Inspect package metadata and contents.
- [ ] Run `lintian`.
- [ ] Install on current Ubuntu LTS.
- [ ] Install on Debian stable.
- [ ] Launch from terminal and GNOME menu.
- [ ] Confirm sound and notifications.
- [ ] Confirm upgrade, removal, and purge behavior.
- [ ] Confirm `apt install ./package.deb` works.

### Release integration

- [ ] Add `.deb` build to release workflow.
- [ ] Upload `.deb` to GitHub Release.
- [ ] Include in checksums.
- [ ] Add Debian and Ubuntu instructions.
- [ ] Document supported versions.
- [ ] Explain that direct `.deb` installation does not provide automatic repository updates.

## Acceptance criteria

- [ ] No unresolved serious `lintian` errors.
- [ ] Package installs on clean Debian and Ubuntu systems.
- [ ] Desktop launch, upgrade, and removal work.
- [ ] GitHub Release includes `.deb` and checksum.

---

# 15. Milestone 11 — Signed APT Repository

## Goal

Allow Debian and Ubuntu users to install and update Hourglass through APT.

## Deliverables

```text
Signed APT repository
Public signing key
Repository publication automation
APT installation documentation
```

## Checklist

### Repository design

- [ ] Choose `reprepro` or `aptly`.
- [ ] Choose hosting provider and base URL.
- [ ] Define suite, codename, architectures, and component.
- [ ] Decide whether Debian and Ubuntu share one repository.
- [ ] Define retention and rollback policies.

### Signing

- [ ] Create dedicated repository signing key.
- [ ] Protect private key.
- [ ] Export and publish public key through HTTPS.
- [ ] Document fingerprint.
- [ ] Configure signed repository metadata.
- [ ] Test verification.
- [ ] Define key rotation.
- [ ] Never commit private signing material.

### Repository generation

- [ ] Create repository configuration.
- [ ] Add initial `.deb`.
- [ ] Generate package indexes and compressed indexes.
- [ ] Generate `Release` and `InRelease`.
- [ ] Verify consistency.
- [ ] Publish atomically.
- [ ] Keep a previous version for rollback where appropriate.

### Client flow

- [ ] Provide `/etc/apt/keyrings` command.
- [ ] Use `signed-by=`.
- [ ] Do not recommend deprecated `apt-key`.
- [ ] Provide source line, `apt update`, and install commands.
- [ ] Test Ubuntu and Debian.
- [ ] Test upgrade and repository removal.
- [ ] Document fingerprint verification.

### Automation

- [ ] Trigger repository update after successful GitHub Release.
- [ ] Verify release `.deb` checksum.
- [ ] Sign repository metadata.
- [ ] Upload atomically.
- [ ] Protect production environment.
- [ ] Add staging or dry-run path.
- [ ] Add failure notification and recovery documentation.

## Acceptance criteria

- [ ] Clean Ubuntu and Debian systems can install from repository.
- [ ] `apt update` verifies signatures.
- [ ] Later release upgrades through APT.
- [ ] Signing key and fingerprint are documented.

---

# 16. Milestone 12 — ARM64 Support

## Goal

Add Linux ARM64 support after x86_64 distribution is stable.

## Checklist

### Application and CI

- [ ] Add `linux-arm64` runtime identifier.
- [ ] Update publish and validation scripts.
- [ ] Add arm64 CI job.
- [ ] Publish self-contained arm64 build.
- [ ] Test on real ARM64 hardware.
- [ ] Confirm Avalonia native dependencies, sound, notifications, timer accuracy, suspend behavior, and single-instance support.

### Channels

- [ ] Add arm64 tarball and checksum.
- [ ] Investigate arm64 AppImage tooling.
- [ ] Add Flatpak aarch64 build.
- [ ] Remove x86_64-only Flathub restriction.
- [ ] Add Fedora aarch64 COPR chroot.
- [ ] Add Debian arm64 package.
- [ ] Add Nix `aarch64-linux`.
- [ ] Add AUR aarch64 only if tested and supported.
- [ ] Update documentation.

## Acceptance criteria

- [ ] ARM64 release is tested on real hardware.
- [ ] Supported channels publish ARM64 artifacts.
- [ ] Unsupported channels remain explicitly documented.

---

# 17. Milestone 13 — Official Distribution Repositories

## Goal

Move from project-maintained repositories to official distribution repositories where practical.

## Official Fedora

- [ ] Review Fedora packaging rules.
- [ ] Ensure source build complies with policy.
- [ ] Remove bundled components where required.
- [ ] Prepare review-quality spec and SRPM.
- [ ] File package review request.
- [ ] Find or qualify as Fedora package maintainer.
- [ ] Resolve review and legal feedback.
- [ ] Import into Fedora dist-git.
- [ ] Build for supported releases.
- [ ] Maintain updates.

## Official Debian

- [ ] Review Debian Policy.
- [ ] Create full Debian source package.
- [ ] Add `debian/control`, `rules`, `changelog`, `copyright`, watch file, and source format.
- [ ] Build with `dpkg-buildpackage`.
- [ ] Test with `lintian` and `sbuild` or `pbuilder`.
- [ ] File Intent To Package.
- [ ] Find sponsor if required.
- [ ] Resolve review feedback.
- [ ] Upload and maintain package.

## Official Arch repositories

- [ ] Maintain a high-quality AUR package.
- [ ] Build adoption.
- [ ] Respond to comments and issues.
- [ ] Keep package current.
- [ ] Coordinate adoption if an Arch maintainer offers to move it.

## Acceptance criteria

- [ ] At least one official distribution repository accepts the package.
- [ ] Maintenance responsibilities are documented.
- [ ] Upstream versus downstream issue boundaries are clear.

---

# 18. Per-Release Maintenance Checklist

## Before tagging

- [ ] Update `.csproj` version.
- [ ] Update AppStream release entry and date.
- [ ] Update `CHANGELOG.md`.
- [ ] Update screenshots if UI changed.
- [ ] Regenerate Flatpak NuGet sources when dependencies change.
- [ ] Regenerate Nix dependency hashes when dependencies change.
- [ ] Update package recipes.
- [ ] Run full test suite and formatting verification.
- [ ] Run metadata validators.
- [ ] Test self-contained publish.
- [ ] Confirm clean Git tree.

## Tag and GitHub Release

- [ ] Create signed or annotated Git tag.
- [ ] Push tag.
- [ ] Confirm release workflow succeeds.
- [ ] Confirm tarball, AppImage, `.deb`, and checksums as applicable.
- [ ] Confirm release notes.
- [ ] Download and verify public artifacts.

## Flathub

- [ ] Update manifest tag and commit.
- [ ] Regenerate NuGet sources if required.
- [ ] Test Flatpak locally.
- [ ] Submit update.
- [ ] Confirm stable publication.

## COPR

- [ ] Update RPM version and release.
- [ ] Build SRPM and run Mock.
- [ ] Submit COPR build.
- [ ] Confirm chroots succeed.
- [ ] Test DNF upgrade.

## AUR

- [ ] Update `pkgver` and `pkgrel`.
- [ ] Update source URL and checksum.
- [ ] Regenerate `.SRCINFO`.
- [ ] Run clean build.
- [ ] Push update.

## Nix

- [ ] Update version and source hash.
- [ ] Update NuGet dependency hashes.
- [ ] Run `nix flake check`.
- [ ] Open nixpkgs update PR if official.

## Debian and APT

- [ ] Update Debian version.
- [ ] Build and lint `.deb`.
- [ ] Test install and upgrade.
- [ ] Publish `.deb`.
- [ ] Add to APT repository.
- [ ] Sign repository metadata.
- [ ] Test `apt update` and upgrade.

---

# 19. Testing Matrix

## Desktop environments

- [ ] GNOME Wayland.
- [ ] GNOME X11 when available.
- [ ] KDE Plasma Wayland.
- [ ] KDE Plasma X11 when available.
- [ ] Xfce.
- [ ] Cinnamon.

## Distributions

- [ ] Fedora current stable.
- [ ] Fedora Rawhide for packaging compatibility.
- [ ] Ubuntu current LTS.
- [ ] Debian stable.
- [ ] Arch Linux.
- [ ] NixOS current stable.

## Package types

- [ ] Self-contained tarball.
- [ ] AppImage.
- [ ] Flatpak.
- [ ] RPM.
- [ ] `.deb`.
- [ ] AUR package.
- [ ] Upstream Nix flake.
- [ ] Nixpkgs package.
- [ ] APT repository upgrade.

## Functional checks per package

- [ ] Application launches.
- [ ] Icon and desktop launcher work.
- [ ] Timer input works.
- [ ] Enter starts timer where supported.
- [ ] Countdown remains accurate.
- [ ] Pause, resume, restart, and stop work.
- [ ] Timer expiry works.
- [ ] Audio plays and stops.
- [ ] Notification appears.
- [ ] Always-on-top behavior is acceptable.
- [ ] Pop-up-on-expiry behavior is acceptable.
- [ ] Settings and saved timers persist.
- [ ] Active sessions restore.
- [ ] Multiple windows and single-instance handoff work.
- [ ] About dialog reports correct version.
- [ ] Application exits cleanly.
- [ ] Upgrade preserves user data.
- [ ] Uninstall does not unexpectedly delete user data.

---

# 20. Documentation Deliverables

## README

- [ ] Add Download section.
- [ ] Add AppImage instructions.
- [ ] Add Flatpak instructions.
- [ ] Add Fedora COPR instructions.
- [ ] Add AUR instructions.
- [ ] Add Nix instructions.
- [ ] Add `.deb` instructions.
- [ ] Add APT repository instructions.
- [ ] Add supported architectures and channels.
- [ ] Add checksum verification.
- [ ] Add known limitations and troubleshooting link.

## Additional documents

- [ ] Create `docs/releasing.md`.
- [ ] Create `docs/packaging.md`.
- [ ] Create `docs/flatpak.md`.
- [ ] Create `docs/rpm.md`.
- [ ] Create `docs/arch.md`.
- [ ] Create `docs/nix.md`.
- [ ] Create `docs/debian.md`.
- [ ] Create `docs/apt-repository.md`.
- [ ] Create `docs/troubleshooting.md`.
- [ ] Create `docs/THIRD_PARTY_NOTICES.md` if required.
- [ ] Update this roadmap after every packaging pull request.

---

# 21. Suggested Pull Request Sequence

## PR 1 — Versioning and metadata

- [ ] Add version validation.
- [ ] Add changelog.
- [ ] Improve AppStream metadata.
- [ ] Add screenshots.
- [ ] Add release documentation.

## PR 2 — Public GitHub Releases

- [ ] Add tag-triggered release workflow.
- [ ] Add tarball naming and checksums.
- [ ] Add release notes.
- [ ] Validate public release flow.

## PR 3 — AppImage

- [ ] Add final AppImage builder.
- [ ] Add AppImage CI and testing.
- [ ] Add README instructions.

## PR 4 — Production Flatpak

- [ ] Replace local source manifest.
- [ ] Add supported runtime and .NET extension.
- [ ] Add offline NuGet sources.
- [ ] Fix sandbox audio.
- [ ] Add lint checks.

## PR 5 — Flathub submission

- [ ] Complete final metadata and sandbox tests.
- [ ] Open submission.
- [ ] Address review feedback.

## PR 6 — RPM and COPR

- [ ] Add RPM spec.
- [ ] Add Mock and rpmlint validation.
- [ ] Publish COPR.
- [ ] Document DNF installation.

## PR 7 — AUR

- [ ] Add PKGBUILD and update documentation.
- [ ] Publish `hourglass-linux-bin`.

## PR 8 — Nix flake

- [ ] Add Nix package, flake, dependency data, and documentation.

## PR 9 — Debian package

- [ ] Add `.deb` builder and lintian validation.
- [ ] Add GitHub Release asset.

## PR 10 — APT repository

- [ ] Add repository generation, signing, publication, and documentation.

## PR 11 — Nixpkgs

- [ ] Adapt package and open upstream pull request.

## PR 12 — ARM64

- [ ] Add arm64 publishing, testing, and channel support.

---

# 22. Definition of Initial Distribution-Ready

Hourglass Linux is initially distribution-ready when:

- [ ] A tagged GitHub Release is public.
- [ ] Release includes a tested x86_64 tarball.
- [ ] Release includes a tested AppImage.
- [ ] Release includes SHA-256 checksums.
- [ ] AppStream and desktop metadata pass validation.
- [ ] Storefront screenshots are published.
- [ ] Flathub package is accepted or under final review.
- [ ] At least one native package channel is available.
- [ ] Installation and upgrade instructions are tested.
- [ ] Licensing and attribution are complete.
- [ ] Known limitations are documented.
- [ ] Release maintenance process is documented.

---

# 23. Project Progress Summary

Update this table whenever a milestone changes state.

| Milestone | Status | Notes |
|---|---|---|
| 0. Release readiness audit | Not started | Repository already has a strong packaging baseline. |
| 1. Versioning and source of truth | Not started | Version exists in project and AppStream metadata. |
| 2. Storefront metadata and screenshots | Not started | AppStream exists; screenshots remain. |
| 3. Public GitHub release pipeline | Not started | CI artifacts exist but are not public releases. |
| 4. AppImage release | Not started | AppDir exists; final AppImage generation is missing. |
| 5. Production Flatpak and Flathub | Not started | Local prototype manifest exists. |
| 6. Fedora RPM and COPR | Not started | RPM spec not yet added. |
| 7. Arch Linux AUR | Not started | PKGBUILD not yet added. |
| 8. Upstream Nix flake | Not started | Nix packaging not yet added. |
| 9. Nixpkgs submission | Blocked | Depends on upstream Nix package. |
| 10. Debian/Ubuntu `.deb` | Not started | Debian builder not yet added. |
| 11. Signed APT repository | Blocked | Depends on stable `.deb` workflow. |
| 12. ARM64 support | Deferred | Current release tooling supports linux-x64 only. |
| 13. Official repositories | Deferred | Start after project-maintained channels stabilize. |

---

# 24. Immediate Next Actions

- [ ] Create `feature/release-pipeline`.
- [ ] Add `CHANGELOG.md`.
- [ ] Add `docs/releasing.md`.
- [ ] Add version consistency validation.
- [ ] Add storefront screenshots.
- [ ] Improve AppStream release metadata.
- [ ] Add tag-triggered GitHub Release workflow.
- [ ] Convert existing AppDir into a final AppImage.
- [ ] Publish a prerelease such as `v0.1.0-rc.1`.
- [ ] Test prerelease on Fedora and Ubuntu.
- [ ] Begin production Flatpak conversion.
- [ ] Resolve Flatpak audio before Flathub submission.

---

# 25. Codex Completion Report Template

Codex should use this format after completing a milestone or pull request:

```text
## Completed

- Milestone:
- Branch:
- Commit:
- Pull request:

## Changes

-
-
-

## Validation

- Command:
  - Result:
- Command:
  - Result:

## Packaging Tests

- Distribution:
- Desktop environment:
- Package type:
- Result:

## Remaining Work

-
-

## Roadmap Updates

- Checkboxes marked complete:
- New blockers:
- Deferred items:
```
