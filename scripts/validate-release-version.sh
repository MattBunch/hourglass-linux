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

if ! command -v xmllint >/dev/null 2>&1; then
  printf 'xmllint is required to read AppStream release metadata.\n' >&2
  exit 1
fi

version=$("$script_dir/read-release-version.sh" --project "$project")
semver_number='(0|[1-9][0-9]*)'
semver_pattern="^${semver_number}\\.${semver_number}\\.${semver_number}(-beta\\.[1-9][0-9]*)?$"

if [[ ! "$version" =~ $semver_pattern ]]; then
  printf 'Project version is not a supported release version: %s\n' "$version" >&2
  exit 1
fi

appstream_version=$(xmllint --xpath 'string((//*[local-name()="release"]/@version)[1])' "$metainfo")
if [[ -z "$appstream_version" ]]; then
  printf 'Expected at least one AppStream <release> version in %s\n' "$metainfo" >&2
  exit 1
fi

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
