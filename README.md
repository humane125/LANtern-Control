<p align="center">
  <img src="assets/lantern-control-social-preview.png" alt="LANtern Control — local network visibility and control" width="100%">
</p>

# LANtern Control — Open-Source SelfishNet Alternative for Windows and Linux

<a href="https://github.com/humane125/LANtern-Control/releases/latest"><img alt="Release v0.1.3" src="https://img.shields.io/badge/release-v0.1.3-C51B3A?style=flat-square"></a>
<a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-C51B3A?style=flat-square"></a>
<img alt="C#" src="https://img.shields.io/badge/language-C%23-239120?style=flat-square&amp;logo=csharp">
<img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&amp;logo=dotnet">
<img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&amp;logo=windows11">
<img alt="Linux beta" src="https://img.shields.io/badge/Linux-beta-FCC624?style=flat-square&amp;logo=linux&amp;logoColor=black">

LANtern Control is an open-source SelfishNet, EvilLimiter, and NetCut
alternative for Windows and Linux. It is a router-independent desktop
application for discovering, monitoring, and controlling devices on a local
IPv4 network. It combines live per-device bandwidth monitoring, download and
upload limits, internet pause controls, visited-domain metadata, and per-device
domain rules without requiring access to the router's administration dashboard.

If you are looking for a maintained SelfishNet, EvilLimiter, or NetCut
alternative with a modern desktop interface, LANtern Control is built for that
use case.

> [!IMPORTANT]
> Windows `v0.1.3` is the stable release. Linux `v0.1.0` is available as a
> public beta AppImage. **macOS remains a work in progress (WIP).**

## Features

- Discover devices connected to the same Ethernet or Wi-Fi network.
- Resolve device names using DHCP, reverse DNS, NetBIOS, and mDNS information.
- Remember device names, aliases, and rules between application launches.
- Display live per-device download and upload speeds.
- Keep a ten-minute network activity chart with 2.5-second samples.
- Apply independent download and upload limits in `KB/s`.
- Apply per-device, per-service download and upload limits from Service
  Inspector. Service limits operate inside the device-wide maximum.
- Use Safe Mode to discover every device while forwarding traffic only for
  devices with active limits, pause controls, or domain rules.
- Pause and restore a device's IPv4 internet connection.
- Observe domains through DNS queries, TLS server names, and plain HTTP host
  headers without decrypting private HTTPS content.
- Block domains for individual devices, including presets for common services.
- Restore normal client and router ARP mappings when control stops.
- Check GitHub Releases for platform-specific optional updates.
- Install Windows through an MSI or portable ZIP, or run Linux through an
  AppImage with a one-time graphical privilege setup.

## Screenshots

The screenshots below use synthetic demo data and do not contain information
from a real network.

### Network overview

![LANtern Control network overview](assets/screenshots/overview.png)

### Visited domains

![LANtern Control visited domains](assets/screenshots/visited-domains.png)

### Domain rules

![LANtern Control domain rules](assets/screenshots/domain-rules.png)

## Requirements

### Windows

- Windows 10 or Windows 11, x64.
- Administrator access.
- [Npcap](https://npcap.com/#download) installed on the controller computer.
  WinPcap API-compatible mode is recommended for compatibility.

LANtern Control does not include or redistribute Npcap. Install Npcap separately
from its official website, then restart LANtern Control.

### Linux beta

- A 64-bit x86 Linux desktop. Linux Mint and Ubuntu are the primary tested
  distributions; other modern distributions may work.
- Polkit with `pkexec` for the one-time graphical administrator prompt.
- An AppImage-compatible environment. A FUSE-free launch fallback is included.

The Linux AppImage bundles the .NET runtime, Avalonia graphics libraries, and
libpcap. On first launch, it installs a protected payload under
`/opt/lantern-control/0.1.0` and grants only `CAP_NET_RAW` and `CAP_NET_ADMIN`.
The desktop interface continues to run as the normal user.

### Network

- The controller computer and target devices must be on the same IPv4 broadcast
  network.
- A stable connection is required. Ethernet is recommended. If Wi-Fi is used,
  keep the controller near the router and prefer 5 GHz when possible.

## Installation

Download the latest version from [GitHub Releases](https://github.com/humane125/LANtern-Control/releases/latest).

### Windows installer

1. Download `LANtern-Control-Setup-v0.1.3.msi`.
2. Run the setup program.
3. Choose the installation folder and whether to create Start Menu or desktop
   shortcuts.
4. Launch LANtern Control and accept the Administrator prompt.

### Windows portable version

1. Download `LANtern-Control-v0.1.3-win-x64.zip`.
2. Extract the complete archive to a folder where it can remain.
3. Run `LANtern Control.exe` as Administrator from that folder. Keep the
   extracted files together; no application installation is required.

### Linux AppImage beta

1. Download `LANtern-Control-v0.1.0-x86_64.AppImage`.
2. Make it executable through **Properties > Permissions > Allow executing file
   as program**, or run:

   ```bash
   chmod +x LANtern-Control-v0.1.0-x86_64.AppImage
   ```

3. Double-click the AppImage, or start it from a terminal:

   ```bash
   ./LANtern-Control-v0.1.0-x86_64.AppImage
   ```

4. Approve the graphical administrator prompt on first launch. Later launches
   of the same payload do not require another password.
5. If FUSE is unavailable, use:

   ```bash
   APPIMAGE_EXTRACT_AND_RUN=1 ./LANtern-Control-v0.1.0-x86_64.AppImage
   ```

Always click **Stop & restore** before closing LANtern, changing networks,
disconnecting the selected adapter, suspending, or rebooting.

## How to use

1. On Windows, launch LANtern Control as Administrator. On Linux, open the
   configured AppImage normally after completing its one-time setup.
2. Select the network adapter whose local IP address and gateway match the
   network you want to manage.
3. Click **Start control** and wait for connected devices to appear.
4. Click **Refresh devices** when you want to run a fresh discovery scan.
5. Enter a download or upload limit in `KB/s`. A value of `0` means unlimited.
6. Open **Service Inspector** to set independent download and upload limits for
   catalog services such as YouTube, Netflix, Discord, and Spotify. These limits
   remain inside the device-wide maximum. For example, a device capped at
   `2000 KB/s` with YouTube capped at `1000 KB/s` never exceeds `2000 KB/s`
   total; while YouTube uses `1000 KB/s`, other apps share the remaining
   `1000 KB/s`.
7. Enable **Safe Mode** to leave unrestricted devices on their normal direct
   router path. A Wi-Fi-only recommendation explains this option on launch and
   can be permanently dismissed with **Don't ask again**.
8. Use the **Internet** switch to pause or restore a device's connection.
9. Open **Visited domains** to inspect the destination domains LANtern can
   observe for each device.
10. Add individual domain rules or use a service preset to block domains for a
   selected device.
11. Click **Stop & restore** before changing adapters, leaving the network, or
   shutting down the controller computer.

Settings, saved device identities, and rules are stored locally in:

```text
Windows: %LOCALAPPDATA%\LANternControl\settings.json
Linux:   ~/.local/share/LANternControl/settings.json
```

LANtern also maintains `settings.backup.json` beside the primary file so a
temporary file lock or interrupted update cannot replace saved device names,
limits, presets, or domain rules with an empty configuration.

No router username or password is required, and LANtern does not permanently
change the router configuration.

## Domain visibility and blocking

LANtern can identify many destination domains, but it does not decrypt HTTPS,
read searches, inspect messages, or view private page contents. Domain blocking
is best effort because applications may use cached connections, several CDN
domains, or encrypted protocols.

The following can hide domains or bypass domain-based rules:

- VPNs, Tor, and proxy applications.
- Encrypted DNS such as DoH or DoT.
- TLS Encrypted Client Hello (ECH).
- QUIC/HTTP3 and cached or already-open connections.
- Applications that use direct IP addresses or frequently changing domains.

The same visibility limits apply to per-service bandwidth rules. Traffic that
cannot be classified still obeys its device-wide limit, but it cannot be charged
to a named service limit.

## Safe Mode

Safe Mode separates device discovery from traffic interception. LANtern keeps
unrestricted devices visible through ordinary ARP discovery and periodic ARP
checks without changing their client or gateway mappings. Their traffic travels
directly through the router.

A device is forwarded through LANtern when it has any device bandwidth limit,
service bandwidth limit, internet pause, or blocked-domain rule. Changing Safe
Mode or adding/removing the final rule applies immediately and sends corrective
ARP mappings when a device returns to its direct router path.

Because unrestricted Safe Mode devices bypass LANtern, their live bandwidth,
visited domains, and Service Inspector activity cannot be measured reliably.
Their presence, saved name, IP address, and configured rules remain visible.

## Network compatibility

LANtern operates on the local IPv4 network rather than through a router-specific
API, so it can work with many ordinary home routers. It cannot be guaranteed on
every network or adapter.

- IPv6 traffic is not controlled in this release.
- Guest Wi-Fi or client isolation can prevent discovery and control.
- Dynamic ARP inspection or other ARP protection can block interception.
- Devices behind another router or NAT are not on the same broadcast LAN.
- Some Wi-Fi drivers do not reliably support packet capture or injection.
- While a device is redirected, the controller computer becomes part of its
  traffic path. A weak controller connection can reduce speed or increase
  latency for that device.
- The controller must remain awake and connected while rules are active.

## Troubleshooting

### Npcap is not detected

Install the latest version from the [official Npcap download page](https://npcap.com/#download),
enable WinPcap API-compatible mode if offered, and restart LANtern Control.

### Devices do not appear

- Confirm that the correct adapter is selected.
- Make sure the controller and devices are on the same normal LAN, not an
  isolated guest network.
- Run LANtern Control as Administrator.
- Click **Refresh devices** and allow the discovery scan to finish.

### A device loses connectivity

Click **Stop & restore** and wait for the application to repair the normal ARP
mappings. If connectivity does not return, reconnect the affected device to
Wi-Fi or disable and re-enable its network adapter.

## Building from source

Both applications require the .NET 8 SDK. The WiX 5 SDK used to produce the
Windows MSI is restored automatically by `dotnet`.

```powershell
dotnet test .\tests\Lantern.Core.Tests\Lantern.Core.Tests.csproj
dotnet test .\tests\Lantern.App.Tests\Lantern.App.Tests.csproj
.\scripts\publish.ps1
```

The publishing script creates the portable application and installer in the
`outputs` folder.

The Linux AppImage is built from the Avalonia project and the packaging scripts
under `packaging/linux`. The reproducible compatibility image uses Ubuntu 20.04:

```bash
dotnet test ./tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj -c Release
dotnet publish ./src/Lantern.Linux/Lantern.Linux.csproj -c Release -r linux-x64 --self-contained true
./packaging/linux/build-appimage.sh 0.1.0
```

## License

LANtern Control is open-source software released under the [MIT License](LICENSE).

## Responsible use

Use LANtern Control only on networks and devices that you own or are authorized
to administer. Pausing, limiting, monitoring, or blocking another person's
traffic without permission may violate policies or local laws.

## Feedback and bug reports

If you find a problem, [open a GitHub issue](https://github.com/humane125/LANtern-Control/issues)
and include the LANtern version, operating system and distribution, adapter
type, capture-library version, router model, and steps needed to reproduce it.

---

**This project was vibecoded so you can expect bugs please leave an issue if you find any**
