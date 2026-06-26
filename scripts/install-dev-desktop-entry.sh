#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)

png_source="$repo_root/src/Hourglass.Linux.Avalonia/Assets/hourglass.png"
svg_source="$repo_root/src/Hourglass.Linux.Avalonia/Assets/hourglass.svg"
launcher_source="$repo_root/scripts/run-hourglass-dev.sh"

data_home=${XDG_DATA_HOME:-"$HOME/.local/share"}
png_destination="$data_home/icons/hicolor/256x256/apps/hourglass.png"
svg_destination="$data_home/icons/hicolor/scalable/apps/hourglass.svg"
desktop_destination="$data_home/applications/io.github.MattBunch.Hourglass.Devel.desktop"
icon_theme_root="$data_home/icons/hicolor"

if [[ ! -f "$png_source" ]]; then
  printf 'Expected PNG icon not found: %s\n' "$png_source" >&2
  exit 1
fi

if [[ ! -f "$svg_source" ]]; then
  printf 'Expected SVG icon not found: %s\n' "$svg_source" >&2
  exit 1
fi

if [[ ! -x "$launcher_source" ]]; then
  printf 'Expected executable development launcher not found: %s\n' "$launcher_source" >&2
  exit 1
fi

install -Dm644 "$png_source" "$png_destination"
install -Dm644 "$svg_source" "$svg_destination"
mkdir -p -- "$(dirname -- "$desktop_destination")"

cat > "$desktop_destination" <<EOF
[Desktop Entry]
Type=Application
Name=Hourglass (Dev)
Comment=Simple timer for Linux
Exec="$launcher_source"
Icon=hourglass
Terminal=false
Categories=Utility;
StartupNotify=true
StartupWMClass=hourglass
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$(dirname -- "$desktop_destination")" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1 && [[ -f "$icon_theme_root/index.theme" ]]; then
  gtk-update-icon-cache -q -t "$icon_theme_root" >/dev/null 2>&1 || true
fi

printf 'Installed PNG icon: %s\n' "$png_destination"
printf 'Installed SVG icon: %s\n' "$svg_destination"
printf 'Installed desktop entry: %s\n' "$desktop_destination"
