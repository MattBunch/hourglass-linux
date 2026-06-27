#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)

exec dotnet run --project "$repo_root/src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj" -- "$@"
