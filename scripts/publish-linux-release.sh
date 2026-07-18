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
build_output="$repo_root/src/Hourglass.Linux.Avalonia/bin/Release/net10.0/$runtime"

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
  --disable-parallel \
  --verbosity normal

dotnet build "$project" \
  --configuration Release \
  --runtime "$runtime" \
  --self-contained true \
  --no-restore \
  --maxcpucount:1 \
  --verbosity normal

cp -a -- "$build_output/." "$output/"
