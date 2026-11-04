LANtern Control for macOS
=========================

LANtern Control runs natively on macOS (Apple Silicon and Intel) with the same
Avalonia interface as the Linux build. It uses the libpcap that ships with
macOS, so nothing else needs to be installed.

Requirements
------------
- macOS 15 or later, the oldest release .NET 8 supports:
  https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md
- The .NET 8 SDK to build the app: https://dotnet.microsoft.com/download
- Administrator rights. Opening a BPF capture device and changing
  net.inet.ip.forwarding both require root.

Build
-----
    packaging/macos/build-app.sh

This publishes a self-contained build for the current CPU and creates:

    dist/LANtern Control.app

Set LANTERN_RUNTIME=osx-x64 or LANTERN_RUNTIME=osx-arm64 to build for the
other architecture.

Run
---
Open "dist/LANtern Control.app" and approve the administrator prompt.

The app is not notarized. The first time, right-click it and choose Open, or
clear the quarantine flag once:

    xattr -dr com.apple.quarantine "dist/LANtern Control.app"

To explore the interface with synthetic data and no network access:

    "dist/LANtern Control.app/Contents/MacOS/payload/LANtern Control" --demo

Usage notes
-----------
- Select the adapter whose IP address and gateway match the network you want to
  manage (usually en0 for Wi-Fi, or a USB/Thunderbolt Ethernet interface).
- Click Start control, wait for devices to appear, then apply limits or pause
  internet access per device.
- Always click Stop & restore before quitting, changing networks, or sleeping,
  so corrective ARP mappings are sent to the LAN.
