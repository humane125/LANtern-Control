#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
version=${1:-0.1.0}
architecture=x86_64
project="$repo_root/src/Lantern.Linux/Lantern.Linux.csproj"
work_dir=${WORK_DIR:-/tmp/lantern-appimage-$version}
publish_dir=${PUBLISH_DIR:-$work_dir/publish}
app_dir="$work_dir/LANtern-Control.AppDir"
tool_dir="$work_dir/tools"
output_dir=${OUTPUT_DIR:-$repo_root/outputs}
output="$output_dir/LANtern-Control-v$version-$architecture.AppImage"

case "$work_dir" in
  /tmp/lantern-appimage-*|"$repo_root"/staging/appimage-*) ;;
  *)
    echo "Refusing to clean unexpected work directory: $work_dir" >&2
    exit 2
    ;;
esac

for command_name in curl ethtool getcap ldconfig setcap sha256sum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Missing required build command: $command_name" >&2
    exit 2
  fi
done

if [[ -z ${PUBLISH_DIR:-} ]]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet is required when PUBLISH_DIR is not provided" >&2
    exit 2
  fi
fi

libpcap=$(ldconfig -p | awk '/libpcap\.so\.0\.8 .*x86-64/{print $NF; exit}')
if [[ -z "$libpcap" ]]; then
  libpcap=$(ldconfig -p | awk '/libpcap\.so .*x86-64/{print $NF; exit}')
fi
if [[ -z "$libpcap" || ! -f "$libpcap" ]]; then
  echo "libpcap was not found on the build system" >&2
  exit 2
fi

rm -rf -- "$work_dir"
mkdir -p "$publish_dir" "$app_dir/usr/bin" "$app_dir/usr/lib" "$tool_dir" "$output_dir"

if [[ -z ${PUBLISH_DIR:-} ]]; then
  dotnet publish "$project" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:DebugSymbols=false \
    -p:DebugType=None \
    -o "$publish_dir"
fi

payload="$publish_dir/LANtern-Control"
if [[ ! -f "$payload" ]]; then
  payload="$publish_dir/LANtern Control"
fi

for required_file in "$payload" "$publish_dir/libHarfBuzzSharp.so" "$publish_dir/libSkiaSharp.so"; do
  if [[ ! -f "$required_file" ]]; then
    echo "Missing published payload: $required_file" >&2
    exit 2
  fi
done

install -Dm755 "$payload" "$app_dir/usr/bin/LANtern-Control"
install -Dm755 "$publish_dir/libHarfBuzzSharp.so" "$app_dir/usr/lib/libHarfBuzzSharp.so"
install -Dm755 "$publish_dir/libSkiaSharp.so" "$app_dir/usr/lib/libSkiaSharp.so"
install -Dm755 "$(command -v ethtool)" "$app_dir/usr/bin/ethtool"
install -Dm755 "$(command -v setcap)" "$app_dir/usr/bin/setcap"
install -Dm755 "$(command -v getcap)" "$app_dir/usr/bin/getcap"
install -Dm755 "$script_dir/AppRun" "$app_dir/AppRun"
install -Dm755 "$script_dir/install-privileged.sh" \
  "$app_dir/usr/libexec/lantern-control/install-privileged"
install -Dm644 /dev/null "$app_dir/usr/share/lantern-control/payload.sha256"
printf '%s\n' "$version" > "$app_dir/usr/share/lantern-control/version"
install -Dm644 "$script_dir/lantern-control.desktop" "$app_dir/lantern-control.desktop"
install -Dm644 "$script_dir/lantern-control.png" "$app_dir/lantern-control.png"
install -Dm644 "$script_dir/lantern-control.desktop" \
  "$app_dir/usr/share/applications/lantern-control.desktop"
install -Dm644 "$script_dir/lantern-control.png" \
  "$app_dir/usr/share/icons/hicolor/256x256/apps/lantern-control.png"
ln -s lantern-control.png "$app_dir/.DirIcon"

linuxdeploy="$tool_dir/linuxdeploy-$architecture.AppImage"
appimagetool="$tool_dir/appimagetool-$architecture.AppImage"
curl --fail --location --silent --show-error \
  https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage \
  --output "$linuxdeploy"
curl --fail --location --silent --show-error \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage \
  --output "$appimagetool"
chmod +x "$linuxdeploy" "$appimagetool"

NO_STRIP=1 "$linuxdeploy" --appimage-extract-and-run \
  --appdir "$app_dir" \
  --library "$libpcap"

# SharpPcap imports the native library as "wpcap" on every platform. The
# .NET Linux loader therefore probes wpcap.so and libwpcap.so, while Ubuntu
# and Linux Mint provide the implementation as libpcap.so.0.8. Keep the real
# bundled SONAME and expose both compatibility names inside the AppImage.
pcap_name=$(basename "$libpcap")
if [[ ! -f "$app_dir/usr/lib/$pcap_name" ]]; then
  echo "linuxdeploy did not bundle the expected libpcap library: $pcap_name" >&2
  exit 2
fi
ln -sfn "$pcap_name" "$app_dir/usr/lib/libwpcap.so"
ln -sfn "$pcap_name" "$app_dir/usr/lib/wpcap.so"

# File capabilities put glibc into secure-execution mode, where
# LD_LIBRARY_PATH is ignored. .NET probes native assets beside the managed
# executable, so expose every bundled native library from usr/bin while
# keeping the real files in usr/lib for dependency resolution.
for native_library in "$app_dir"/usr/lib/*.so*; do
  native_name=$(basename "$native_library")
  ln -sfn "../lib/$native_name" "$app_dir/usr/bin/$native_name"
done

# linuxdeploy may generate its own AppRun. Restore LANtern's explicit launcher
# and payload after dependency collection so the .NET single-file bundle is
# never modified by a packaging tool.
install -Dm755 "$payload" "$app_dir/usr/bin/LANtern-Control"
install -Dm755 "$script_dir/AppRun" "$app_dir/AppRun"
install -Dm755 "$script_dir/install-privileged.sh" \
  "$app_dir/usr/libexec/lantern-control/install-privileged"
printf '%s\n' "$version" > "$app_dir/usr/share/lantern-control/version"
payload_hash=$(
  cd "$app_dir/usr"
  find . \( -type f -o -type l \) ! -path './share/lantern-control/payload.sha256' -print0 |
    sort -z |
    while IFS= read -r -d '' payload_entry; do
      if [[ -L "$payload_entry" ]]; then
        printf 'L %s %s\n' "$payload_entry" "$(readlink "$payload_entry")"
      else
        entry_hash=$(sha256sum "$payload_entry" | awk '{print $1}')
        printf 'F %s %s\n' "$payload_entry" "$entry_hash"
      fi
    done |
    sha256sum |
    awk '{print $1}'
)
printf '%s\n' "$payload_hash" > "$app_dir/usr/share/lantern-control/payload.sha256"
install -Dm644 "$script_dir/lantern-control.desktop" "$app_dir/lantern-control.desktop"
install -Dm644 "$script_dir/lantern-control.png" "$app_dir/lantern-control.png"
ln -sfn lantern-control.png "$app_dir/.DirIcon"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$app_dir/lantern-control.desktop"
fi

rm -f -- "$output"
ARCH=$architecture "$appimagetool" --appimage-extract-and-run "$app_dir" "$output"
chmod +x "$output"

echo "$output"
