# Service Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add shared Windows/Linux service classification, live session statistics, and persistent 30-day daily usage history without changing LANtern's forwarding behavior.

**Architecture:** `Lantern.Core` will classify normalized observed hostnames and account attributed bidirectional flows in a thread-safe tracker. Windows WPF and Linux Avalonia will pass the same routed packet facts into that tracker, read immutable snapshots on their existing 2.5-second refresh, and render platform-specific views backed by shared presentation models. A separate atomic JSON store persists bounded daily aggregates.

**Tech Stack:** C# 12, .NET 8, WPF, Avalonia 11, SharpPcap, xUnit, System.Text.Json

## Global Constraints

- The first release provides classification and accounting only; it must not add per-service shaping.
- Existing whole-device limits, pause, domain blocking, forwarding, ARP behavior, and capture filters must not change.
- Sessions roll over after exactly 60 seconds without matching activity.
- Live UI sampling remains exactly 2.5 seconds.
- Persist no more than the most recent 30 local calendar days.
- Never claim to decrypt HTTPS content; VPN, encrypted DNS, ECH, and unobservable QUIC traffic remain `Other`.
- Windows and Linux must share classification, session, history, and presentation logic.

---

### Task 1: Shared service definition catalog

**Files:**
- Create: `src/Lantern.Core/Services/ServiceDefinition.cs`
- Create: `src/Lantern.Core/Services/ServiceDefinitionCatalog.cs`
- Test: `tests/Lantern.Core.Tests/ServiceDefinitionCatalogTests.cs`

**Interfaces:**
- Produces: `ServiceDefinition(string Id, string Name, IReadOnlyList<string> Domains)`
- Produces: `ServiceDefinitionCatalog.All`, `ServiceDefinitionCatalog.Other`, and `ServiceDefinitionCatalog.MatchDomain(string?)`

- [ ] **Step 1: Write failing catalog tests**

```csharp
[Theory]
[InlineData("www.youtube.com", "youtube")]
[InlineData("discord.com", "discord")]
[InlineData("cdninstagram.com", "instagram")]
public void MatchDomain_MatchesExactAndSubdomains(string domain, string id) =>
    Assert.Equal(id, ServiceDefinitionCatalog.MatchDomain(domain).Id);

[Fact]
public void MatchDomain_DoesNotGuessUnknownSharedInfrastructure() =>
    Assert.Equal("other", ServiceDefinitionCatalog.MatchDomain("example.invalid").Id);
```

- [ ] **Step 2: Run the focused tests and verify missing types fail**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter ServiceDefinitionCatalogTests`

Expected: FAIL because `Lantern.Core.Services` does not exist.

- [ ] **Step 3: Implement normalized suffix matching and the approved built-ins**

```csharp
public sealed record ServiceDefinition(string Id, string Name, IReadOnlyList<string> Domains);

public static ServiceDefinition MatchDomain(string? domain)
{
    var normalized = TrafficPolicy.NormalizeDomain(domain ?? string.Empty);
    return All.FirstOrDefault(service => service.Domains.Any(candidate =>
        normalized.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
        normalized.EndsWith($".{candidate}", StringComparison.OrdinalIgnoreCase))) ?? Other;
}
```

- [ ] **Step 4: Run catalog and existing core tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit the catalog**

```powershell
git add src/Lantern.Core/Services tests/Lantern.Core.Tests/ServiceDefinitionCatalogTests.cs
git commit -m "Add service classification catalog"
```

### Task 2: Expose safe bidirectional flow attribution

**Files:**
- Modify: `src/Lantern.Core/Networking/FrameRouter.cs`
- Test: `tests/Lantern.Core.Tests/FrameRouterTests.cs`

**Interfaces:**
- Produces: `ServiceFlowKey(string ClientMac, ushort ClientPort, IPAddress RemoteAddress, ushort RemotePort, byte Protocol)`
- Extends: `FrameRouteResult` with `ServiceFlowKey? Flow` and `string? AttributedDomain`

- [ ] **Step 1: Write failing route-attribution tests**

```csharp
[Fact]
public void Route_ReusesTlsHostnameForReverseDownloadFlow()
{
    var upload = router.Route(tlsClientHelloFrame);
    var download = router.Route(reverseServerFrame);
    Assert.Equal("youtube.com", upload.AttributedDomain);
    Assert.Equal(upload.Flow, download.Flow);
    Assert.Equal("youtube.com", download.AttributedDomain);
}
```

- [ ] **Step 2: Run the focused tests and verify the properties are missing**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter Route_ReusesTlsHostnameForReverseDownloadFlow`

Expected: FAIL at compile time for missing `Flow` and `AttributedDomain`.

- [ ] **Step 3: Replace the private observed key with the public canonical key**

```csharp
public readonly record struct ServiceFlowKey(
    string ClientMac,
    ushort ClientPort,
    IPAddress RemoteAddress,
    ushort RemotePort,
    byte Protocol);
```

Return the canonical key and remembered hostname for both upload and download results. DNS observations retain `Observation` but do not bind the DNS resolver flow.

- [ ] **Step 4: Run all core networking tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS with unchanged forwarding/drop assertions.

- [ ] **Step 5: Commit flow attribution**

```powershell
git add src/Lantern.Core/Networking/FrameRouter.cs tests/Lantern.Core.Tests/FrameRouterTests.cs
git commit -m "Expose service flow attribution"
```

### Task 3: Thread-safe session accounting

**Files:**
- Create: `src/Lantern.Core/Services/ServiceSessionSnapshot.cs`
- Create: `src/Lantern.Core/Services/ServiceInspectorTracker.cs`
- Test: `tests/Lantern.Core.Tests/ServiceInspectorTrackerTests.cs`

**Interfaces:**
- Consumes: `FrameRouteResult.Flow`, `FrameRouteResult.AttributedDomain`, `FrameRouteResult.Direction`, `FrameRouteResult.MeteredByteCount`
- Produces: `Observe(FrameRouteResult result, DateTimeOffset observedAt)`
- Produces: `IReadOnlyList<ServiceSessionSnapshot> GetSnapshots(DateTimeOffset sampledAt)`
- Produces: `IReadOnlyList<CompletedServiceSession> DrainCompletedSessions(DateTimeOffset sampledAt)`
- Produces: `CompleteAll(DateTimeOffset stoppedAt)`

- [ ] **Step 1: Write failing tracker tests**

```csharp
[Fact]
public void Snapshot_AccountsBothDirectionsAndRateDelta()
{
    tracker.Observe(UploadResult("youtube.com", 1_000), start);
    tracker.Observe(DownloadResult("youtube.com", 4_000), start.AddSeconds(1));
    var snapshot = Assert.Single(tracker.GetSnapshots(start.AddSeconds(2.5)));
    Assert.Equal(1_000, snapshot.UploadBytes);
    Assert.Equal(4_000, snapshot.DownloadBytes);
    Assert.Equal(1, snapshot.ActiveConnections);
}

[Fact]
public void Observe_AfterSixtySeconds_CreatesCompletedAndNewSession()
{
    tracker.Observe(UploadResult("youtube.com", 1_000), start);
    tracker.GetSnapshots(start.AddSeconds(60));
    tracker.Observe(UploadResult("youtube.com", 500), start.AddSeconds(61));
    Assert.Single(tracker.DrainCompletedSessions(start.AddSeconds(61)));
    Assert.Equal(500, Assert.Single(tracker.GetSnapshots(start.AddSeconds(61))).UploadBytes);
}

[Fact]
public void ConcurrentServices_RemainSeparate()
{
    tracker.Observe(UploadResult("youtube.com", 1_000, clientPort: 50001), start);
    tracker.Observe(UploadResult("discord.com", 2_000, clientPort: 50002), start);
    var snapshots = tracker.GetSnapshots(start.AddSeconds(2.5));
    Assert.Equal(2, snapshots.Count);
    Assert.Contains(snapshots, item => item.ServiceId == "youtube" && item.UploadBytes == 1_000);
    Assert.Contains(snapshots, item => item.ServiceId == "discord" && item.UploadBytes == 2_000);
}
```

- [ ] **Step 2: Run focused tests and verify missing tracker fails**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter ServiceInspectorTrackerTests`

Expected: FAIL because `ServiceInspectorTracker` is undefined.

- [ ] **Step 3: Implement synchronized flow/session state and immutable snapshots**

```csharp
public void Observe(FrameRouteResult result, DateTimeOffset observedAt)
{
    if (result.ClientMac is null || result.Direction is null) return;
    lock (sync) { Expire(observedAt); BindAndAccount(result, observedAt); }
}
```

Use cumulative counters and per-snapshot baselines for rates. Count distinct non-expired flow keys. DNS observations touch a named session with zero attributed payload; unmatched attributed payload is placed in `Other`.

- [ ] **Step 4: Run tracker tests and the full core suite**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit session tracking**

```powershell
git add src/Lantern.Core/Services tests/Lantern.Core.Tests/ServiceInspectorTrackerTests.cs
git commit -m "Track per-service traffic sessions"
```

### Task 4: Atomic 30-day daily history

**Files:**
- Create: `src/Lantern.Core/Settings/ServiceUsageHistory.cs`
- Create: `src/Lantern.Core/Settings/ServiceUsageHistoryStore.cs`
- Test: `tests/Lantern.Core.Tests/ServiceUsageHistoryStoreTests.cs`

**Interfaces:**
- Consumes: `CompletedServiceSession`
- Produces: `LoadAsync`, `MergeAndSaveAsync(IEnumerable<CompletedServiceSession>)`, and `GetToday(mac, serviceId, localDate)`

- [ ] **Step 1: Write failing merge, reload, and retention tests**

```csharp
[Fact]
public async Task MergeAndSaveAsync_PersistsDailyTotalsAcrossInstances()
{
    await first.MergeAndSaveAsync([Completed("youtube", down: 1000, up: 200)]);
    await second.MergeAndSaveAsync([Completed("youtube", down: 500, up: 50)]);
    var today = Assert.Single((await second.LoadAsync()).Days);
    Assert.Equal(1500, today.Services.Single().DownloadBytes);
}
```

- [ ] **Step 2: Run tests and verify the store is missing**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter ServiceUsageHistoryStoreTests`

Expected: FAIL at compile time.

- [ ] **Step 3: Implement versioned JSON, atomic replacement, merge keys, and retention**

Use the same per-path semaphore and temporary-file replacement pattern as `SettingsStore`, but save `service-history.json` and `service-history.backup.json` independently.

- [ ] **Step 4: Run settings/history tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS, including concurrent save coverage.

- [ ] **Step 5: Commit persistence**

```powershell
git add src/Lantern.Core/Settings tests/Lantern.Core.Tests/ServiceUsageHistoryStoreTests.cs
git commit -m "Persist daily service usage history"
```

### Task 5: Feed both capture engines and shared presentation models

**Files:**
- Modify: `src/Lantern.App/Services/PcapLanEngine.cs`
- Modify: `src/Lantern.Linux/Services/LinuxLanEngine.cs`
- Create: `src/Lantern.App/ViewModels/ServiceSessionViewModel.cs`
- Create: `src/Lantern.App/ViewModels/DeviceServiceGroupViewModel.cs`
- Modify: `src/Lantern.Linux/Lantern.Linux.csproj`
- Test: `tests/Lantern.App.Tests/ServiceInspectorPresentationTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxServiceInspectorIntegrationTests.cs`

**Interfaces:**
- Engine constructors consume optional `ServiceInspectorTracker serviceInspectorTracker`
- Presentation builder consumes snapshots, device display names, and daily aggregates
- Produces shared `ObservableCollection<DeviceServiceGroupViewModel>`-compatible models

- [ ] **Step 1: Write failing engine and presentation tests**

```csharp
[Fact]
public void Build_GroupsSessionsByDeviceAndFormatsMetrics()
{
    var groups = ServiceInspectorPresentationBuilder.Build(snapshots, names, history);
    Assert.Equal("YouTube", groups.Single().Services.Single().ServiceName);
    Assert.Equal("4.0 KB", groups.Single().Services.Single().SessionDownloadText);
}
```

- [ ] **Step 2: Run the focused Windows and Linux tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter ServiceInspector`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter ServiceInspector`

Expected: FAIL because the models and engine wiring do not exist.

- [ ] **Step 3: Feed the tracker directly after `FrameRouter.Route`**

```csharp
var result = frameRouter.Route(bytes);
serviceInspectorTracker.Observe(result, DateTimeOffset.UtcNow);
```

Wrap observation so accounting failure cannot escape into forwarding. Link the shared presentation files into the Linux project in the established `Shared` item group.

- [ ] **Step 4: Run both platform test projects**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit engine and presentation integration**

```powershell
git add src/Lantern.App src/Lantern.Linux tests/Lantern.App.Tests tests/Lantern.Linux.Tests
git commit -m "Integrate service inspection on both platforms"
```

### Task 6: Windows and Linux Service Inspector pages

**Files:**
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml.cs`
- Modify: `src/Lantern.Linux/MainWindow.axaml`
- Modify: `src/Lantern.Linux/MainWindow.axaml.cs`
- Modify: `src/Lantern.Linux/MainWindow.Demo.cs`
- Test: `tests/Lantern.App.Tests/DeviceViewModelTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxUiParityTests.cs`

**Interfaces:**
- Consumes: shared service groups and history store
- Produces: `ServiceInspectorNavButton`, `ServiceInspectorSection`, `ServiceDeviceGroups`, and platform click handlers preserving expansion state

- [ ] **Step 1: Add failing structural and navigation tests**

```csharp
Assert.NotNull(window.FindName("ServiceInspectorNavButton"));
Assert.NotNull(window.FindName("ServiceInspectorSection"));
Assert.Contains("ItemsSource=\"{Binding ServiceDeviceGroups}\"", xaml);
```

Also assert that the sidebar order is Overview, Visited domains, Service Inspector, Domain rules and that the metadata limitation copy exists on both platforms.

- [ ] **Step 2: Run UI tests and verify missing controls fail**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "DeviceViewModelTests|ServiceInspector"`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter "UiParity|ServiceInspector"`

Expected: FAIL for missing named controls.

- [ ] **Step 3: Implement matching carbon-crimson pages and refresh lifecycle**

Instantiate one tracker and history store per app. On every existing dashboard refresh, build snapshots and refresh grouped rows. Drain completed sessions asynchronously. On stop/close call `CompleteAll`, persist once, then clear live rows. Keep group expansion state in a MAC-keyed dictionary and initialize new groups collapsed.

- [ ] **Step 4: Run all tests and release builds**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj`

Run: `dotnet build src/Lantern.App/Lantern.App.csproj -c Release`

Run: `dotnet build src/Lantern.Linux/Lantern.Linux.csproj -c Release`

Expected: all tests and both release builds PASS with zero warnings.

- [ ] **Step 5: Commit the complete UI slice**

```powershell
git add src/Lantern.App src/Lantern.Linux tests docs/superpowers
git commit -m "Add cross-platform Service Inspector"
```
