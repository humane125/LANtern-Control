# LANtern Control

LANtern Control is a native Windows application for discovering devices and
applying SelfishNet-style IPv4 bandwidth controls without signing in to a
router dashboard.

## What it does

- Detects ordinary Ethernet and Wi-Fi LAN adapters at runtime.
- Discovers IPv4 devices on the selected subnet.
- Shows live per-device download and upload rates after control starts.
- Applies independent download/upload limits in `KB/s`.
- Pauses a device's IPv4 internet while the application is running.
- Saves rules per MAC address.
- Sends corrective ARP mappings when control stops or the window closes.

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

No rule changes the router permanently. Saved rules activate only when control
is started.

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
