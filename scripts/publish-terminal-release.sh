#!/usr/bin/env bash
set -euo pipefail
runtime=
output=
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime|--output)
      [[ $# -ge 2 && -n "$2" ]] || { printf 'Missing option value.\n' >&2; exit 2; }
      if [[ "$1" == --runtime ]]; then runtime=$2; else output=$2; fi
      shift 2 ;;
    --help|-h) printf 'Usage: %s --runtime linux-x64|linux-arm64 --output <new-directory>\n' "$0"; exit 0 ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done
[[ "$runtime" == linux-x64 || "$runtime" == linux-arm64 ]] || { printf 'Unsupported runtime.\n' >&2; exit 2; }
[[ -n "$output" && ! -e "$output" ]] || { printf 'Output must be a new directory.\n' >&2; exit 2; }
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
[[ -z "$(git -C "$repo_root" status --porcelain)" ]] || { printf 'Commit changes before publishing.\n' >&2; exit 1; }
revision=$(git -C "$repo_root" rev-parse HEAD)
version=$(dotnet msbuild "$repo_root/src/Hourglass.Cli/Hourglass.Cli.csproj" -nologo -getProperty:Version)
package="$output/package"
mkdir -p "$package/bin" "$package/docs/linux-port"
for component in cli tui host; do
  case "$component" in cli) project=Hourglass.Cli ;; tui) project=Hourglass.Tui ;; host) project=Hourglass.Host ;; esac
  dotnet publish "$repo_root/src/$project/$project.csproj" --configuration Release --runtime "$runtime" --self-contained true \
    --output "$package/lib/hourglass/$component" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:SourceRevisionId="$revision"
done
for component in cli tui; do
  case "$component" in cli) executable=hourglass ;; tui) executable=hourglass-tui ;; esac
  cat > "$package/bin/$executable" <<EOF
#!/bin/sh
set -eu
launcher=\$(readlink -f -- "\$0")
base=\$(CDPATH= cd -- "\$(dirname -- "\$launcher")/.." && pwd)
exec "\$base/lib/hourglass/$component/$executable" "\$@"
EOF
  chmod +x "$package/bin/$executable"
done
find "$package" -type f -name '*.pdb' -delete
python3 "$repo_root/scripts/stage-terminal-notices.py" "$package" "$runtime"
cp "$repo_root/docs/cli.md" "$repo_root/docs/tui.md" "$repo_root/docs/terminal-installation.md" "$package/docs/"
cp -a "$repo_root/docs/linux-port/." "$package/docs/linux-port/"
printf '{"version":"%s","runtime":"%s","sourceRevision":"%s"}\n' "$version" "$runtime" "$revision" > "$package/manifest.json"
archive="hourglass-terminal-${version}-${runtime}.tar.gz"
epoch=$(git -C "$repo_root" show -s --format=%ct HEAD)
tar --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner -C "$package" -cf - . | gzip -n > "$output/$archive"
(cd "$output" && sha256sum "$archive" > SHA256SUMS && sha256sum --check SHA256SUMS)
python3 "$repo_root/scripts/validate-terminal-packaging.py" "$package" "$runtime"
