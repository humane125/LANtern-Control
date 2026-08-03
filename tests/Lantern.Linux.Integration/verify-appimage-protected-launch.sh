#!/usr/bin/env bash
set -euo pipefail

appimage=${1:-}
if [[ -z ${appimage} || ! -x ${appimage} ]]; then
  echo "Usage: $0 /absolute/path/to/LANtern-Control.AppImage" >&2
  exit 2
fi
if ! command -v xvfb-run >/dev/null 2>&1; then
  echo "xvfb-run is required for the protected launch test" >&2
  exit 2
fi

work_dir=$(mktemp -d /tmp/lantern-appimage-protected-XXXXXX)
cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT

cd "${work_dir}"
"${appimage}" --appimage-extract >/dev/null
version=$(cat squashfs-root/usr/share/lantern-control/version)
/bin/sh squashfs-root/usr/libexec/lantern-control/install-privileged \
  "${work_dir}/squashfs-root" "${version}"

installed_root="/opt/lantern-control/${version}"
ethtool_capabilities=$(
  LD_LIBRARY_PATH="${installed_root}/usr/lib" \
    "${installed_root}/usr/bin/getcap" "${installed_root}/usr/bin/ethtool"
)
if [[ ${ethtool_capabilities} != *cap_net_admin* ]]; then
  echo "Protected ethtool helper does not have CAP_NET_ADMIN" >&2
  exit 1
fi

test_user=lantern-appimage-test
if ! id "${test_user}" >/dev/null 2>&1; then
  useradd --no-create-home --shell /bin/sh "${test_user}"
fi
user_home="${work_dir}/home"
mkdir -p "${user_home}"
chown "${test_user}:${test_user}" "${user_home}"

set +e
timeout 8s su -s /bin/sh "${test_user}" -c \
  "HOME='${user_home}' xvfb-run -a '${installed_root}/usr/bin/LANtern-Control' --demo" \
  >"${work_dir}/launch.log" 2>&1
status=$?
set -e

if [[ ${status} -ne 124 ]]; then
  cat "${work_dir}/launch.log" >&2
  echo "Protected LANtern Control process exited during startup (status ${status})" >&2
  exit 1
fi

echo "PASS: protected LANtern Control launch stayed running as a normal user."
