#!/usr/bin/env bash
set -euo pipefail

usage() {
  printf 'Usage: %s --output <publish-dir> [--runtime linux-x64]\n' "$0" >&2
}

runtime=linux-x64
output=

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime)
      if [[ $# -lt 2 || -z "$2" ]]; then
        usage
        exit 2
      fi
      runtime=$2
      shift 2
      ;;
    --output)
      if [[ $# -lt 2 || -z "$2" ]]; then
        usage
        exit 2
      fi
      output=$2
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unexpected argument: %s\n' "$1" >&2
      usage
      exit 2
      ;;
  esac
done

if [[ -z "$output" ]]; then
  usage
  exit 2
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
project="$repo_root/src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj"
source_revision=

if git -C "$repo_root" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if [[ -n "$(git -C "$repo_root" status --porcelain)" ]]; then
    printf 'Cannot publish a release with uncommitted changes. Commit, stash, or discard changes first.\n' >&2
    exit 1
  fi

  source_revision=$(git -C "$repo_root" rev-parse --verify HEAD 2>/dev/null || true)
fi

case "$runtime" in
  linux-x64)
    ;;
  *)
    printf 'Unsupported runtime for this release script: %s\n' "$runtime" >&2
    exit 2
    ;;
esac

rm -rf -- "$output"
mkdir -p -- "$output"

dotnet restore "$project" \
  --runtime "$runtime" \
  --verbosity normal

publish_args=(
  "$project"
  --configuration Release
  --runtime "$runtime"
  --self-contained true
  --no-restore
  --output "$output"
  --verbosity normal
)

if [[ -n "$source_revision" ]]; then
  publish_args+=("-p:SourceRevisionId=$source_revision")
fi

dotnet publish "${publish_args[@]}"

find "$output" -type f -name '*.pdb' -delete
if find "$output" -type f -name '*.pdb' -print -quit | grep -q .; then
  printf 'Release publish output still contains portable debug symbols.\n' >&2
  exit 1
fi

notice_directory="$output/licenses"
nuget_packages=$(dotnet nuget locals global-packages --list | sed -n 's/^global-packages: //p')
runtime_version=$(sed -nE 's/^[[:space:]]*"version": "([^"]+)".*/\1/p' "$output/hourglass-linux.runtimeconfig.json")
project_assets="$repo_root/src/Hourglass.Linux.Avalonia/obj/project.assets.json"

package_version() {
  local package_name=$1
  local escaped_name="${package_name//./\\.}"

  sed -nE "s/^[[:space:]]*\"${escaped_name}\/([^\"]+)\": \{.*/\1/p" "$project_assets" | head -n 1
}

stage_native_asset_notices() {
  local package_name=$1
  local package_version=$2
  local package_directory="${nuget_packages}/${package_name}/${package_version}"
  local destination_directory="${notice_directory}/${package_name}"

  mkdir -p "$destination_directory"
  cp "$package_directory/LICENSE.txt" "$destination_directory/LICENSE.txt"
  cp "$package_directory/THIRD-PARTY-NOTICES.txt" "$destination_directory/THIRD-PARTY-NOTICES.txt"
}

test -n "$nuget_packages"
test -n "$runtime_version"
mkdir -p "$notice_directory/Microsoft.NETCore.App.Runtime.linux-x64"
cp "$repo_root/LICENSE.md" "$notice_directory/LICENSE.md"
cp "$nuget_packages/microsoft.netcore.app.runtime.linux-x64/$runtime_version/LICENSE.TXT" "$notice_directory/Microsoft.NETCore.App.Runtime.linux-x64/LICENSE.TXT"
cp "$nuget_packages/microsoft.netcore.app.runtime.linux-x64/$runtime_version/THIRD-PARTY-NOTICES.TXT" "$notice_directory/Microsoft.NETCore.App.Runtime.linux-x64/THIRD-PARTY-NOTICES.TXT"
stage_native_asset_notices harfbuzzsharp.nativeassets.linux "$(package_version HarfBuzzSharp.NativeAssets.Linux)"
stage_native_asset_notices skiasharp.nativeassets.linux "$(package_version SkiaSharp.NativeAssets.Linux)"
