# Local Xiaomi Router Gateway Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a one-click, localhost-only Windows dashboard for Xiaomi R4AC status, connected devices, QoS, manual port forwarding, and reboot.

**Architecture:** A TypeScript Express service binds to `127.0.0.1:8787`, owns the MiWiFi session, exposes a validated local API, and serves a compiled React dashboard. A router adapter normalizes firmware-specific responses and verifies all mutations by reading the router state back.

**Tech Stack:** Node.js 22, TypeScript, Express 5, React 19, Vite, Zod, Vitest, Supertest, Playwright, Lucide icons

## Global Constraints

- Bind only to `127.0.0.1`; reject unexpected Host and Origin values.
- Keep the router password and `stok` token only in process memory.
- Never log credentials, tokens, request bodies, or full device identifiers.
- Preserve router state until the user explicitly submits an action.
- Version 1 has no LAN/remote access, schedules, UPnP management, firmware update, factory reset, or WAN/LAN addressing changes.
- Treat unsupported simultaneous QoS priority and speed-limit behavior as a visible firmware capability, never as a fabricated success.

---

### Task 1: Project foundation and shared contracts

**Files:**
- Create: `package.json`, `tsconfig.json`, `tsconfig.server.json`, `vite.config.ts`
- Create: `src/shared/models.ts`, `src/shared/schemas.ts`
- Test: `tests/shared/schemas.test.ts`

**Interfaces:**
- Produces: `LoginInput`, `RouterStatus`, `Device`, `QosState`, `QosUpdate`, `PortForward`, `PortForwardInput`, `ApiError`, and Zod request schemas.

- [ ] Write failing tests for MAC normalization, IPv4 validation, port bounds, protocol values, Mbps/KBps QoS values, and overlapping external port/protocol rules.
- [ ] Run `npm test -- tests/shared/schemas.test.ts` and confirm missing-module failure.
- [ ] Add the Node/React toolchain, scripts for `dev`, `build`, `test`, `test:e2e`, `start`, and `package`, plus strict TypeScript configuration.
- [ ] Implement shared types and Zod schemas. Represent protocol as `tcp | udp | both`, priority as `low | normal | high | unsupported`, and unlimited speed as `0`.
- [ ] Run the shared tests and `npm run typecheck`.
- [ ] Commit with `feat: scaffold router gateway contracts`.

### Task 2: MiWiFi authentication and read-only adapter

**Files:**
- Create: `src/server/router/errors.ts`
- Create: `src/server/router/login.ts`
- Create: `src/server/router/normalize.ts`
- Create: `src/server/router/xiaomi-router.ts`
- Test: `tests/server/router-login.test.ts`, `tests/server/router-normalize.test.ts`

**Interfaces:**
- Produces: `XiaomiRouter.login(password)`, `logout()`, `isAuthenticated()`, `getStatus()`, `getDevices()`, `getWifi()`, `reboot()`, and a private authenticated `request()` helper.

- [ ] Write failing tests for nonce shape, `sha1(nonce + sha1(password + key))`, login code mapping, token expiry, timeout mapping, and normalization fixtures.
- [ ] Implement login-page metadata parsing with the R4AC fallback key `a2ffa5c9be07488bbb04a3a47d3c5f6a`, nonce generation, form-encoded login, and in-memory token storage.
- [ ] Build authenticated URLs as `/cgi-bin/luci/;stok=<token>/api/<endpoint>`, use an 8-second abort timeout, and map router codes `401/403` to session expiry.
- [ ] Normalize `misystem/status`, `misystem/devicelist`, `xqnetwork/wifi_detail_all`, `xqnetwork/wan_info`, and `xqsystem/init_info` without assuming every field exists.
- [ ] Add secret-safe error messages and tests proving passwords and tokens never appear in serialized errors.
- [ ] Run targeted tests, all tests, and typecheck.
- [ ] Commit with `feat: add secure MiWiFi adapter`.

### Task 3: QoS service with capability detection

**Files:**
- Create: `src/server/services/qos-service.ts`
- Test: `tests/server/qos-service.test.ts`

**Interfaces:**
- Consumes: `XiaomiRouter.request()`, `Device`, and QoS schemas.
- Produces: `getQosState()`, `updateDeviceQos(mac, update)`, `clearDeviceQos(mac)`, `setQosEnabled(enabled)`.

- [ ] Write failing fixture tests for `misystem/qos_info`, including `status.on`, `status.mode`, `band`, `list[].qos`, and partial/missing fields.
- [ ] Normalize speed limits from router KB/s values and expose the router’s priority level only when `qos.level` is present.
- [ ] Implement `POST misystem/qos_limits` with `data=[{mac,maxup,maxdown}]`, `GET misystem/qos_offlimit?mac=...`, and `GET misystem/qos_switch?on=0|1`.
- [ ] Probe priority support without mutation from QoS state and authenticated QoS page metadata. Only enable the priority selector when the firmware advertises a compatible mutation path; otherwise return `prioritySupported: false`.
- [ ] After each mutation, poll `qos_info` up to three times over three seconds and reject a verification mismatch.
- [ ] Run targeted tests, all tests, and typecheck.
- [ ] Commit with `feat: add verified device QoS controls`.

### Task 4: Port-forwarding service and disabled-rule store

**Files:**
- Create: `src/server/services/port-forward-service.ts`
- Create: `src/server/storage/disabled-rules.ts`
- Test: `tests/server/port-forward-service.test.ts`, `tests/server/disabled-rules.test.ts`

**Interfaces:**
- Produces: `list()`, `create(input)`, `update(id,input)`, `setEnabled(id,enabled)`, and `remove(id)`.

- [ ] Write failing tests for Xiaomi’s `portforward?ftype=1` response, protocol mapping `1/2/3`, overlap rejection, delete/add edit flow, rollback, and atomic disabled-rule persistence.
- [ ] Implement listing through `GET xqnetwork/portforward?ftype=1`, creation through form-encoded `POST xqnetwork/add_redirect`, and deletion through `POST xqnetwork/delete_redirect`.
- [ ] Derive stable IDs from protocol, external port, internal IP, and internal port; merge active router rules with locally disabled rules.
- [ ] Implement edit as verified delete then add, with best-effort restoration of the original if add fails.
- [ ] Implement disable by deleting from the router and atomically saving the normalized non-secret rule under the application data directory; enable by adding and then removing the saved entry.
- [ ] Verify every operation by re-listing router rules; never report success from the mutation response alone.
- [ ] Run targeted tests, all tests, and typecheck.
- [ ] Commit with `feat: add verified port forwarding management`.

### Task 5: Secure local HTTP API

**Files:**
- Create: `src/server/config.ts`, `src/server/session-store.ts`, `src/server/security.ts`
- Create: `src/server/app.ts`, `src/server/index.ts`
- Test: `tests/server/api.test.ts`, `tests/server/security.test.ts`

**Interfaces:**
- Produces: `/api/health`, `/api/session`, `/api/status`, `/api/devices`, `/api/wifi`, `/api/qos`, `/api/port-forwards`, and `/api/router/reboot`.

- [ ] Write failing Supertest cases for loopback Host enforcement, Origin enforcement, login throttling, `HttpOnly; SameSite=Strict` cookies, CSRF rejection, auth rejection, schema errors, and sanitized error output.
- [ ] Implement an in-memory random session ID and CSRF token. Return the CSRF token only in authenticated session JSON and require `X-CSRF-Token` for mutations.
- [ ] Implement three login attempts per five minutes, no automatic bad-password retries, 30-minute idle sessions, explicit logout, and router logout/token clearing on session expiry.
- [ ] Add all routes using the service interfaces; use `202 Accepted` for reboot and clear the session immediately after the router acknowledges it.
- [ ] Serve the Vite production build with SPA fallback and strict security headers.
- [ ] Run API/security tests, all tests, typecheck, and build.
- [ ] Commit with `feat: expose secure localhost API`.

### Task 6: React dashboard

**Files:**
- Create: `index.html`, `src/client/main.tsx`, `src/client/api.ts`, `src/client/App.tsx`
- Create: `src/client/components/*`, `src/client/styles.css`
- Test: `tests/client/app.test.tsx`

**Interfaces:**
- Consumes: local API contracts and CSRF token.
- Produces: login, overview, devices/QoS, port forwards, and settings/reboot screens.

- [ ] Write failing component tests for login error states, navigation, status refresh, device search, QoS editing, forward-rule validation, confirmations, session expiry, and unsupported priority messaging.
- [ ] Build a responsive dark dashboard with a compact sidebar, status cards, data tables, clear loading/empty/error states, and no decorative dependency on router-hosted assets.
- [ ] Build device/QoS editing with upload/download caps, router-reported priority support, QoS master switch, and read-back confirmation feedback.
- [ ] Build port-forward CRUD forms with device/IP selection, TCP/UDP/Both, enable toggles, conflict errors, and destructive confirmations.
- [ ] Build read-only Wi-Fi details and a typed `REBOOT` confirmation.
- [ ] Run component tests, accessibility checks, typecheck, and production build.
- [ ] Commit with `feat: build router gateway dashboard`.

### Task 7: Windows launcher and distributable package

**Files:**
- Create: `scripts/start-gateway.ps1`, `Start Router Gateway.cmd`
- Create: `README.md`
- Test: `tests/scripts/launcher.Tests.ps1`

**Interfaces:**
- Produces: a one-click launcher and `outputs/Xiaomi-Router-Gateway.zip`.

- [ ] Write Pester-compatible launcher checks for Node detection, duplicate-process detection through `/api/health`, bounded health polling, log location, and browser opening.
- [ ] Implement the CMD entry point and PowerShell launcher. Start Node hidden, store PID/logs under `%LOCALAPPDATA%\XiaomiRouterGateway`, wait at most 20 seconds, then open `http://127.0.0.1:8787`.
- [ ] Add concise installation, start, stop, privacy, and troubleshooting instructions.
- [ ] Build production assets, install production dependencies into a staging directory, copy launcher/runtime files, and archive the exact staged directory.
- [ ] Run launcher checks and inspect the archive contents.
- [ ] Commit with `feat: package one-click Windows gateway`.

### Task 8: End-to-end and release verification

**Files:**
- Create: `e2e/gateway.spec.ts`, `tests/fixtures/*`
- Modify: only files required by failures found in verification.

**Interfaces:**
- Produces: verified release artifact and test evidence.

- [ ] Add Playwright coverage using the fake router adapter for login, overview, QoS, port forwarding, unsupported features, timeout, and expired session flows.
- [ ] Run `npm test`, `npm run test:e2e`, `npm run typecheck`, and `npm run build`.
- [ ] Start the production server, verify only `127.0.0.1:8787` is listening, and exercise `/api/health`.
- [ ] Perform read-only live checks against `192.168.31.1`; do not mutate QoS, forwarding, Wi-Fi, or reboot without user interaction.
- [ ] Extract the release zip to a clean temporary directory, launch it, verify health/UI, stop it, and compare packaged build hashes to the staged output.
- [ ] Run `git status --short`, review the diff for secrets and generated junk, then commit release fixes if any.

