# Service Inspector Design

## Goal

Add a shared Windows and Linux Service Inspector that groups observable device traffic by recognizable service, shows live session statistics, and preserves daily usage history without decrypting user content or changing the current packet-forwarding behavior.

## Scope

The first release provides classification and accounting only. Per-service shaping is deliberately deferred until the classifications have been validated on real networks. Existing whole-device pause, limits, domain observation, and domain blocking must continue to behave exactly as they do today.

## Classification

`Lantern.Core` owns a `ServiceDefinitionCatalog` independent from the existing domain-block preset catalog. Definitions use normalized exact-domain and subdomain suffix patterns. The initial catalog contains YouTube, Discord, Instagram, Facebook, Messenger, Snapchat, TikTok, Netflix, Twitch, Spotify, Steam, Epic Games, Xbox, PlayStation, WhatsApp, and Telegram. Traffic that cannot be attributed safely is reported as `Other`; LANtern must not guess from a generic shared CDN alone.

The existing `FrameRouter` already parses transport flows and remembers DNS/TLS/HTTP observations. It will expose the attributed hostname and canonical client-side flow key on routed packets. A shared `ServiceInspectorTracker` consumes these routed-packet facts. A TLS or HTTP hostname binds its bidirectional transport flow to a service. A DNS observation can open or refresh a service session, but its bytes are not assigned to that service because the query is traffic to the DNS resolver rather than to the service itself.

Encrypted DNS, VPN traffic, ECH, QUIC without an observable hostname, and shared infrastructure can prevent classification. The UI must disclose this and leave such traffic under `Other` rather than implying access to encrypted content.

## Sessions and Metrics

A session is keyed by normalized device MAC plus service identifier. It begins when an observable matching hostname or attributed flow appears. Packets on attributed flows update:

- current download and upload bytes per second;
- downloaded and uploaded bytes during this session;
- active transport-flow count;
- first-seen timestamp;
- last-activity timestamp;
- active duration.

A flow and its service session become idle after 60 seconds without matching traffic. Reusing that service after the idle threshold creates a new session. Live rates are calculated from monotonic byte-counter deltas at the existing 2.5-second dashboard sampling interval, while totals retain every attributed byte.

## Persistence

Completed and interrupted sessions are written to a separate versioned JSON history file in the existing per-user LANtern data directory. History is grouped by local calendar day, device MAC, and service. It stores aggregate downloaded/uploaded bytes, total active duration, session count, and last activity. Writes are atomic and bounded to the most recent 30 days so normal updates preserve the data without allowing unbounded growth.

Current sessions remain in memory. On a clean stop or application close they are checkpointed into daily history exactly once. Startup loads daily aggregates but begins fresh live sessions.

## User Interface

Both platforms add a `Service Inspector` item beneath `Visited domains` in the sidebar. The page follows the existing carbon-crimson theme and preserves the platform's established outer scrollbar behavior.

Devices appear as collapsed rows by default. Expanding a device shows one row per observed service with:

- service name and activity state;
- current download and upload speed;
- session download and upload totals;
- active duration;
- active connection count;
- first seen and last activity;
- today's aggregate data.

Expanded/collapsed device state is preserved while navigating between pages during the same app run. The page includes an honest empty state and a note that only hostname metadata is classified. Windows and Linux use the same catalog, tracker, snapshot types, history schema, formatting rules, and test fixtures; their WPF and Avalonia views remain separate thin renderers.

## Reliability and Concurrency

Packet processing must not block on UI dispatch or file I/O. The tracker uses short synchronized in-memory updates, and the UI reads immutable snapshots. History writes occur off the forwarding thread. Tracker failures must never stop forwarding. Service Inspector adds observation only and does not alter `TrafficPolicy.ShouldForward`, Ethernet rewriting, poisoning cadence, or capture filters.

## Testing

Core tests cover domain matching, exact/subdomain behavior, bidirectional flow attribution, rate deltas, concurrent services, 60-second session rollover, `Other` accounting, and daily-history merging/retention. Windows and Linux presentation tests cover sidebar navigation, empty states, device grouping, row formatting, and platform parity. Existing full test suites and release builds must remain green.

