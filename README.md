<p align="center">
  <img src="docs/assets/lantern-control-social-preview.png" alt="LANtern Control — Local Network Visibility &amp; Control" width="100%">
</p>

# LANtern Control

LANtern Control is a native Windows application for discovering devices and
applying SelfishNet-style IPv4 bandwidth controls without signing in to a
router dashboard.

## What it does

- Detects ordinary Ethernet and Wi-Fi LAN adapters at runtime.
- Discovers IPv4 devices from the Windows neighbor cache and normal LAN traffic.
- Resolves device names through reverse DNS, NetBIOS, mDNS, and passively observed DHCP hostnames, with inline saved aliases.
- Marks clients offline after missed liveness checks and hides them after 45 seconds while retaining saved names and limits.
- Restores the router and client ARP mappings directly when control stops, preventing stale controller identities in router dashboards.
- Refreshes the passive device list every five seconds without sweeping or
  probing every address on the subnet.
- Shows live download and upload rates for every redirected device.
- Applies independent download and upload limits in `KB/s`.
- Pauses a device's IPv4 internet in both directions.
- Saves rules per MAC address.
- Sends corrective ARP mappings when control stops or the window closes.
- Uses separate ARP and packet-forwarding handles with a dedicated blocking
  forwarding loop, matching the architecture validated with SelfishNet.

It contains no Xiaomi or Tunisie Telecom login code. The Xiaomi
`192.168.31.1` LAN can be used now; later, connect the PC directly to the
Tunisie Telecom LAN and select its `192.168.100.x` adapter.

## Requirements

- Windows 10 or 11, x64.
- Administrator access.
- WinPcap, or Npcap installed with **WinPcap API-compatible mode** enabled.
- The controller PC and target devices on the same IPv4 broadcast LAN.

The current PC already has the WinPcap runtime. LANtern Control starts its NPF
driver when launched as Administrator.

## Start

1. Open `LANtern-Control.exe` and accept the Windows Administrator prompt.
2. Select the adapter whose gateway is the router you want to control.
3. Click **Start control**.
4. Wait for devices to appear.
5. Enter download/upload limits in `KB/s`; `0` means unlimited.
6. Use **Pause internet** to stop a device's IPv4 internet.
7. Click **Stop & restore** before changing networks or closing the program.

No rule changes the router permanently. While control is active, LANtern uses
two-way forwarding for discovered devices so live download and upload rates stay
visible even when both limits are `0`. A zero value is unlimited; positive values
enable shaping, and the pause switch blocks forwarding. Corrective ARP traffic is
sent when control stops. Saved rules activate when control starts.

Device discovery combines the Windows neighbor cache with a paced ARP scan of
the local `/24`. The scan runs every five seconds and spaces requests out so it
does not create the burst that caused packet loss on weak routers. Larger
networks are limited to the controller PC's local `/24`.

LANtern uses normal rather than promiscuous packet capture, separate handles for
ARP and forwarded IP traffic, a one-millisecond blocking capture loop, and a
five-second ARP maintenance interval. If the forwarding loop fails, it cancels
control and restores normal ARP mappings automatically.

## Compatibility limits

This technique works on many ordinary home routers because it operates on the
local IPv4 LAN rather than through a vendor API. It cannot be guaranteed on
every Wi-Fi:

- Guest/client isolation prevents direct LAN control.
- Dynamic ARP inspection or ARP protection blocks interception.
- IPv6 traffic is not controlled in this release.
- Devices behind another router/NAT are not on the same broadcast LAN.
- Some Wi-Fi adapter drivers do not support raw packet injection.

Use this only on networks and devices you own or administer.

## Build

```powershell
dotnet test .\tests\Lantern.Core.Tests\Lantern.Core.Tests.csproj
.\scripts\publish.ps1
```

The self-contained executable is written to `release\LANtern-Control.exe`.

---

**This project was vibecoded so you can expect bugs please leave an issue if you find any**
