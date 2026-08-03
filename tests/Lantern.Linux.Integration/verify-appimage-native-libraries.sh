#!/usr/bin/env bash
set -euo pipefail

appimage=${1:-}
if [[ -z ${appimage} || ! -x ${appimage} ]]; then
  echo "Usage: $0 /absolute/path/to/LANtern-Control.AppImage" >&2
  exit 2
fi

work_dir=$(mktemp -d /tmp/lantern-appimage-native-XXXXXX)
cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT

cd "${work_dir}"
"${appimage}" --appimage-extract >/dev/null

library_dir="${work_dir}/squashfs-root/usr/lib"
ethtool_path="${work_dir}/squashfs-root/usr/bin/ethtool"
if [[ ! -x ${ethtool_path} ]]; then
  echo "Missing bundled ethtool helper" >&2
  exit 1
fi
LD_LIBRARY_PATH="${library_dir}" "${ethtool_path}" --version >/dev/null
if ! grep -q 'usr/bin.*PATH' "${work_dir}/squashfs-root/AppRun"; then
  echo "AppRun does not expose the bundled ethtool helper on PATH" >&2
  exit 1
fi

for compatibility_name in libwpcap.so wpcap.so; do
  library_path="${library_dir}/${compatibility_name}"
  if [[ ! -e ${library_path} ]]; then
    echo "Missing SharpPcap compatibility library: ${compatibility_name}" >&2
    exit 1
  fi

  resolved=$(readlink -f "${library_path}")
  case "$(basename "${resolved}")" in
    libpcap.so*) ;;
    *)
      echo "${compatibility_name} does not resolve to bundled libpcap" >&2
      exit 1
      ;;
  esac
done

LD_LIBRARY_PATH="${library_dir}" python3 - <<'PY'
import ctypes

ctypes.CDLL("libwpcap.so")
ctypes.CDLL("wpcap.so")
PY

max_glibc=$(
  find "${work_dir}/squashfs-root" -type f \
    -exec readelf --version-info '{}' ';' 2>/dev/null |
    grep -o 'GLIBC_[0-9.]*' |
    sed 's/^GLIBC_//' |
    sort -Vu |
    tail -1
)
baseline_glibc=2.31
newest=$(printf '%s\n%s\n' "${baseline_glibc}" "${max_glibc}" | sort -V | tail -1)
if [[ ${newest} != "${baseline_glibc}" ]]; then
  echo "AppImage requires GLIBC_${max_glibc}; maximum supported baseline is GLIBC_${baseline_glibc}" >&2
  exit 1
fi

echo "PASS: bundled libpcap and ethtool load correctly; maximum GLIBC is ${max_glibc}."
