#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  printf 'Usage: %s <publish-dir> <appdir>\n' "$0" >&2
  exit 2
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
publish_dir=$1
appdir=$2
app_id=io.github.MattBunch.Hourglass
desktop_file="$repo_root/packaging/linux/$app_id.desktop"
metainfo_file="$repo_root/packaging/linux/$app_id.metainfo.xml"
flatpak_manifest="$repo_root/packaging/flatpak/$app_id.yml"
require_validators=${HOURGLASS_REQUIRE_PACKAGE_VALIDATORS:-false}

require_file() {
  if [[ ! -f "$1" ]]; then
    printf 'Missing file: %s\n' "$1" >&2
    exit 1
  fi
}

require_executable() {
  if [[ ! -x "$1" ]]; then
    printf 'Missing executable: %s\n' "$1" >&2
    exit 1
  fi
}

require_text() {
  local file=$1
  local text=$2

  if ! grep -Fqx -- "$text" "$file"; then
    printf 'Expected line not found in %s: %s\n' "$file" "$text" >&2
    exit 1
  fi
}

require_executable "$publish_dir/hourglass-linux"
require_file "$publish_dir/licenses/LICENSE.md"
require_file "$publish_dir/licenses/Microsoft.NETCore.App.Runtime.linux-x64/LICENSE.TXT"
require_file "$publish_dir/licenses/Microsoft.NETCore.App.Runtime.linux-x64/THIRD-PARTY-NOTICES.TXT"
require_file "$publish_dir/licenses/harfbuzzsharp.nativeassets.linux/LICENSE.txt"
require_file "$publish_dir/licenses/harfbuzzsharp.nativeassets.linux/THIRD-PARTY-NOTICES.txt"
require_file "$publish_dir/licenses/skiasharp.nativeassets.linux/LICENSE.txt"
require_file "$publish_dir/licenses/skiasharp.nativeassets.linux/THIRD-PARTY-NOTICES.txt"
require_file "$desktop_file"
require_file "$metainfo_file"
require_file "$flatpak_manifest"

require_text "$desktop_file" 'Exec=hourglass-linux'
require_text "$desktop_file" 'Icon=hourglass'
require_text "$desktop_file" 'StartupWMClass=hourglass'
require_text "$desktop_file" 'Categories=Utility;'

require_text "$flatpak_manifest" "app-id: $app_id"
require_text "$flatpak_manifest" 'command: hourglass-linux'
require_text "$flatpak_manifest" '  - --socket=x11'
if grep -Fqx -- '  - --socket=wayland' "$flatpak_manifest"; then
  printf 'Flatpak manifest should not request Wayland while this Avalonia build requires X11.\n' >&2
  exit 1
fi
if grep -Fq -- '--talk-name=org.freedesktop.portal.' "$flatpak_manifest"; then
  printf 'Flatpak manifest should not request portal talk-name access; portals are allowed by default.\n' >&2
  exit 1
fi
require_text "$flatpak_manifest" '      - install -d /app/bin /app/lib/hourglass-linux'
require_text "$flatpak_manifest" '      - cp -a publish/. /app/lib/hourglass-linux/'
require_text "$flatpak_manifest" '        exec /app/lib/hourglass-linux/hourglass-linux "$@"'
require_text "$flatpak_manifest" '      - chmod +x /app/bin/hourglass-linux'

while IFS= read -r -d '' sound; do
  relative_sound=${sound#"$repo_root/src/Hourglass.Linux.Avalonia/"}
  require_file "$publish_dir/$relative_sound"
done < <(find "$repo_root/src/Hourglass.Linux.Avalonia/Assets/Sounds" -maxdepth 1 -type f -name '*.wav' -print0 | sort -z)

require_executable "$appdir/AppRun"
require_file "$appdir/$app_id.desktop"
require_file "$appdir/hourglass.png"
require_file "$appdir/hourglass.svg"
require_file "$appdir/.DirIcon"
require_executable "$appdir/usr/bin/hourglass-linux"
require_file "$appdir/usr/share/applications/$app_id.desktop"
require_file "$appdir/usr/share/metainfo/$app_id.metainfo.xml"
require_file "$appdir/usr/share/icons/hicolor/256x256/apps/hourglass.png"
require_file "$appdir/usr/share/icons/hicolor/scalable/apps/hourglass.svg"
require_file "$appdir/usr/bin/licenses/LICENSE.md"
require_file "$appdir/usr/bin/licenses/Microsoft.NETCore.App.Runtime.linux-x64/LICENSE.TXT"
require_file "$appdir/usr/bin/licenses/Microsoft.NETCore.App.Runtime.linux-x64/THIRD-PARTY-NOTICES.TXT"
require_file "$appdir/usr/bin/licenses/harfbuzzsharp.nativeassets.linux/LICENSE.txt"
require_file "$appdir/usr/bin/licenses/harfbuzzsharp.nativeassets.linux/THIRD-PARTY-NOTICES.txt"
require_file "$appdir/usr/bin/licenses/skiasharp.nativeassets.linux/LICENSE.txt"
require_file "$appdir/usr/bin/licenses/skiasharp.nativeassets.linux/THIRD-PARTY-NOTICES.txt"

if find "$publish_dir" -type f -name '*.pdb' -print -quit | grep -q .; then
  printf 'Release publish output should not contain portable debug symbols.\n' >&2
  exit 1
fi

if find "$appdir" -type f -name '*.pdb' -print -quit | grep -q .; then
  printf 'AppDir should not contain portable debug symbols.\n' >&2
  exit 1
fi

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$desktop_file"
elif [[ "$require_validators" == true ]]; then
  printf 'Missing required validator: desktop-file-validate\n' >&2
  exit 1
else
  printf 'Skipping desktop-file-validate: command not found\n' >&2
fi

if command -v appstreamcli >/dev/null 2>&1; then
  appstreamcli validate --no-net "$metainfo_file"
elif [[ "$require_validators" == true ]]; then
  printf 'Missing required validator: appstreamcli\n' >&2
  exit 1
else
  printf 'Skipping appstreamcli validate: command not found\n' >&2
fi
