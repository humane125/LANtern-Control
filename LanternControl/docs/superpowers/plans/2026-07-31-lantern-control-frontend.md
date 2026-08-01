# LANtern Control Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the approved compact WPF operations-console frontend using only real LANtern telemetry while preserving all existing network-control behavior.

**Architecture:** Keep packet capture and traffic policy code unchanged. Extend the existing device view models with numeric telemetry, collect a bounded two-minute aggregate history in a pure testable model, and render it through a focused WPF chart control. Recompose `MainWindow` around the approved sidebar, metrics, chart, and device table while the existing event handlers continue to own Start, Refresh, Stop and restore, and rule changes.

**Tech Stack:** .NET 8, WPF/XAML, C#, xUnit, existing SharpPcap networking engine.

## Global Constraints

- Preserve the approved navy, slate, teal, blue, green, and red palette exactly as recorded in the design spec.
- Show only telemetry derived from `DeviceRegistry`, `PcapLanEngine`, and local UI state; do not invent packet loss or router data.
- Do not add router login/API dependencies or new network probes.
- Keep Stop and restore visually and spatially separated from normal actions.
- Retain the existing one-second refresh and traffic-ranked device ordering.
- Support a minimum window size of 1100 by 700 and a preferred size near 1440 by 900.
- Preserve all existing networking behavior and tests.

---

### Task 1: Numeric device telemetry and bounded traffic history

**Files:**
- Create: `src/Lantern.App/ViewModels/TrafficSample.cs`
- Create: `src/Lantern.App/ViewModels/TrafficHistory.cs`
- Modify: `src/Lantern.App/ViewModels/DeviceViewModel.cs`
- Create: `tests/Lantern.App.Tests/TrafficHistoryTests.cs`
- Modify: `tests/Lantern.App.Tests/DeviceViewModelTests.cs`

**Interfaces:**
- Produces: `TrafficSample(DateTimeOffset Timestamp, double DownloadBytesPerSecond, double UploadBytesPerSecond, string? TopDevice)`.
- Produces: `TrafficHistory(int capacity)`, `IReadOnlyList<TrafficSample> Samples`, `void Add(TrafficSample sample)`, and `void Clear()`.
- Produces: numeric `DownloadBytesPerSecond`, `UploadBytesPerSecond`, and `HasActiveRule` properties on `DeviceViewModel`.

- [ ] **Step 1: Write failing history tests**

```csharp
[Fact]
public void Add_KeepsOnlyTheNewestSamples()
{
    var history = new TrafficHistory(2);
    history.Add(new TrafficSample(DateTimeOffset.UnixEpoch, 10, 1, "A"));
    history.Add(new TrafficSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 20, 2, "B"));
    history.Add(new TrafficSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 30, 3, "C"));

    Assert.Equal(new[] { 20D, 30D }, history.Samples.Select(x => x.DownloadBytesPerSecond));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "FullyQualifiedName~TrafficHistoryTests"`

Expected: compilation fails because `TrafficHistory` and `TrafficSample` do not exist.

- [ ] **Step 3: Implement the bounded model and numeric properties**

Use a private `List<TrafficSample>` guarded by argument validation. `Add` removes the oldest item when count exceeds capacity. `DeviceViewModel.Update` assigns the numeric rates before formatting their display strings, and `HasActiveRule` returns `CanControl && (PauseInternet || DownloadLimit > 0 || UploadLimit > 0)` with change notifications from all dependent setters.

- [ ] **Step 4: Run the focused tests and full app tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Expected: all app tests pass.

- [ ] **Step 5: Review the diff without committing unrelated dirty files**

Run: `git diff --check -- src/Lantern.App/ViewModels tests/Lantern.App.Tests`

Expected: no whitespace errors.

---

### Task 2: Chart scaling and WPF live-traffic control

**Files:**
- Create: `src/Lantern.App/Controls/TrafficChartScale.cs`
- Create: `src/Lantern.App/Controls/LiveTrafficChart.cs`
- Create: `tests/Lantern.App.Tests/TrafficChartScaleTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<TrafficSample>` from Task 1.
- Produces: `TrafficChartScale.GetMaximum(IReadOnlyList<TrafficSample>)` and `TrafficChartScale.GetX(DateTimeOffset, DateTimeOffset, DateTimeOffset, double)` for deterministic layout tests.
- Produces: `LiveTrafficChart.Samples` dependency property and keyboard/mouse sample inspection.

- [ ] **Step 1: Write failing scaling tests**

```csharp
[Fact]
public void GetMaximum_AddsHeadroomAndNeverReturnsZero()
{
    var samples = new[] { new TrafficSample(DateTimeOffset.UnixEpoch, 1000, 250, null) };
    Assert.Equal(1200D, TrafficChartScale.GetMaximum(samples));
    Assert.Equal(1D, TrafficChartScale.GetMaximum(Array.Empty<TrafficSample>()));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "FullyQualifiedName~TrafficChartScaleTests"`

Expected: compilation fails because `TrafficChartScale` does not exist.

- [ ] **Step 3: Implement scale helpers and the chart control**

The chart derives from `FrameworkElement`, draws subtle horizontal grid lines, teal download and blue upload polylines, labels the current time range, and shows an explanatory empty state. `OnMouseMove` selects the closest timestamp; left/right keys move the selected sample; `ToolTip` contains local time, formatted aggregate rates, and top device. Rendering invalidates only when `Samples` changes or the control resizes.

- [ ] **Step 4: Run focused and full tests**

Run: `dotnet test LanternControl.slnx --no-restore`

Expected: all tests pass with no warnings.

- [ ] **Step 5: Review the focused diff**

Run: `git diff --check -- src/Lantern.App/Controls tests/Lantern.App.Tests/TrafficChartScaleTests.cs`

Expected: no whitespace errors.

---

### Task 3: Theme resources and reusable dashboard controls

**Files:**
- Modify: `src/Lantern.App/App.xaml`
- Create: `src/Lantern.App/Controls/ResponsiveUniformGrid.cs`
- Create: `tests/Lantern.App.Tests/ResponsiveUniformGridTests.cs`

**Interfaces:**
- Produces: semantic brushes `DownloadAccent`, `UploadAccent`, `Success`, and existing danger/surface tokens.
- Produces: keyed styles `NavButtonStyle`, `MetricCardStyle`, `PrimaryButtonStyle`, `DangerButtonStyle`, `SwitchCheckBoxStyle`, and compact dark input styles.
- Produces: `ResponsiveUniformGrid` with `Breakpoint` and `WideColumns` dependency properties, plus pure `GetColumnCount(double width, double breakpoint, int wideColumns)`; below the breakpoint it uses two columns.

- [ ] **Step 1: Write a failing responsive-grid test**

```csharp
[Fact]
public void WidthBelowBreakpoint_UsesTwoColumns()
{
    Assert.Equal(2, ResponsiveUniformGrid.GetColumnCount(800, 900, 4));
    Assert.Equal(4, ResponsiveUniformGrid.GetColumnCount(1000, 900, 4));
}
```

- [ ] **Step 2: Run the test and verify RED**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "FullyQualifiedName~ResponsiveUniformGridTests"`

Expected: compilation fails because `ResponsiveUniformGrid` does not exist.

- [ ] **Step 3: Implement semantic resources and controls**

Use 40-to-44-pixel button targets, visible focus borders, stable hover/pressed states, 12-pixel minimum metadata text, and a labelled toggle template. Avoid animation that changes layout bounds.

- [ ] **Step 4: Run the app tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Expected: all app tests pass.

- [ ] **Step 5: Validate XAML compilation**

Run: `dotnet build src/Lantern.App/Lantern.App.csproj -c Release --no-restore`

Expected: build succeeds with zero warnings and errors.

---

### Task 4: Compose the approved dashboard and connect real state

**Files:**
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml.cs`
- Create: `src/Lantern.App/ViewModels/DashboardSummary.cs`
- Create: `tests/Lantern.App.Tests/DashboardSummaryTests.cs`

**Interfaces:**
- Consumes: numeric device properties and `TrafficHistory` from Task 1.
- Consumes: `LiveTrafficChart` and `ResponsiveUniformGrid` from Tasks 2 and 3.
- Produces: `DashboardSummary.From(IEnumerable<DeviceViewModel>)` with connected client count, aggregate download/upload, active-rule count, and top-device name.

- [ ] **Step 1: Write failing summary tests**

```csharp
[Fact]
public void From_ExcludesProtectedRowsFromClientMetrics()
{
    var now = DateTimeOffset.UnixEpoch;
    var client = new DeviceViewModel(_ => Task.CompletedTask);
    client.Initialize(new DeviceSnapshot(
        PhysicalAddress.Parse("0E4F69CCE4F0"), IPAddress.Parse("192.168.31.213"),
        "POCO-F6", now, now, 1000, 200), null, false, "Online");
    var gateway = new DeviceViewModel(_ => Task.CompletedTask);
    gateway.Initialize(new DeviceSnapshot(
        PhysicalAddress.Parse("64644A380A15"), IPAddress.Parse("192.168.31.1"),
        "Gateway", now, now, 5000, 5000), null, true, "Gateway — protected");
    var summary = DashboardSummary.From(new[] { client, gateway });
    Assert.Equal(1, summary.ConnectedDevices);
    Assert.Equal(1000D, summary.DownloadBytesPerSecond);
    Assert.Equal(200D, summary.UploadBytesPerSecond);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "FullyQualifiedName~DashboardSummaryTests"`

Expected: compilation fails because `DashboardSummary` does not exist.

- [ ] **Step 3: Implement summary calculation and bind the screen**

`RefreshDevices` computes `DashboardSummary`, updates four metric labels, appends one `TrafficSample`, refreshes the chart, and updates uptime from a stored control-start timestamp. Navigation buttons call `BringIntoView` for the adapter strip, chart, and device table. The idle state shows Start control; the active state shows Refresh devices and Stop and restore. Existing handlers and enablement rules remain authoritative.

- [ ] **Step 4: Replace the old table layout with the approved hierarchy**

Build the fixed sidebar, header/status pill, adapter strip, responsive metric cards, live chart, and ranked device table. Bind all rows to real `Devices`; use stacked IP/MAC identity and suffix-labelled limit inputs. Preserve `EmptyState`, `DetailStatusText`, and existing control names used by code-behind.

- [ ] **Step 5: Run summary tests and the full suite**

Run: `dotnet test LanternControl.slnx --no-restore`

Expected: all tests pass.

- [ ] **Step 6: Build Release and check the diff**

Run: `dotnet build src/Lantern.App/Lantern.App.csproj -c Release --no-restore`

Run: `git diff --check`

Expected: Release build succeeds and diff check reports no errors.

---

### Task 5: Visual, interaction, and package verification

**Files:**
- Modify if required by verified defects: `src/Lantern.App/App.xaml`
- Modify if required by verified defects: `src/Lantern.App/MainWindow.xaml`
- Modify if required by verified defects: `src/Lantern.App/MainWindow.xaml.cs`
- Modify: `src/Lantern.App/Lantern.App.csproj`
- Create output: `publish/v0.2.9/LANtern Control.exe`
- Create output: `C:/Users/moham/Documents/Codex/2026-07-28/can/outputs/LANtern-Control-v0.2.9.exe`

**Interfaces:**
- Consumes: the completed dashboard from Tasks 1 through 4.
- Produces: a self-contained Windows x64 executable with product version `0.2.9`.

- [ ] **Step 1: Run the full Release test suite**

Run: `dotnet test LanternControl.slnx -c Release --no-restore`

Expected: zero failed tests.

- [ ] **Step 2: Launch and inspect the window**

Inspect at 1100 by 700, 1280 by 760, and 1440 by 900. Compare hierarchy, palette, spacing, chart, device table, focus states, disabled states, and action separation with `docs/superpowers/specs/assets/lantern-control-frontend-concept.png`.

- [ ] **Step 3: Exercise existing interactions**

Select the Ethernet adapter, start control, refresh devices, edit both limits, toggle Pause internet, and use Stop and restore. Confirm networking behavior and status feedback match the pre-redesign build.

- [ ] **Step 4: Bump and publish version 0.2.9**

Change `<Version>` in `src/Lantern.App/Lantern.App.csproj` to `0.2.9`, then run:

```powershell
dotnet publish src/Lantern.App/Lantern.App.csproj -c Release -o publish/v0.2.9 --no-restore
Copy-Item 'publish/v0.2.9/LANtern Control.exe' 'C:/Users/moham/Documents/Codex/2026-07-28/can/outputs/LANtern-Control-v0.2.9.exe'
```

- [ ] **Step 5: Verify the packaged artifact**

Run `Get-FileHash` and inspect `VersionInfo.ProductVersion` and `VersionInfo.FileVersion` for the copied executable.

Expected: version 0.2.9 and a non-empty SHA-256 hash.
