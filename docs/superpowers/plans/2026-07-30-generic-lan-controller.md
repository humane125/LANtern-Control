# Generic LAN Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a router-independent Windows executable for IPv4 LAN discovery, live traffic measurement, per-device limits, and temporary internet pause.

**Architecture:** A WPF shell displays adapter and device state. A testable core owns subnet enumeration, frame encoding/parsing, device accounting, and rate rules; a SharpPcap Windows service owns capture, injection, ARP interception, forwarding, and restoration.

**Tech Stack:** .NET 8, WPF, SharpPcap 6.3.1, xUnit

## Global Constraints

- No router login, router-specific endpoint, or stored router credential.
- Require Windows 10/11 x64 and an installed WinPcap/Npcap-compatible runtime.
- Support IPv4 broadcast LANs with at most 1024 addresses.
- Never control the selected computer or its gateway.
- Restore real ARP mappings whenever control stops or the process closes.
- Label the feature `Pause internet`; do not claim Wi-Fi radio deauthentication.
- Persist rules under `%LOCALAPPDATA%\LANternControl`, but activate them only after the user starts control.

---

### Task 1: Core network models and frame codec

**Files:**
- Create: `LanternControl/src/Lantern.Core/Lantern.Core.csproj`
- Create: `LanternControl/src/Lantern.Core/Networking/AdapterProfile.cs`
- Create: `LanternControl/src/Lantern.Core/Networking/SubnetScanner.cs`
- Create: `LanternControl/src/Lantern.Core/Networking/EthernetFrameCodec.cs`
- Test: `LanternControl/tests/Lantern.Core.Tests/NetworkingTests.cs`

**Interfaces:**
- Produces: `AdapterProfile`, `SubnetScanner.EnumerateHosts()`, and raw Ethernet/ARP parse/build methods.

- [ ] Write failing tests with literal `/24` and `/30` host lists and byte-exact ARP request/reply fixtures.
- [ ] Run the tests and confirm failure because the core types do not exist.
- [ ] Implement address validation, bounded subnet enumeration, VLAN-aware Ethernet offsets, ARP parsing, and ARP request/reply encoding.
- [ ] Run the targeted tests and confirm they pass.

### Task 2: Traffic rules and device accounting

**Files:**
- Create: `LanternControl/src/Lantern.Core/Control/TrafficRule.cs`
- Create: `LanternControl/src/Lantern.Core/Control/TokenBucket.cs`
- Create: `LanternControl/src/Lantern.Core/Devices/DeviceRecord.cs`
- Create: `LanternControl/src/Lantern.Core/Devices/DeviceRegistry.cs`
- Test: `LanternControl/tests/Lantern.Core.Tests/TrafficControlTests.cs`

**Interfaces:**
- Produces: per-direction rule decisions and one-second device rate snapshots.

- [ ] Write failing tests for unlimited, paused, exact-token, depleted-token, refill, direction accounting, and rate ordering behavior.
- [ ] Run the tests and verify the expected missing-type failures.
- [ ] Implement monotonic token buckets and thread-safe per-device accounting.
- [ ] Run the targeted and full core test suites.

### Task 3: Settings persistence

**Files:**
- Create: `LanternControl/src/Lantern.Core/Settings/AppSettings.cs`
- Create: `LanternControl/src/Lantern.Core/Settings/SettingsStore.cs`
- Test: `LanternControl/tests/Lantern.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: atomic load/save of aliases and limits keyed by normalized MAC.

- [ ] Write a failing round-trip test using a temporary directory and malformed-file fallback test.
- [ ] Verify both tests fail before implementation.
- [ ] Implement JSON persistence through a temporary file and atomic replacement.
- [ ] Run all core tests.

### Task 4: Windows packet engine

**Files:**
- Create: `LanternControl/src/Lantern.App/Services/WindowsAdapterService.cs`
- Create: `LanternControl/src/Lantern.App/Services/PcapLanEngine.cs`
- Create: `LanternControl/src/Lantern.App/Services/NpfDriverService.cs`

**Interfaces:**
- Consumes: core frame codec, registry, and traffic rules.
- Produces: adapter enumeration, scan, start/stop control, traffic forwarding, and ARP restoration.

- [ ] Add a failing boundary test using recorded raw frames for upload/download direction and forwarding MAC rewrites.
- [ ] Implement adapter-to-pcap matching by physical MAC, gateway discovery, ARP scan, capture callback, periodic poisoning, rule enforcement, and corrective restoration.
- [ ] Run boundary and core tests.

### Task 5: Native WPF user interface

**Files:**
- Create: `LanternControl/src/Lantern.App/Lantern.App.csproj`
- Create: `LanternControl/src/Lantern.App/App.xaml`
- Create: `LanternControl/src/Lantern.App/App.xaml.cs`
- Create: `LanternControl/src/Lantern.App/MainWindow.xaml`
- Create: `LanternControl/src/Lantern.App/MainWindow.xaml.cs`
- Create: `LanternControl/src/Lantern.App/ViewModels/DeviceViewModel.cs`
- Create: `LanternControl/src/Lantern.App/app.manifest`

**Interfaces:**
- Consumes: Windows adapter and packet services.
- Produces: elevated native desktop workflow.

- [ ] Build the dark adapter/status/header and accessible device grid with explicit labels.
- [ ] Wire start, stop/restore, rescan, editable limits, pause toggles, sorting, status, and settings persistence.
- [ ] Ensure window close awaits safe engine shutdown.
- [ ] Build and run unit tests.

### Task 6: Packaging and verification

**Files:**
- Create: `LanternControl/LanternControl.slnx`
- Create: `LanternControl/README.md`
- Create: `LanternControl/scripts/publish.ps1`

**Interfaces:**
- Produces: `outputs/LANtern-Control.exe`.

- [ ] Publish `win-x64` self-contained single-file Release output.
- [ ] Run all tests and inspect compiler output.
- [ ] Confirm the executable manifest requests Administrator access.
- [ ] Confirm the executable can enumerate the installed capture runtime when elevated; document the current non-elevated live-test boundary.
- [ ] Review the complete diff for credentials, generated junk, and unrelated changes.
