#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
  echo "Run this test as root." >&2
  exit 2
fi

controller_binary=${1:-}
if [[ -z ${controller_binary} || ! -x ${controller_binary} ]]; then
  echo "Usage: $0 /absolute/path/to/Lantern.Linux.Integration" >&2
  exit 2
fi

suffix=$$
ctl_ns="lnctl${suffix}"
client_ns="lncli${suffix}"
router_ns="lnrt${suffix}"
server_ns="lnsrv${suffix}"
bridge="lnbr${suffix}"
control_dir="/tmp/lantern-e2e-${suffix}"
log_path="${control_dir}/controller.log"
controller_pid=""
server_pid=""

cleanup() {
  local exit_code=$?
  set +e
  if (( exit_code != 0 )) && [[ -f ${log_path} ]]; then
    echo "--- controller log ---" >&2
    cat "${log_path}" >&2
  fi
  if (( exit_code != 0 )) && [[ -f ${control_dir}/http.log ]]; then
    echo "--- HTTP server log ---" >&2
    cat "${control_dir}/http.log" >&2
  fi
  if (( exit_code != 0 )) && [[ -s ${control_dir}/upload-error ]]; then
    echo "--- upload server error ---" >&2
    cat "${control_dir}/upload-error" >&2
  fi
  if [[ -n ${controller_pid} ]]; then
    kill "${controller_pid}" 2>/dev/null || true
    wait "${controller_pid}" 2>/dev/null || true
  fi
  if [[ -n ${server_pid} ]]; then
    kill "${server_pid}" 2>/dev/null || true
    wait "${server_pid}" 2>/dev/null || true
  fi
  for namespace in "${ctl_ns}" "${client_ns}" "${router_ns}" "${server_ns}"; do
    ip netns del "${namespace}" 2>/dev/null || true
  done
  ip link del "${bridge}" 2>/dev/null || true
  if [[ ${control_dir} == /tmp/lantern-e2e-[0-9]* ]]; then
    rm -rf -- "${control_dir}"
  fi
}
trap cleanup EXIT

mkdir -p "${control_dir}"
for namespace in "${ctl_ns}" "${client_ns}" "${router_ns}" "${server_ns}"; do
  ip netns add "${namespace}"
  ip -n "${namespace}" link set lo up
done

ip link add "${bridge}" type bridge
ip link set "${bridge}" up

add_lan_peer() {
  local host_name=$1
  local peer_name=$2
  local namespace=$3
  ip link add "${host_name}" type veth peer name "${peer_name}"
  ip link set "${peer_name}" netns "${namespace}"
  ip link set "${host_name}" master "${bridge}"
  ip link set "${host_name}" up
}

add_lan_peer "lnch${suffix}" ctl0 "${ctl_ns}"
add_lan_peer "lnclh${suffix}" cli0 "${client_ns}"
add_lan_peer "lnrh${suffix}" lan0 "${router_ns}"
ip link add rtw0 type veth peer name srv0
ip link set rtw0 netns "${router_ns}"
ip link set srv0 netns "${server_ns}"

ip -n "${ctl_ns}" link set ctl0 address 02:77:00:00:00:02
ip -n "${ctl_ns}" addr add 10.77.0.2/24 dev ctl0
ip -n "${ctl_ns}" link set ctl0 up
ip -n "${ctl_ns}" route add default via 10.77.0.1
# Reproduce a real machine where Docker, a VPN, or a hotspot left kernel
# forwarding enabled. LANtern must disable this while active or both the kernel
# and the app forward each intercepted frame, delivering two copies.
ip netns exec "${ctl_ns}" sysctl -q -w net.ipv4.ip_forward=1
controller_ip_forward_before=$(ip netns exec "${ctl_ns}" cat /proc/sys/net/ipv4/ip_forward)

ip -n "${client_ns}" link set cli0 address 02:77:00:00:00:03
ip -n "${client_ns}" addr add 10.77.0.3/24 dev cli0
ip -n "${client_ns}" link set cli0 up
ip -n "${client_ns}" route add default via 10.77.0.1

ip -n "${router_ns}" link set lan0 address 02:77:00:00:00:01
ip -n "${router_ns}" addr add 10.77.0.1/24 dev lan0
ip -n "${router_ns}" link set lan0 up
ip -n "${router_ns}" link set rtw0 address 02:88:00:00:00:01
ip -n "${router_ns}" addr add 10.88.0.1/24 dev rtw0
ip -n "${router_ns}" link set rtw0 up
ip netns exec "${router_ns}" sysctl -q -w net.ipv4.ip_forward=1

ip -n "${server_ns}" link set srv0 address 02:88:00:00:00:02
ip -n "${server_ns}" addr add 10.88.0.2/24 dev srv0
ip -n "${server_ns}" link set srv0 up
ip -n "${server_ns}" route add default via 10.88.0.1

# veth can expose packets before Linux has materialized checksum/GSO fields.
# A physical LAN capture sees wire-ready frames, so turn off virtual offloads
# to make this topology match the packets libpcap receives from Wi-Fi/Ethernet.
for endpoint in \
  "${client_ns}:cli0" \
  "${router_ns}:lan0" "${router_ns}:rtw0" "${server_ns}:srv0"; do
  namespace=${endpoint%%:*}
  interface=${endpoint##*:}
  ip netns exec "${namespace}" ethtool -K "${interface}" \
    rx off tx off tso off gso off gro off lro off 2>/dev/null || true
done
controller_offloads_before=$(ip netns exec "${ctl_ns}" ethtool -k ctl0 | \
  grep -E '^(tcp-segmentation-offload|generic-segmentation-offload|generic-receive-offload|large-receive-offload):')
for interface in "lnch${suffix}" "lnclh${suffix}" "lnrh${suffix}"; do
  ethtool -K "${interface}" rx off tx off tso off gso off gro off lro off 2>/dev/null || true
done

wait_state() {
  local expected=$1
  for _ in $(seq 1 400); do
    if [[ -f ${control_dir}/state ]] && grep -q "^${expected} " "${control_dir}/state"; then
      return 0
    fi
    sleep 0.05
  done
  echo "Timed out waiting for controller state '${expected}'." >&2
  [[ -f ${log_path} ]] && cat "${log_path}" >&2
  exit 3
}

command_controller() {
  local command=$1
  local expected=$2
  rm -f "${control_dir}/state"
  printf '%s\n' "${command}" > "${control_dir}/command"
  wait_state "${expected}"
}

echo "[1/9] Baseline routing"
ip netns exec "${client_ns}" ping -q -c 3 -W 1 10.88.0.2 >/dev/null

echo "[2/9] Start real LinuxLanEngine/libpcap controller"
ip netns exec "${ctl_ns}" "${controller_binary}" \
  --controller ctl0 02:77:00:00:00:03 "${control_dir}" >"${log_path}" 2>&1 &
controller_pid=$!
wait_state active
controller_ip_forward_active=$(ip netns exec "${ctl_ns}" cat /proc/sys/net/ipv4/ip_forward)
if [[ ${controller_ip_forward_active} != 0 ]]; then
  echo "Kernel IPv4 forwarding remained enabled while LANtern was active." >&2
  exit 13
fi
controller_offloads_active=$(ip netns exec "${ctl_ns}" ethtool -k ctl0 | \
  grep -E '^(tcp-segmentation-offload|generic-segmentation-offload|generic-receive-offload|large-receive-offload):')
if grep -q ': on$' <<<"${controller_offloads_active}"; then
  echo "Controller packet coalescing remained enabled:" >&2
  echo "${controller_offloads_active}" >&2
  exit 11
fi

echo "[3/9] Confirm bidirectional ARP interception and forwarding"
client_gateway_neighbor=$(ip -n "${client_ns}" neigh show 10.77.0.1)
router_client_neighbor=$(ip -n "${router_ns}" neigh show 10.77.0.3)
grep -qi 'lladdr 02:77:00:00:00:02' <<<"${client_gateway_neighbor}"
grep -qi 'lladdr 02:77:00:00:00:02' <<<"${router_client_neighbor}"
ip netns exec "${client_ns}" ping -q -c 4 -W 1 10.88.0.2 >/dev/null
command_controller snapshot snapshot
read -r _ forwarded_before_pause dropped_before_pause < "${control_dir}/state"
if (( forwarded_before_pause < 2 )); then
  echo "Expected forwarded frames, got ${forwarded_before_pause}." >&2
  exit 4
fi

echo "[4/9] Enforce upload limiting under burst pressure"
command_controller snapshot snapshot
read -r _ _ dropped_before_upload_limit < "${control_dir}/state"
rm -f "${control_dir}/state"
printf 'limit 0 1\n' > "${control_dir}/command"
wait_state limited
ip netns exec "${client_ns}" ping -f -q -s 1400 -c 50 -W 1 10.88.0.2 >/dev/null 2>&1 || true
sleep 1
command_controller snapshot snapshot
read -r _ _ dropped_after_upload_limit < "${control_dir}/state"
if (( dropped_after_upload_limit <= dropped_before_upload_limit )); then
  echo "Upload limiter did not reject excess burst frames." >&2
  exit 8
fi

echo "[5/9] Enforce download limiting under burst pressure"
rm -f "${control_dir}/state"
printf 'limit 1 0\n' > "${control_dir}/command"
wait_state limited
ip netns exec "${server_ns}" ping -f -q -s 1400 -c 50 -W 1 10.77.0.3 >/dev/null 2>&1 || true
sleep 1
command_controller snapshot snapshot
read -r _ _ dropped_after_download_limit < "${control_dir}/state"
if (( dropped_after_download_limit <= dropped_after_upload_limit )); then
  echo "Download limiter did not reject excess burst frames." >&2
  exit 9
fi

echo "[6/9] Pause only the target client"
command_controller snapshot snapshot
read -r _ _ dropped_before_pause < "${control_dir}/state"
command_controller pause paused
if ip netns exec "${client_ns}" ping -q -c 2 -W 1 10.88.0.2 >/dev/null 2>&1; then
  command_controller snapshot snapshot
  echo "Paused client still reached the server." >&2
  echo "Client gateway neighbor: $(ip -n "${client_ns}" neigh show 10.77.0.1)" >&2
  echo "Router client neighbor: $(ip -n "${router_ns}" neigh show 10.77.0.3)" >&2
  echo "Controller state: $(cat "${control_dir}/state" 2>/dev/null || true)" >&2
  exit 5
fi
command_controller snapshot snapshot
read -r _ _ dropped_after_pause < "${control_dir}/state"
if (( dropped_after_pause <= dropped_before_pause )); then
  echo "Pause did not produce dropped frames." >&2
  exit 6
fi

echo "[7/9] Resume unlimited traffic"
command_controller resume resumed

echo "[8/9] Stop and restore real ARP mappings"
ip netns exec "${router_ns}" python3 -c \
  'exec("import socket,time\ns=socket.socket(socket.AF_PACKET, socket.SOCK_RAW, socket.htons(0x0806))\ns.bind((\"lan0\", 0))\ns.settimeout(0.2)\nend=time.monotonic()+2\nwhile time.monotonic()<end:\n    try:\n        f=s.recv(2048)\n        print(f[6:12].hex(), int.from_bytes(f[20:22], \"big\"), f[22:28].hex(), \".\".join(map(str,f[28:32])))\n    except TimeoutError: pass")' \
  >"${control_dir}/arp-restore.log" 2>&1 &
server_pid=$!
sleep 0.1
command_controller stop stopped
wait "${controller_pid}"
controller_pid=""
controller_offloads_after=$(ip netns exec "${ctl_ns}" ethtool -k ctl0 | \
  grep -E '^(tcp-segmentation-offload|generic-segmentation-offload|generic-receive-offload|large-receive-offload):')
controller_ip_forward_after=$(ip netns exec "${ctl_ns}" cat /proc/sys/net/ipv4/ip_forward)
if [[ ${controller_ip_forward_after} != "${controller_ip_forward_before}" ]]; then
  echo "Kernel IPv4 forwarding was not restored after Stop." >&2
  echo "Before: ${controller_ip_forward_before}; after: ${controller_ip_forward_after}" >&2
  exit 14
fi
if [[ ${controller_offloads_after} != "${controller_offloads_before}" ]]; then
  echo "Controller offloads were not restored after Stop." >&2
  echo "Before:" >&2
  echo "${controller_offloads_before}" >&2
  echo "After:" >&2
  echo "${controller_offloads_after}" >&2
  exit 12
fi
wait "${server_pid}"
server_pid=""
if ! ip netns exec "${client_ns}" ping -q -c 3 -W 1 10.88.0.2 >/dev/null; then
  echo "Post-stop traffic did not recover." >&2
  echo "Router ARP frames during restoration:" >&2
  cat "${control_dir}/arp-restore.log" >&2
  ip -n "${client_ns}" neigh show >&2
  ip -n "${router_ns}" neigh show >&2
  exit 10
fi
grep -qi 'lladdr 02:77:00:00:00:01' < <(ip -n "${client_ns}" neigh show 10.77.0.1)
grep -qi 'lladdr 02:77:00:00:00:03' < <(ip -n "${router_ns}" neigh show 10.77.0.3)

echo "[9/9] PASS"
echo "PASS: discovery, two-way forwarding, both limit directions, pause/resume rules, and ARP restoration all worked."
echo "Rate pacing is deterministic in unit tests; this run verified both live directions reject excess bursts."
