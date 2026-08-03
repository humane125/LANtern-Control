LANtern Control for Linux - local test build v0.1.0
==================================================

Requirements
------------
- 64-bit Linux with a desktop environment.
- libpcap installed.
- The computer and target devices must share the same IPv4 LAN.
- Raw-packet privileges through Linux capabilities or root.

Install libpcap
---------------
Ubuntu / Debian:
  sudo apt update
  sudo apt install libpcap-dev libcap2-bin

Fedora:
  sudo dnf install libpcap libcap

Arch Linux:
  sudo pacman -S libpcap libcap

Run the test build
------------------
1. Extract the archive.
2. Open a terminal inside the extracted folder.
3. Make the app executable:
     chmod +x LANtern-Control
4. Preferred: grant only the packet privileges it needs:
     sudo setcap cap_net_raw,cap_net_admin=eip ./LANtern-Control
5. Launch it:
     ./LANtern-Control

If the desktop or filesystem does not preserve Linux capabilities, test it with:
  sudo ./LANtern-Control

Test checklist
--------------
1. Select the adapter whose gateway is your router.
2. Click Start control. The status should become Active.
3. Click Refresh devices. This performs the paced local subnet sweep.
4. Confirm the gateway is last and protected, and your other device appears.
5. Run a speed test on that other device. Its live rates should update every second.
6. Set a small download or upload limit. Zero means unlimited.
7. Try a domain preset, then open its Visited domains and Domain rules pages.
8. Click Stop & restore before closing or changing networks.

Safety
------
Test on a network you own or administer. Keep a terminal ready. If connectivity
does not recover after Stop & restore, close LANtern and reconnect the affected
device to Wi-Fi. This build controls IPv4 only.

Troubleshooting
---------------
"Permission denied" or capture-open failure:
  Re-run setcap, or launch once with sudo.

"libpcap" missing:
  Install the package shown above for your distribution.

No devices found:
  Confirm the selected adapter has the router as its gateway and that Wi-Fi
  client isolation is disabled.
