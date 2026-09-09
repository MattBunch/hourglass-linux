#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  printf 'Usage: %s <appdir> <output-appimage>\n' "$0" >&2
  exit 2
fi

appdir=$1
output=$2
appimagetool_version=1.9.1
appimagetool_sha256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
appimagetool_url="https://github.com/AppImage/appimagetool/releases/download/${appimagetool_version}/appimagetool-x86_64.AppImage"
runtime_version=20251108
runtime_sha256=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d
runtime_url="https://github.com/AppImage/type2-runtime/releases/download/${runtime_version}/runtime-x86_64"
temporary_directory=$(mktemp -d)

cleanup() {
  rm -rf "$temporary_directory"
}

verify_checksum() {
  local path=$1
  local expected=$2
  local actual

  actual=$(sha256sum "$path" | awk '{print $1}')
  if [[ "$actual" != "$expected" ]]; then
    printf 'Checksum mismatch for %s.\n' "$path" >&2
    exit 1
  fi
}

trap cleanup EXIT

if [[ ! -x "$appdir/AppRun" ]]; then
  printf 'Expected AppRun executable not found: %s\n' "$appdir/AppRun" >&2
  exit 1
fi

mkdir -p "$(dirname "$output")"
rm -f "$output"

appimagetool="$temporary_directory/appimagetool-x86_64.AppImage"
runtime="$temporary_directory/runtime-x86_64"
curl --fail --location --retry 3 --output "$appimagetool" "$appimagetool_url"
curl --fail --location --retry 3 --output "$runtime" "$runtime_url"
verify_checksum "$appimagetool" "$appimagetool_sha256"
verify_checksum "$runtime" "$runtime_sha256"
chmod +x "$appimagetool" "$runtime"

APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 "$appimagetool" --runtime-file "$runtime" "$appdir" "$output"

if [[ ! -s "$output" || ! -x "$output" ]]; then
  printf 'Expected executable AppImage was not created: %s\n' "$output" >&2
  exit 1
fi
