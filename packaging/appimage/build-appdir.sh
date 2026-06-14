#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  printf 'Usage: %s <publish-dir> <appdir>\n' "$0" >&2
  exit 2
fi

publish_dir=$1
appdir=$2
app_id=io.github.MattBunch.Hourglass

if [[ ! -x "$publish_dir/hourglass-linux" ]]; then
  printf 'Expected executable not found: %s\n' "$publish_dir/hourglass-linux" >&2
  exit 1
fi

rm -rf "$appdir"
mkdir -p \
  "$appdir/usr/bin" \
  "$appdir/usr/share/applications" \
  "$appdir/usr/share/metainfo"

cp -a "$publish_dir"/. "$appdir/usr/bin/"
install -Dm644 "packaging/linux/$app_id.desktop" "$appdir/usr/share/applications/$app_id.desktop"
install -Dm644 "packaging/linux/$app_id.metainfo.xml" "$appdir/usr/share/metainfo/$app_id.metainfo.xml"

cat > "$appdir/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
appdir=$(dirname "$(readlink -f "$0")")
exec "$appdir/usr/bin/hourglass-linux" "$@"
EOF
chmod +x "$appdir/AppRun"

ln -s "usr/share/applications/$app_id.desktop" "$appdir/$app_id.desktop"
