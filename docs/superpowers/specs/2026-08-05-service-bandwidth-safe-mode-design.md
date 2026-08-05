# Service Bandwidth Limits and Safe Mode Design

## Goal

Add per-device, per-service download and upload limits while preserving the
existing device-wide limit as the hard aggregate ceiling. Add a global Safe
Mode setting that discovers every device but forwards traffic only for devices
with active enforcement rules.

## Current Architecture

LANtern discovers IPv4 devices through ARP and controls traffic by sending ARP
interception frames that place the controller between a client and its gateway.
`FrameRouter` rewrites and forwards intercepted Ethernet frames. `TrafficPolicy`
currently owns device-wide limits, pause state, and domain blocks. The Service
Inspector already classifies bidirectional flows from DNS answers, TLS server
names, HTTP host headers, and remembered flow context.

Windows enforces current device limits in the routing path. Linux defers rate
enforcement to `LinuxFramePacer` so limited TCP traffic can be paced instead of
immediately discarded. The new design retains these platform-specific sending
strategies while sharing rule storage, service attribution, and enforcement
semantics in the core.

## Bandwidth Semantics

Service rules belong to one device and one catalog service. Every rule has
independent download and upload limits in `KB/s`; `0` means unlimited.

The device-wide limit is the hard aggregate ceiling. A service limit is a child
ceiling within the device ceiling, not an additional pool. For a device limited
to `2000 KB/s` download with YouTube limited to `1000 KB/s` download:

- Total device download never exceeds `2000 KB/s`.
- YouTube download never exceeds `1000 KB/s`.
- While YouTube consumes `1000 KB/s`, all other services can share at most the
  remaining `1000 KB/s`.
- While YouTube is idle, other services can use the full `2000 KB/s`.
- If all other services are idle, YouTube remains capped at `1000 KB/s` and the
  unused device capacity is not reassigned to YouTube.

Upload uses the same independent hierarchy. A packet belonging to a configured
service must satisfy both its child service ceiling and the parent device
ceiling. Recognized services without a configured child limit, plus all
unrecognized traffic, use only the parent device ceiling.

Pause and domain blocking take precedence over bandwidth scheduling. Dropped or
blocked traffic is not queued for later forwarding.

## Service Attribution and Enforcement

`TrafficPolicy` stores:

- The existing device-wide `TrafficRule` keyed by normalized device MAC.
- Service traffic rules keyed by normalized device MAC and stable service ID.
- Existing per-device blocked-domain rules.

`FrameRouter` continues to bind observed domains to canonical bidirectional flow
keys. It additionally exposes the stable matched service ID on routed frames so
the forwarding layer can apply the correct child limiter without duplicating
domain matching.

Classification is best effort. DNS, TLS hostname, HTTP host, and remembered
DNS/flow attribution can identify a service. VPNs, proxies, Encrypted Client
Hello, direct-IP traffic, and flows without observable attribution remain
`Other`. Such traffic still obeys the device-wide ceiling but does not consume a
named service's child allowance.

Changing a device or service rule while control is running resets the affected
scheduling state immediately. Queue memory remains bounded. When a queue cannot
accept more traffic, LANtern drops the excess instead of growing memory without
limit.

## Safe Mode

Safe Mode is one persisted global setting. It does not create a separate screen
or operating workflow.

When Safe Mode is disabled, LANtern preserves current intercept-all behavior so
all controllable devices provide live bandwidth, domain, and Service Inspector
data.

When Safe Mode is enabled, LANtern intercepts a device only when at least one of
these enforceable rules is active:

- Device download limit above `0`.
- Device upload limit above `0`.
- Service download or upload limit above `0` for any service.
- Internet pause enabled.
- At least one blocked domain.

A device with none of those rules is still discovered through ordinary ARP
requests, replies, and neighbor-cache refreshes. LANtern does not poison its ARP
mappings, and its traffic travels directly between the device and router. The
device remains visible in LANtern, but live bandwidth, visited domains, and
Service Inspector traffic are unavailable while it bypasses the controller.

Changing Safe Mode while control is active applies immediately to every known
device. LANtern sends corrective ARP mappings for devices becoming exempt and
begins interception for devices becoming governed. Devices that remain governed
stay intercepted. Periodic discovery continues independently of interception.

Windows and Linux must use the same eligibility rules and both must restore
removed interception targets, not merely stop refreshing poison frames.

## Service Inspector UI

Service limits live in Service Inspector. Every controllable device appears in
the inspector even when it has no observed service sessions, allowing rules to
be configured before Safe Mode interception begins.

Expanding a device shows the complete preset service catalog. Each service row
contains separate download and upload limit editors in `KB/s`, where `0` means
unlimited. Existing live/session/today usage and active-state information remain
visible. Services with configured limits are visually distinguishable and sort
above untouched inactive services. Active observed services continue to receive
appropriate prominence without hiding configured rules.

Setting both directions to `0` removes the persisted service rule. Rule changes
apply to a running engine immediately.

## Settings and Wi-Fi Recommendation

Settings persist:

- `SafeModeEnabled`, defaulting to `false` when absent for backward
  compatibility.
- `SuppressWifiSafeModePrompt`, defaulting to `false` when absent.
- Service limits keyed by normalized device MAC and stable service ID.

The Settings UI contains a global Safe Mode switch and explains that
unrestricted devices remain discoverable but bypass live traffic inspection.

At launch, LANtern determines whether the selected active adapter is Wi-Fi or
Ethernet. Ethernet never shows a Safe Mode recommendation. When the selected
adapter is Wi-Fi, Safe Mode is off, and prompt suppression is off, LANtern shows
one themed recommendation during that application launch. A later launch on
Wi-Fi shows it again unless suppression was selected.

The Wi-Fi popup contains:

- A concise explanation that Safe Mode avoids forwarding unrestricted devices
  through the controller's wireless connection.
- `Turn on Safe Mode`, which enables and persists Safe Mode immediately.
- `Ignore`, which leaves Safe Mode unchanged.
- `Don't ask again`, which may accompany either action and permanently sets
  prompt suppression.

If Safe Mode is already enabled, the recommendation is unnecessary and does not
appear. Switching from an Ethernet launch to a later Wi-Fi launch triggers the
recommendation under the same conditions.

## Failure Handling

Service accounting remains non-critical telemetry and must not interrupt packet
forwarding. Invalid or stale service IDs are ignored or normalized against the
catalog without crashing control startup. Existing settings files load without
migration failures.

Runtime Safe Mode transitions use the existing corrective ARP mechanisms.
Failures flow through the engine's existing status and error reporting rather
than silently reporting a successful transition. The application continues to
advise stopping and restoring control before network changes or shutdown.

## Testing

Core tests cover:

- Parent device ceilings combined with child service ceilings.
- Dynamic use of remaining parent bandwidth by other services.
- Independent download and upload behavior.
- Recognized services without child rules and unrecognized traffic.
- Pause and domain-block precedence.
- Service-rule normalization, removal, and persistence.
- Safe Mode eligibility for every supported rule type.
- Backward-compatible settings defaults.

Routing and platform tests cover:

- Stable bidirectional service IDs on attributed flows.
- Immediate Windows and Linux interception/restore transitions.
- Scheduling-state reset after live rule changes.
- Bounded queues and overflow behavior.
- Wi-Fi versus Ethernet recommendation policy.
- Recommendation suppression and once-per-launch behavior.
- Service Inspector catalog visibility, ordering, editing, and immediate rule
  application.

Existing traffic-control, domain-blocking, service-attribution, settings, and
platform regression suites must remain green.

## Out of Scope

- Decrypting HTTPS or bypassing VPN, proxy, ECH, or encrypted-DNS privacy.
- IPv6 traffic control.
- Router-specific QoS configuration.
- Per-URL, search, message, or page-content classification.
- A separate Safe Mode screen or operating mode.
