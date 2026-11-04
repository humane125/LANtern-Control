#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
project="$repo_root/src/Lantern.Linux/Lantern.Linux.csproj"

dotnet=$(command -v dotnet || true)
if [ -z "$dotnet" ] && [ -x "$HOME/.dotnet/dotnet" ]; then
  dotnet="$HOME/.dotnet/dotnet"
fi
if [ -z "$dotnet" ]; then
  echo "The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download" >&2
  exit 1
fi

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)
# hw.optional.arm64 reports the real CPU even when this shell runs under Rosetta.
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = "1" ]; then
  host_arch="arm64"
else
  host_arch="x64"
fi
runtime=${LANTERN_RUNTIME:-osx-$host_arch}
app_dir="$repo_root/dist/LANtern Control.app"
contents_dir="$app_dir/Contents"
payload_dir="$contents_dir/MacOS/payload"

echo "Publishing LANtern Control $version for $runtime..."
rm -rf "$app_dir"
"$dotnet" publish "$project" \
  --configuration Release \
  --runtime "$runtime" \
  --self-contained true \
  --output "$payload_dir"

mkdir -p "$contents_dir/Resources"
install -m 0755 "$repo_root/packaging/macos/launcher.sh" "$contents_dir/MacOS/lantern-launcher"
sed "s/@VERSION@/$version/g" "$repo_root/packaging/macos/Info.plist" > "$contents_dir/Info.plist"

icon_source="$repo_root/packaging/linux/lantern-control.png"
work_dir=$(mktemp -d)
iconset="$work_dir/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$contents_dir/Resources/AppIcon.icns"
rm -rf "$work_dir"

# Apple Silicon refuses to run unsigned code, and adding the launcher and
# Info.plist invalidates the payload's signature, so re-sign the bundle ad hoc.
codesign --force --deep --sign - "$app_dir"
codesign --verify --deep --strict "$app_dir"

echo "Built $app_dir"
