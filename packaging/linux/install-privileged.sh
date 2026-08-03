#!/usr/bin/env sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
  echo "LANtern Control setup must run through pkexec." >&2
  exit 1
fi

source_dir=${1:-}
version=${2:-}
case "$version" in
  ''|*[!A-Za-z0-9._-]*)
    echo "Invalid LANtern Control version." >&2
    exit 1
    ;;
esac

payload="$source_dir/usr/bin/LANtern-Control"
marker="$source_dir/usr/share/lantern-control/payload.sha256"
if [ ! -f "$payload" ] || [ ! -f "$marker" ]; then
  echo "The AppImage payload is incomplete." >&2
  exit 1
fi

install_root=/opt/lantern-control
final_dir="$install_root/$version"
mkdir -p "$install_root"
chmod 0755 "$install_root"
stage_dir=$(mktemp -d "$install_root/.install-$version.XXXXXX")
chmod 0755 "$stage_dir"
backup_dir="$install_root/.previous-$version-$$"

cleanup() {
  if [ -n "${stage_dir:-}" ] && [ -d "$stage_dir" ]; then
    rm -rf -- "$stage_dir"
  fi
}
trap cleanup EXIT HUP INT TERM

mkdir -p "$stage_dir/usr"
cp -a -- "$source_dir/usr/." "$stage_dir/usr/"
chown -R root:root "$stage_dir"
chmod -R go-w "$stage_dir"
chmod 0755 "$stage_dir/usr/bin/LANtern-Control"

setcap_tool=$(command -v setcap || true)
getcap_tool=$(command -v getcap || true)
if [ -z "$setcap_tool" ]; then
  setcap_tool="$stage_dir/usr/bin/setcap"
fi
if [ -z "$getcap_tool" ]; then
  getcap_tool="$stage_dir/usr/bin/getcap"
fi
if [ -z "$setcap_tool" ] || [ -z "$getcap_tool" ]; then
  echo "setcap/getcap are unavailable." >&2
  exit 1
fi

"$setcap_tool" cap_net_raw,cap_net_admin=eip "$stage_dir/usr/bin/LANtern-Control"
capabilities=$("$getcap_tool" "$stage_dir/usr/bin/LANtern-Control")
case "$capabilities" in
  *cap_net_admin*cap_net_raw*|*cap_net_raw*cap_net_admin*) ;;
  *)
    echo "Could not verify LANtern Control network capabilities." >&2
    exit 1
    ;;
esac

ethtool_path="$stage_dir/usr/bin/ethtool"
if [ ! -x "$ethtool_path" ]; then
  echo "The bundled ethtool helper is unavailable." >&2
  exit 1
fi
"$setcap_tool" cap_net_admin=eip "$ethtool_path"
ethtool_capabilities=$("$getcap_tool" "$ethtool_path")
case "$ethtool_capabilities" in
  *cap_net_admin*) ;;
  *)
    echo "Could not verify the ethtool network capability." >&2
    exit 1
    ;;
esac

if [ -e "$final_dir" ]; then
  mv -- "$final_dir" "$backup_dir"
fi
if ! mv -- "$stage_dir" "$final_dir"; then
  if [ -e "$backup_dir" ]; then
    mv -- "$backup_dir" "$final_dir"
  fi
  exit 1
fi
stage_dir=
if [ -e "$backup_dir" ]; then
  rm -rf -- "$backup_dir"
fi

chmod -R go-w "$final_dir"
exit 0
