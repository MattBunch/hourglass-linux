# Releasing Hourglass Linux

## Version source of truth

`src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj` is the canonical
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
3. Run `scripts/validate-release-version.sh`. When preparing a tag, also run
   `scripts/validate-release-version.sh --tag vX.Y.Z` with the exact tag.
4. Run the modern Linux restore, Release build, tests, formatting verification,
   and packaging validators documented in `AGENTS.md`.
5. Commit the release-preparation change, create the matching annotated Git
   tag, and push both only when the required release workflow from DEV-7 is in
   place.

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
