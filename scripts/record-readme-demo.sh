#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
temporary_config=$(mktemp -d "${TMPDIR:-/tmp}/hourglass-readme-demo-config.XXXXXX")

cleanup() {
  rm -rf -- "$temporary_config"
}
trap cleanup EXIT

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found on PATH. Install the .NET 10 SDK and rerun this script." >&2
  exit 1
fi

if ! command -v ffmpeg >/dev/null 2>&1; then
  echo "ffmpeg was not found on PATH. Install ffmpeg and rerun this script." >&2
  echo "Fedora: sudo dnf install ffmpeg" >&2
  exit 1
fi

echo "Building Hourglass demo recorder..."
dotnet build "$repo_root/tools/Hourglass.DemoRecorder/Hourglass.DemoRecorder.csproj" --configuration Release

export XDG_CONFIG_HOME="$temporary_config"

dotnet run \
  --configuration Release \
  --no-build \
  --project "$repo_root/tools/Hourglass.DemoRecorder/Hourglass.DemoRecorder.csproj" \
  -- \
  "$@"
