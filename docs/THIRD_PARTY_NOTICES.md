# Third-Party Notices Audit

This document records the third-party license evidence reviewed for the
`0.1.0` Linux `linux-x64` self-contained publish audited on 2026-08-12. It is
an audit record, not a replacement for the verbatim notices that must be
included with a public release artifact.

## Hourglass Linux

Hourglass Linux, including the copied upstream timer sounds, is distributed
under the MIT License in the repository root's `LICENSE.md`. The three bundled
WAV files are byte-for-byte copies of the upstream Hourglass resources covered
by that license. The application icon is a repository-authored asset; this
audit found no imported third-party icon asset.

The application retains the original Hourglass attribution in the README and
the About dialog.

## Runtime dependencies

The audit used `dotnet package list --project
src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj
--include-transitive --format json --no-restore` and the corresponding NuGet
package metadata. The following packages are present in the `linux-x64`
publish output or provide its runtime dependencies:

| Component | Version | License evidence |
| --- | --- | --- |
| .NET runtime | 10.0.10 | MIT; `Microsoft.NETCore.App.Runtime.linux-x64` includes `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`. |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.HarfBuzz, Avalonia.Native, Avalonia.Remote.Protocol, Avalonia.Skia, Avalonia.X11 | 12.0.4 | MIT, AvaloniaUI Project package metadata. |
| HarfBuzzSharp and HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT; package `LICENSE.txt` names Xamarin and Microsoft copyright holders. |
| SkiaSharp and SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT; packages include `LICENSE.txt` and native-assets `THIRD-PARTY-NOTICES.txt`. |
| MicroCom.Runtime | 0.11.4 | MIT, package metadata. |
| Tmds.DBus.Protocol | 0.92.0 | MIT, package metadata. |

The audit also queried NuGet vulnerability metadata with `dotnet package list
--project src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj
--vulnerable --include-transitive --no-restore`; it reported no vulnerable
packages for the current sources.

## Public-release requirement

Before publishing an archive, AppImage, or other redistributable artifact, the
release pipeline must include the project MIT license and the verbatim
third-party notices supplied by the self-contained .NET runtime and native
asset packages. `scripts/publish-linux-release.sh` stages that notice bundle in
`licenses/`, and the AppDir builder copies it into the final AppImage payload.
