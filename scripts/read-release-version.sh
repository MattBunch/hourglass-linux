#!/usr/bin/env bash
set -euo pipefail

usage() {
  printf 'Usage: %s [--project <project-file>]\n' "$0" >&2
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
project="$script_dir/../src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      if [[ $# -lt 2 || -z "$2" ]]; then
        usage
        exit 2
      fi
      project=$2
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

if [[ ! -f "$project" ]]; then
  printf 'Project file not found: %s\n' "$project" >&2
  exit 1
fi

mapfile -t versions < <(sed -nE 's@.*<Version>([^<]+)</Version>.*@\1@p' "$project")

if [[ ${#versions[@]} -ne 1 || -z "${versions[0]}" ]]; then
  printf 'Expected exactly one non-empty <Version> element in %s\n' "$project" >&2
  exit 1
fi

printf '%s\n' "${versions[0]}"
