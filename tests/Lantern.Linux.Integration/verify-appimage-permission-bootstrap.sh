#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
app_run="${repo_root}/packaging/linux/AppRun"
installer="${repo_root}/packaging/linux/install-privileged.sh"
builder="${repo_root}/packaging/linux/build-appimage.sh"

[[ -f ${installer} ]] || { echo "Missing privileged installer" >&2; exit 1; }
grep -q 'command -v pkexec' "${app_run}" || { echo "AppRun does not use a graphical polkit prompt" >&2; exit 1; }
grep -q '/opt/lantern-control' "${app_run}" || { echo "AppRun does not use a protected install" >&2; exit 1; }
grep -q 'payload.sha256' "${app_run}" || { echo "AppRun cannot detect an updated payload" >&2; exit 1; }
grep -q 'bootstrap_dir=$(mktemp -d' "${app_run}" || { echo "AppRun does not stage bootstrap files outside the FUSE mount" >&2; exit 1; }
grep -q 'cp -R -- "$app_dir/usr/." "$bootstrap_dir/usr/"' "${app_run}" || { echo "AppRun does not copy the bootstrap payload to the staging directory" >&2; exit 1; }
grep -q 'pkexec /bin/sh "$bootstrap_dir/usr/libexec/lantern-control/install-privileged"' "${app_run}" || { echo "AppRun does not execute the staged helper through pkexec" >&2; exit 1; }
if grep -q 'pkexec "$app_dir/usr/libexec/lantern-control/install-privileged"' "${app_run}"; then
  echo "AppRun still asks pkexec to enter the user-only FUSE mount" >&2
  exit 1
fi
grep -q 'cap_net_raw,cap_net_admin=eip' "${installer}" || { echo "Installer grants the wrong capabilities" >&2; exit 1; }
grep -q '"$setcap_tool" cap_net_admin=eip "$ethtool_path"' "${installer}" || { echo "Bundled ethtool is missing CAP_NET_ADMIN" >&2; exit 1; }
grep -q 'chmod 0755 "$stage_dir"' "${installer}" || { echo "Installed version directory is not traversable by the desktop user" >&2; exit 1; }
grep -q 'install-privileged.sh' "${builder}" || { echo "Builder does not package the installer" >&2; exit 1; }
grep -q 'libcap2-bin' "${repo_root}/packaging/linux/Dockerfile.appimage" || { echo "Builder does not provide setcap/getcap" >&2; exit 1; }
grep -q 'find \. .* -type f .* -type l' "${builder}" || { echo "Payload marker does not cover packaged files and symbolic links" >&2; exit 1; }
if grep -q 'sha256sum "$app_dir/usr/bin/LANtern-Control"' "${builder}"; then
  echo "Payload marker still covers only the application binary" >&2
  exit 1
fi

echo "PASS: AppImage uses a one-time graphical least-privilege bootstrap."
