# Local Xiaomi Router Gateway Design

## Goal

Build a fast, localhost-only dashboard for the Xiaomi Mi Router 4A (R4AC,
firmware 3.0.12) at `192.168.31.1`. The dashboard replaces the slow legacy
workflow for checking devices, applying QoS limits, and maintaining manual
port-forwarding rules.

Success means the user can start the gateway with one click, enter the router
admin password, and safely perform the supported tasks without opening the
Xiaomi interface.

## Scope

Version 1 includes:

- Router reachability, internet state, uptime, traffic, and Wi-Fi status.
- Connected-device inventory using router-provided names, IP addresses, and
  MAC addresses.
- Always-on per-device QoS configuration for upload/download caps and priority,
  limited to capabilities reported by this firmware.
- Manual port-forwarding rules: list, create, edit, enable/disable, and delete;
  support TCP, UDP, and combined protocols where the firmware supports them.
- Read-only Wi-Fi status and confirmed router reboot.
- Clear unsupported-feature and router-session-expired states.

Version 1 excludes remote/LAN access, QoS schedules, UPnP mapping management,
firmware upgrades, factory reset, WAN/LAN addressing changes, and cloud access.

## Architecture

A single TypeScript Node.js process binds only to `127.0.0.1` and serves both a
compiled web dashboard and a local JSON API. A dedicated Xiaomi router adapter
implements the firmware's nonce/password hashing login, holds the `stok` session
token in memory, and translates between stable local models and MiWiFi/LuCI
responses.

The browser never calls `192.168.31.1` directly. It receives an `HttpOnly`,
same-site local session cookie and calls the local API. The router admin
password and Xiaomi token are never returned to the browser, logged, or saved.
Stopping the process clears them.

The one-click Windows launcher starts the service, waits for its health endpoint,
and opens the dashboard in the default browser. A second launch focuses/opens
the existing service instead of starting a duplicate.

## Components and Data Flow

The dashboard contains:

- Login/connection screen with router reachability feedback.
- Overview for health, traffic, Wi-Fi, and recent refresh state.
- Devices table with search, recognizable device labels, online state, and QoS
  summaries.
- QoS editor for upload/download limits and supported priority values.
- Port-forwarding table and validated create/edit form.
- Settings area for read-only Wi-Fi details and reboot.

The local API uses stable resources:

- `/api/session` for login, logout, and session state.
- `/api/status` and `/api/devices` for read-only router data.
- `/api/qos` and `/api/qos/:mac` for supported QoS operations.
- `/api/port-forwards` and `/api/port-forwards/:id` for manual rules.
- `/api/wifi` for read-only details and `/api/router/reboot` for reboot.

Before each mutation, the backend validates input and refreshes the relevant
router state. After a successful mutation it reads the state back from the
router and returns the confirmed result. The dashboard never claims success
based only on the outgoing request.

## Safety and Failure Handling

- Bind exclusively to `127.0.0.1`; reject non-loopback hosts and unexpected
  origins.
- Use an `HttpOnly`, `SameSite=Strict` session cookie and require a per-session
  CSRF token for mutations.
- Redact passwords, Xiaomi tokens, device identifiers, and request bodies from
  logs.
- Rate-limit login attempts locally and pass through Xiaomi lockout feedback
  without automatically retrying bad credentials.
- Preserve all existing rules on startup; make no changes until the user
  explicitly submits an action.
- Confirm deletions, reboot, Wi-Fi changes, and edits that conflict with another
  forwarding rule.
- Treat router timeouts, expired tokens, unsupported endpoints, and
  verification mismatches as distinct actionable errors.
- Disable mutation controls while the router is unreachable or the session is
  invalid.

## Validation Rules

Port-forwarding input requires a non-empty label, valid internal IPv4 address,
valid ports in `1..65535`, and a supported protocol. The service rejects
duplicate or overlapping external-port/protocol combinations before calling the
router. Device targets come from the current router device list unless the user
explicitly enters a valid LAN address.

QoS limits must use the firmware's supported units and bounds. The UI displays
the normalized value read back from the router. Priority choices are populated
from capabilities instead of inventing unsupported levels.

## Testing and Acceptance

Automated tests cover the Xiaomi login hash/nonce generation, response
normalization, input validation, overlap detection, secret redaction, session
expiry, timeout handling, and all local API authorization checks. Router calls
are exercised through fixtures and a fake adapter so tests never modify the
real router.

Browser tests cover login errors, status refresh, device search, QoS editing,
forward-rule CRUD, confirmation dialogs, unreachable-router behavior, and
responsive layout.

Before release, a live smoke test against `192.168.31.1` verifies:

1. Login succeeds without persisting credentials.
2. Existing QoS and forwarding rules load without modification.
3. A temporary QoS change is read back and then restored.
4. A temporary forwarding rule is created, verified, disabled, re-enabled,
   deleted, and confirmed absent.
5. Logout and process shutdown invalidate all local and router session state.

Any live mutation requires the user's explicit interaction in the dashboard.
