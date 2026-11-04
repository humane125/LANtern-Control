#!/usr/bin/env bash
set -euo pipefail

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
binary="$here/payload/LANtern Control"

applescript_string() {
  local escaped=${1//\\/\\\\}
  printf '"%s"' "${escaped//\"/\\\"}"
}

command="quoted form of $(applescript_string "$binary")"
for arg in "$@"; do
  command="$command & \" \" & quoted form of $(applescript_string "$arg")"
done

if stderr=$(osascript -e "do shell script ($command) with administrator privileges" 2>&1 >/dev/null); then
  exit 0
fi

# -128 means the user cancelled the prompt; open unprivileged so the app can explain.
case "$stderr" in
  *-128*)
    exec "$binary" "$@"
    ;;
  *)
    echo "LANtern Control could not start with administrator privileges: $stderr" >&2
    exit 1
    ;;
esac
