# Generic LAN Controller Design

## Goal

Build a native Windows application that provides SelfishNet-style IPv4 device
discovery, live traffic visibility, bandwidth limits, and temporary internet
pause without logging into a router dashboard.

The first live target is the current Xiaomi LAN (`192.168.31.0/24`, gateway
`192.168.31.1`). The same executable must derive addressing from the selected
Windows adapter so it can later run on the Tunisie Telecom LAN or another
ordinary home network.

## Compatibility

- Windows 10/11 x64 with WinPcap or Npcap-compatible packet capture installed.
- Wired Ethernet or managed Wi-Fi adapter with packet injection support.
- The controller PC, gateway, and target devices must share one IPv4 broadcast
  domain.
- IPv4 networks up to 1024 host addresses are scanned.
- Guest/client isolation, dynamic ARP inspection, some mesh systems, and
  enterprise Wi-Fi can prevent ARP interception.
- The application does not require or store router credentials.

## Architecture

`LANtern Control` is a .NET 8 WPF desktop application with a testable core and
a Windows packet engine. It requests Administrator access, starts the installed
NPF capture driver when necessary, maps a SharpPcap device to the selected
Windows adapter, and derives the local address, prefix, gateway, and MAC at
runtime.

The packet engine sends ARP probes to discover clients. When control is started,
it periodically advertises the controller MAC between each active client and
the gateway. IPv4 frames then pass through the controller, where it records
per-device byte counts, forwards allowed packets with rewritten Ethernet
addresses, and drops packets according to pause or token-bucket rules.

Stopping control, removing a client, closing the app, or an unhandled shutdown
causes several corrective ARP replies to be sent with the real gateway/client
MAC mappings. No permanent router setting is changed.

## User Interface

The main window uses a compact dark desktop layout:

- Adapter selector showing interface name, local address, subnet, and gateway.
- Driver/control status with actionable errors.
- `Start control`, `Stop and restore`, and `Scan now` controls.
- Device table sorted by current combined bandwidth.
- Device name, IP, MAC, live download/upload, download/upload limit fields, and
  a clearly labeled `Pause internet` toggle.
- A persistent compatibility notice that control is IPv4-only and requires the
  same LAN.

Controls have visible keyboard focus, text labels, non-color-only state
indicators, stable loading states, and at least 40-pixel desktop hit areas.

## Data and Rules

Device identity is keyed by normalized MAC address. The registry records the
latest IP, optional reverse-DNS name, first/last seen timestamps, and byte
counters. The local computer and gateway are shown but cannot be paused or
limited.

Limits are entered as decimal `KB/s`; blank or zero means unlimited. Separate
download and upload token buckets use a short burst allowance. Paused traffic
is dropped in both directions while ARP interception remains active.

Settings are saved per MAC under `%LOCALAPPDATA%\LANternControl\settings.json`.
Saved rules are not activated until the user starts control on a compatible
adapter.

## Failure Handling

- Missing capture runtime: explain that WinPcap/Npcap is required.
- Driver cannot start: explain that Administrator access is required.
- No IPv4 gateway: reject that adapter without changing network state.
- Gateway MAC unresolved: retry ARP discovery, then stop safely.
- Capture failure: stop control and attempt ARP restoration.
- Adapter/network change: stop control, restore, then require a restart.
- App exit: stop capture, cancel timers, restore all controlled devices, and
  close the packet handle.

## Verification

Automated tests cover subnet enumeration, Ethernet/ARP frame encoding and
parsing, traffic direction, token-bucket boundaries, rule validation, settings
round-trips, and restoration frame contents.

Release verification builds a self-contained Windows x64 executable, runs all
tests, checks the manifest requests elevation, and confirms the executable
starts to the main window. Live packet mutation is enabled only from the
application's `Start control` button.
