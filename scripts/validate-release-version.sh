#!/usr/bin/env bash
set -euo pipefail

usage() {
  printf 'Usage: %s [--project <project-file>] [--metainfo <metainfo-file>] [--tag <tag>]\n' "$0" >&2
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
project="$repo_root/src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj"
metainfo="$repo_root/packaging/linux/io.github.MattBunch.Hourglass.metainfo.xml"
tag=

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project|--metainfo|--tag)
      if [[ $# -lt 2 || -z "$2" ]]; then
        usage
        exit 2
      fi
      case "$1" in
        --project) project=$2 ;;
        --metainfo) metainfo=$2 ;;
        --tag) tag=$2 ;;
      esac
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      usage
      exit 2
      ;;
  esac
done

if [[ ! -f "$metainfo" ]]; then
  printf 'AppStream metadata file not found: %s\n' "$metainfo" >&2
  exit 1
fi

version=$($script_dir/read-release-version.sh --project "$project")
semver_pattern='^[0-9]+\.[0-9]+\.[0-9]+(-beta\.[1-9][0-9]*)?$'

if [[ ! "$version" =~ $semver_pattern ]]; then
  printf 'Project version is not a supported release version: %s\n' "$version" >&2
  exit 1
fi

mapfile -t appstream_versions < <(sed -nE 's@.*<release[[:space:]]+version="([^"]+)".*>@\1@p' "$metainfo")

if [[ ${#appstream_versions[@]} -eq 0 || -z "${appstream_versions[0]}" ]]; then
  printf 'Expected at least one AppStream <release> version in %s\n' "$metainfo" >&2
  exit 1
fi

appstream_version=${appstream_versions[0]}
if [[ ! "$appstream_version" =~ $semver_pattern ]]; then
  printf 'Latest AppStream release is not a supported release version: %s\n' "$appstream_version" >&2
  exit 1
fi

if [[ "$version" != "$appstream_version" ]]; then
  printf 'Project version %s does not match latest AppStream release %s\n' "$version" "$appstream_version" >&2
  exit 1
fi

if [[ -n "$tag" && "$tag" != "v$version" ]]; then
  printf 'Release tag %s does not match expected tag v%s\n' "$tag" "$version" >&2
  exit 1
fi

printf 'Release version validation passed: %s\n' "$version"
