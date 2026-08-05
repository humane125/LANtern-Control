# Service Bandwidth Limits and Safe Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add hierarchical per-service bandwidth limits and an immediately applied Safe Mode that intercepts only devices with enforceable rules, plus cross-platform configuration and Wi-Fi guidance.

**Architecture:** Extend the shared policy with persisted service rules, service-aware forwarding decisions, and centralized interception eligibility. Preserve Windows packet enforcement and extend Linux pacing with service identity while enforcing the device bucket as the parent ceiling. Drive both desktop UIs from the same service catalog and prompt policy.

**Tech Stack:** C# 12, .NET 8, WPF, Avalonia, SharpPcap, xUnit

## Global Constraints

- Device-wide download/upload limits remain hard aggregate ceilings.
- Service limits are child ceilings and never add bandwidth above the device ceiling.
- `0` means unlimited for every limit field.
- Safe Mode discovers unrestricted devices without intercepting their traffic.
- Any device limit, service limit, pause, or blocked domain requires interception.
- Safe Mode changes and traffic-rule changes apply immediately.
- Existing settings files load with Safe Mode disabled and no service rules.
- Service classification remains best effort and does not decrypt traffic.

---

### Task 1: Persisted service rules and centralized interception eligibility

**Files:**
- Create: `src/Lantern.Core/Control/ServiceTrafficRule.cs`
- Modify: `src/Lantern.Core/Control/TrafficPolicy.cs`
- Modify: `src/Lantern.Core/Settings/AppSettings.cs`
- Modify: `src/Lantern.Core/Settings/SettingsStore.cs`
- Test: `tests/Lantern.Core.Tests/TrafficControlTests.cs`
- Test: `tests/Lantern.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `ServiceTrafficRule(int DownloadKiloBytesPerSecond, int UploadKiloBytesPerSecond)`.
- Produces: `TrafficPolicy.SafeModeEnabled`, `SetSafeMode(bool)`, `SetServiceRule(string,string,ServiceTrafficRule)`, `GetServiceRule(string,string)`, and `GetServiceRules(string)`.
- Produces: `AppSettings.ServiceLimits[mac][serviceId]` plus `SafeModeEnabled` and `SuppressWifiSafeModePrompt`.

- [ ] **Step 1: Write failing policy and settings tests**

```csharp
[Fact]
public void TrafficPolicy_SafeModeInterceptsOnlyEnforcedDevices()
{
    var policy = new TrafficPolicy();
    policy.SetSafeMode(true);
    Assert.Equal(InterceptionTargets.None, policy.GetInterceptionTargets(Mac));
    policy.SetServiceRule(Mac, "youtube", new ServiceTrafficRule(1000, 0));
    Assert.Equal(InterceptionTargets.Client | InterceptionTargets.Gateway,
        policy.GetInterceptionTargets(Mac));
}

[Fact]
public async Task SaveAndLoad_RoundTripsSafeModeAndServiceLimits()
{
    var settings = new AppSettings { SafeModeEnabled = true };
    settings.ServiceLimits[Mac] = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = new ServiceTrafficRule(1000, 0),
    };
    // save, load, and assert normalized MAC/service/rates
}
```

- [ ] **Step 2: Run tests and verify the new APIs are missing**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter "TrafficPolicy_SafeMode|SaveAndLoad_RoundTripsSafeMode"`

Expected: build failure for missing Safe Mode and service-rule members.

- [ ] **Step 3: Implement normalized rule storage and settings normalization**

```csharp
public sealed record ServiceTrafficRule(int DownloadKiloBytesPerSecond, int UploadKiloBytesPerSecond)
{
    public ServiceTrafficRule Normalize() => new(
        Math.Max(0, DownloadKiloBytesPerSecond),
        Math.Max(0, UploadKiloBytesPerSecond));
    public bool IsUnlimited => DownloadKiloBytesPerSecond == 0 && UploadKiloBytesPerSecond == 0;
}
```

Store only catalog service IDs and remove a service entry when both directions normalize to zero. `GetInterceptionTargets` returns two-way interception for all devices when Safe Mode is off; in Safe Mode it returns two-way interception only for a device limit, service limit, pause, or blocked domain.

- [ ] **Step 4: Run focused and full core tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.Core tests/Lantern.Core.Tests
git commit -m "feat: persist service limits and safe mode policy"
```

### Task 2: Service-aware hierarchical forwarding decisions

**Files:**
- Modify: `src/Lantern.Core/Control/TokenBucket.cs`
- Modify: `src/Lantern.Core/Control/TrafficPolicy.cs`
- Modify: `src/Lantern.Core/Networking/FrameRouter.cs`
- Test: `tests/Lantern.Core.Tests/TrafficControlTests.cs`
- Test: `tests/Lantern.Core.Tests/FrameRouterTests.cs`

**Interfaces:**
- Produces: `FrameRouteResult.ServiceId`.
- Produces: `TrafficPolicy.ShouldForward(mac, serviceId, direction, byteCount)` with one atomic parent/child decision.
- Consumes: service IDs from `ServiceDefinitionCatalog.MatchDomain`.

- [ ] **Step 1: Write failing hierarchical-limit and route-attribution tests**

```csharp
[Fact]
public void HierarchicalLimit_NeverExceedsServiceOrDeviceCeiling()
{
    var clock = new ManualClock();
    var policy = new TrafficPolicy(clock.Read);
    policy.SetRule(Mac, new TrafficRule(false, 2, 0));
    policy.SetServiceRule(Mac, "youtube", new ServiceTrafficRule(1, 0));
    Assert.True(policy.ShouldForward(Mac, "youtube", TrafficDirection.Download, 1000));
    Assert.True(policy.ShouldForward(Mac, "spotify", TrafficDirection.Download, 1000));
    Assert.False(policy.ShouldForward(Mac, "spotify", TrafficDirection.Download, 1000));
}
```

Add a routed TLS/DNS flow assertion that both directions expose `ServiceId == "youtube"`.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter "HierarchicalLimit|ServiceId"`

Expected: FAIL because service-aware forwarding is absent.

- [ ] **Step 3: Implement atomic parent/child consumption and route service IDs**

Refactor token buckets so a device limiter can refill, verify parent and child capacity, and commit both consumptions while holding one device-level lock. Unlimited buckets participate without consuming tokens. Resolve `ServiceId` after flow attribution and pass it into the forwarding decision.

- [ ] **Step 4: Run core tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.Core tests/Lantern.Core.Tests
git commit -m "feat: enforce hierarchical service bandwidth"
```

### Task 3: Linux service-aware pacing

**Files:**
- Modify: `src/Lantern.Linux/Services/LinuxForwardingStrategy.cs`
- Modify: `src/Lantern.Linux/Services/LinuxFramePacer.cs`
- Modify: `src/Lantern.Linux/Services/LinuxLanEngine.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxForwardingStrategyTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxFramePacerTests.cs`

**Interfaces:**
- Consumes: `FrameRouteResult.ServiceId` and parent/child service rules.
- Produces: `TryEnqueue(clientMac, serviceId, direction, frame)`.

- [ ] **Step 1: Write failing Linux pacing tests**

```csharp
[Fact]
public async Task ServiceLimit_IsPacedInsideDeviceLimit()
{
    policy.SetRule(Mac, new TrafficRule(false, 2, 0));
    policy.SetServiceRule(Mac, "youtube", new ServiceTrafficRule(1, 0));
    Assert.True(pacer.TryEnqueue(ClientMac, "youtube", TrafficDirection.Download, new byte[1000]));
    Assert.True(pacer.TryEnqueue(ClientMac, "other", TrafficDirection.Download, new byte[1000]));
    // assert the parent schedule never sends above 2 KB/s and YouTube never above 1 KB/s
}
```

- [ ] **Step 2: Run Linux focused tests and verify failure**

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter "ServiceLimit|RequiresPacing"`

Expected: build/test failure for missing service-aware pacing.

- [ ] **Step 3: Extend pacing keys and scheduling**

Keep one bounded per-device/direction parent scheduler and child eligibility per service. Unlimited unknown services use only the parent schedule. Reset all queues for a device after either device or service rule changes. Route immediate packets only when neither parent nor matched child requires pacing.

- [ ] **Step 4: Run Linux and core tests**

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.Linux tests/Lantern.Linux.Tests
git commit -m "feat: pace service traffic on linux"
```

### Task 4: Immediate cross-platform Safe Mode transitions

**Files:**
- Modify: `src/Lantern.App/Services/PcapLanEngine.cs`
- Modify: `src/Lantern.Linux/Services/LinuxLanEngine.cs`
- Test: `tests/Lantern.App.Tests/PcapLanEngineConfigurationTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxArpCacheTests.cs`
- Test: `tests/Lantern.Core.Tests/TrafficControlTests.cs`

**Interfaces:**
- Produces: `ApplySafeModeAsync(bool)` on both engines.
- Produces: `ApplyServiceRuleAsync(mac, serviceId, rule)` on both engines.

- [ ] **Step 1: Add failing transition tests around `InterceptionTransition` and engine configuration**

Assert that enabling Safe Mode restores both peers for an unrestricted device, preserves interception for ruled devices, and that adding the first service rule poisons both peers.

- [ ] **Step 2: Run focused platform tests and verify failure**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "SafeMode|ServiceRule"`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter "SafeMode|ServiceRule"`

Expected: FAIL for missing engine transition APIs.

- [ ] **Step 3: Implement engine-wide transitions**

Capture previous targets for all known clients, update policy state, then call the existing restore/poison helpers with `InterceptionTransition.Between(previous,current)`. Add the missing Linux corrective transition equivalent to Windows. Ensure reactive ARP responses also consult current targets.

- [ ] **Step 4: Run app and Linux tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.App/Services src/Lantern.Linux/Services tests
git commit -m "feat: apply safe mode transitions immediately"
```

### Task 5: Shared adapter and Wi-Fi prompt policy

**Files:**
- Modify: `src/Lantern.Core/Networking/AdapterProfile.cs`
- Create: `src/Lantern.Core/Networking/AdapterConnectionKind.cs`
- Create: `src/Lantern.Core/Settings/WifiSafeModePromptPolicy.cs`
- Modify: `src/Lantern.App/Services/WindowsAdapterService.cs`
- Modify: `src/Lantern.Linux/Services/LinuxAdapterService.cs`
- Test: `tests/Lantern.Core.Tests/NetworkingTests.cs`
- Test: `tests/Lantern.App.Tests/WindowsAdapterServiceTests.cs` if a new injectable mapper is needed
- Test: `tests/Lantern.Linux.Tests/LinuxAdapterServiceTests.cs`

**Interfaces:**
- Produces: `AdapterProfile.ConnectionKind` with `Ethernet`, `Wifi`, or `Unknown`.
- Produces: `WifiSafeModePromptPolicy.ShouldPrompt(kind, safeModeEnabled, suppressed, shownThisLaunch)`.

- [ ] **Step 1: Write failing adapter-kind and prompt-policy tests**

```csharp
[Theory]
[InlineData(AdapterConnectionKind.Wifi, false, false, false, true)]
[InlineData(AdapterConnectionKind.Ethernet, false, false, false, false)]
[InlineData(AdapterConnectionKind.Wifi, true, false, false, false)]
[InlineData(AdapterConnectionKind.Wifi, false, true, false, false)]
public void PromptPolicy_MatchesWifiOnlyRequirement(
    AdapterConnectionKind kind,
    bool safeModeEnabled,
    bool suppressed,
    bool shownThisLaunch,
    bool expected)
{
    Assert.Equal(expected, WifiSafeModePromptPolicy.ShouldPrompt(
        kind, safeModeEnabled, suppressed, shownThisLaunch));
}
```

- [ ] **Step 2: Run focused tests and verify missing types**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj --filter "PromptPolicy"`

Expected: build failure.

- [ ] **Step 3: Implement cross-platform adapter classification**

Map Windows `NetworkInterfaceType.Wireless80211` to Wi-Fi. Carry interface type in the Linux snapshot and map wireless interfaces to Wi-Fi, using the runtime network-interface type and a conservative `Unknown` fallback. Unknown never triggers the popup.

- [ ] **Step 4: Run adapter tests**

Run: `dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter "LinuxAdapterService"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.Core src/Lantern.App/Services/WindowsAdapterService.cs src/Lantern.Linux/Services/LinuxAdapterService.cs tests
git commit -m "feat: classify adapters for wifi safe mode guidance"
```

### Task 6: Service Inspector rule presentation and editing

**Files:**
- Modify: `src/Lantern.App/ViewModels/ServiceSessionViewModel.cs`
- Modify: `src/Lantern.App/ViewModels/DeviceServiceGroupViewModel.cs`
- Modify: `src/Lantern.App/ViewModels/ServiceInspectorPresentationBuilder.cs`
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml.cs`
- Modify: `src/Lantern.Linux/MainWindow.axaml`
- Modify: `src/Lantern.Linux/MainWindow.axaml.cs`
- Test: `tests/Lantern.App.Tests/ServiceInspectorPresentationTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxMainWindowTests.cs`

**Interfaces:**
- Produces mutable per-service limit properties and a change callback carrying `(mac, serviceId, ServiceTrafficRule)`.
- Consumes full `ServiceDefinitionCatalog.All`, current sessions/history, current devices, and persisted rules.

- [ ] **Step 1: Write failing presentation tests**

Assert that a device with no sessions still appears, all catalog services appear, configured services sort above untouched inactive services, limits initialize from settings, and edits emit the normalized service rule.

- [ ] **Step 2: Run focused UI model tests and verify failure**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "ServiceInspectorPresentation"`

Expected: FAIL because inactive catalog services and limit fields are absent.

- [ ] **Step 3: Implement view models and Windows/Avalonia templates**

Extend the builder inputs with all live/remembered identities and saved service rules. Render `Download limit` and `Upload limit` editors in each service row using the existing non-negative numeric patterns and `KB/s` units. Debounce or normalize edits through the existing settings-save paths, update policy/engine immediately, and preserve expansion state.

- [ ] **Step 4: Run app and Linux UI tests**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Lantern.App src/Lantern.Linux tests
git commit -m "feat: edit service limits in service inspector"
```

### Task 7: Safe Mode controls and themed Wi-Fi recommendation

**Files:**
- Create: `src/Lantern.App/SafeModePromptWindow.xaml`
- Create: `src/Lantern.App/SafeModePromptWindow.xaml.cs`
- Create: `src/Lantern.Linux/SafeModePromptWindow.axaml`
- Create: `src/Lantern.Linux/SafeModePromptWindow.axaml.cs`
- Modify: `src/Lantern.App/MainWindow.xaml`
- Modify: `src/Lantern.App/MainWindow.xaml.cs`
- Modify: `src/Lantern.Linux/MainWindow.axaml`
- Modify: `src/Lantern.Linux/MainWindow.axaml.cs`
- Test: `tests/Lantern.App.Tests/ForwarderStartupPolicyTests.cs`
- Test: `tests/Lantern.Linux.Tests/LinuxMainWindowTests.cs`

**Interfaces:**
- Produces a global Safe Mode toggle bound to persisted state.
- Produces prompt result `{ EnableSafeMode, SuppressFuturePrompts }`.

- [ ] **Step 1: Add failing launch/prompt and immediate-toggle tests**

Assert Wi-Fi-only display, once-per-launch display, suppression persistence, no prompt when already enabled, and that a toggle invokes `ApplySafeModeAsync` before saving.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/Lantern.App.Tests/Lantern.App.Tests.csproj --filter "SafeMode"`

Run: `dotnet test tests/Lantern.Linux.Tests/Lantern.Linux.Tests.csproj --filter "SafeMode"`

Expected: FAIL because UI and prompt do not exist.

- [ ] **Step 3: Implement themed controls and prompt flow**

Use existing application brushes, typography, rounded cards, and switch styles. On Wi-Fi launch, show the recommendation only when the shared prompt policy returns true. Persist `Don't ask again` for either button. Apply Safe Mode immediately when enabled from the prompt or settings.

- [ ] **Step 4: Run all automated tests**

Run: `dotnet test LanternControl.slnx`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat: add safe mode controls and wifi guidance"
```

### Task 8: Documentation and release verification

**Files:**
- Modify: `README.md`

**Interfaces:**
- Documents exact Safe Mode visibility limitations and hierarchical service-limit semantics.

- [ ] **Step 1: Update README behavior and usage**

Document per-service rules, the hard device ceiling example, Wi-Fi recommendation, ARP-only discovery for bypassed devices, and the loss of live traffic details for unrestricted Safe Mode devices.

- [ ] **Step 2: Run formatting, build, and test verification**

Run: `dotnet test LanternControl.slnx`

Run: `dotnet build LanternControl.slnx -c Release`

Run: `git diff --check`

Expected: all commands succeed with no new warnings or whitespace errors.

- [ ] **Step 3: Review the final diff for scope and secrets**

Run: `git status --short`

Run: `git diff --stat HEAD~7..HEAD`

Confirm only service-limit, Safe Mode, prompt, tests, and documentation files changed.

- [ ] **Step 4: Commit**

```powershell
git add README.md
git commit -m "docs: explain service limits and safe mode"
```
