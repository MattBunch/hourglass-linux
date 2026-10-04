# Releasing Hourglass Linux

## Version source of truth

`build/Hourglass.Version.props`, imported by GUI, CLI, TUI and Host, is the canonical
source of the Hourglass Linux release version. Its `<Version>` must use one of
these forms:

- final release: `X.Y.Z`, for example `0.1.0`;
- beta prerelease: `X.Y.Z-beta.N`, where `N` starts at `1`, for example
  `0.2.0-beta.1`.

The latest release in
`packaging/linux/io.github.MattBunch.Hourglass.metainfo.xml` must exactly match
that version. The date on that AppStream release is updated when the release
preparation change is made.

Git tags are immutable release identifiers and must be exactly `v` followed by
the canonical version: `v0.1.0` or `v0.2.0-beta.1`. Do not create a tag until
the version, AppStream entry, changelog, and validation are committed.

## Release preparation

1. Update `<Version>` and prepend the matching AppStream `<release>` entry,
   including its ISO-8601 release date.
2. Add a matching `CHANGELOG.md` section with user-visible changes and known
   limitations.
3. Read the canonical version with
   `dotnet run --project tools/Hourglass.ReleaseTool -- version`. Run
   `dotnet run --project tools/Hourglass.ReleaseTool -- validate-version`.
   When preparing a tag, also run
   `dotnet run --project tools/Hourglass.ReleaseTool -- validate-version --tag vX.Y.Z`
   with the exact tag.
4. Run the modern Linux restore, Release build, tests, formatting verification,
   and packaging validators documented in `AGENTS.md`.
5. Commit the release-preparation change, create the matching annotated Git
   tag, and push both only when the required release workflow from DEV-7 is in
   place.

## Public GitHub Releases

Pushing an approved tag matching `vX.Y.Z` or `vX.Y.Z-beta.N` runs the
tag-triggered release workflow. It repeats version, build, test, formatting,
and package validation before publishing these assets:

- `hourglass-linux-X.Y.Z-linux-x64.tar.gz`;
- `Hourglass-X.Y.Z-x86_64.AppImage`;
- `hourglass-terminal-X.Y.Z-linux-x64.tar.gz`;
- `hourglass-terminal-X.Y.Z-linux-arm64.tar.gz`;
- `SHA256SUMS`.

Each package asset contains a `licenses/` directory with the project `LICENSE.md` and
the verbatim notices for the resolved .NET runtime, HarfBuzzSharp native asset,
and SkiaSharp native asset packages. This is separate from the audit record in
`docs/THIRD_PARTY_NOTICES.md`.
Each package asset excludes portable PDB debug symbols so public artifacts do
not expose build-machine source paths.

Beta tags create GitHub prereleases; final-version tags create normal releases.
If a release attempt leaves a draft because asset upload or publication failed,
rerunning the same tag workflow replaces the draft assets and publishes that
draft. A published release for the tag remains immutable and causes the retry
to fail. If the pushed tag is deleted while validation is running, the workflow
also fails rather than recreating it from the default branch. Before publishing,
the workflow also verifies that the remote tag still resolves to the commit it
built, so a force-moved tag fails safely. Draft retries repeat that target check
after asset upload and before publishing the draft. New releases also begin as
drafts, so every public release uses this same verified publish sequence.
After downloading one or both package assets into the same directory, verify
the available assets with:

```bash
sha256sum --check --ignore-missing SHA256SUMS
```

The workflow does not publish package-manager artifacts, signed assets, signed
tags, or attestations. AppImage signing and AppImageUpdate metadata remain
separate milestones with their own key-management and distribution policies.

## Future package version mappings

These mappings are policy for their later packaging milestones; DEV-6 does not
add package recipes.

| Channel | Final `X.Y.Z` | Beta `X.Y.Z-beta.N` |
| --- | --- | --- |
| RPM | `Version: X.Y.Z`, `Release: 1` | `Version: X.Y.Z`, `Release: 0.beta.N` |
| Debian | `X.Y.Z-1` | `X.Y.Z~beta.N-1` |
| AUR | `pkgver=X.Y.Z`, `pkgrel=1` | `pkgver=X.Y.Z.betaN`, `pkgrel=1` |
| Nix | source version `X.Y.Z` | source version `X.Y.Z-beta.N` |

Each Nix source update must use the immutable tag or commit and its matching
SRI hash. Increment RPM, Debian, and AUR package revisions only for packaging
changes to the same upstream version.

## Terminal archive gates

From a clean committed checkout, run `scripts/publish-terminal-release.sh
--runtime linux-x64 --output /tmp/hourglass-terminal-release` (choose a fresh
output directory). The publisher stages separate CLI/TUI/Host payloads, sound
assets, pinned dependency notices, source revision and a deterministic archive.
Use `linux-arm64` for the other target; cross-publishing alone does not validate
execution. Both native CI jobs extract to a path containing spaces and execute
`tests/verify-terminal-package.py`, including foreground/detached lifecycle and
PTY restoration checks. The tagged release consumes those tested archives only
after both jobs succeed. Existing GUI archive and AppImage names remain unchanged.

Version validation checks all executable project versions against the GUI,
AppStream and tag. Verbatim terminal dependency licenses are pinned under
`packaging/terminal/licenses`; package/version changes require updating that
audit. Missing or modified required license evidence fails packaging.

Release approval must distinguish automated package/process checks from attended
SSH, terminal emulator, screen-reader, desktop-service and physical wake checks.
Unavailable attended checks are recorded as Not run. This workflow does not
create tags on behalf of an implementation task.
