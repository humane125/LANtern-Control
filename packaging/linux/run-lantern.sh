#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
app_path="$script_dir/LANtern-Control"

if [ ! -x "$app_path" ]; then
  chmod +x "$app_path"
fi

exec "$app_path"
