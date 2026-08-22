# LAN-M15-E — Abuse / Flood / Negative-Path Hardening

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Built on top of unpromoted LAN-M14-F through LAN-M14-I, LAN-M15-A, LAN-M15-B, LAN-M15-C, and LAN-M15-D.
- Promotion status: parked. No Sandbox promotion has occurred.

## Goal

Make the remote read-only LAN surface safer under repeated denied requests and hostile or
malformed inputs, without adding schema, new persistent storage, or any mutation path.

## Runtime Surface

- New component `RemoteReadOnlyAbuseGuard` (in-memory, no schema, no persistence) provides:
  - `IsHeaderValueWithinLimit` — bounded header length (`MaxHeaderLength = 512`).
  - `IsAcceptableCategory` — allow-list of safe read categories only
    (`status`, `product`, `dashboard`, `terminal-status`, `capabilities`, `bind-policy`,
    `audit`, `diagnostics`, `preflight`).
  - `TryNormalizeTerminalId` — malformed / empty GUID rejection.
  - `SanitizeHostileText` — strips control characters, collapses whitespace, caps length,
    and redacts secret markers (reusing `RemoteReadOnlyBridgeDiagnostics.ContainsSecretMarker`).
  - `RegisterDenial` / `IsSoftThrottled` — a process-local sliding-window soft-throttle
    (`SoftThrottleThreshold = 20` denials per `SoftThrottleWindow = 1 minute`).
- `RemoteReadOnlyBridgeRequestContext` now classifies `HasOversizedHeader`,
  `HasMalformedTerminalId`, and `HasMalformedHandshakeId`, and sanitizes any operator- or
  attacker-supplied `X-Salryn-Remote-Origin` header before it is propagated.
- `RemoteReadOnlyApiAuthorization` rejects oversized headers and non-allow-listed categories
  before touching any service, and records every denial through a soft-throttle-aware audit
  helper.

## Soft-Throttle Model (intentional design)

- The soft-throttle **counts and flags** repeated denials; it does **not** hard-lock a
  terminal. The authorization decision itself remains fail-closed on every request.
- This is deliberate: hard-locking keyed on terminal id / origin could let a spoofed origin
  lock out a legitimate terminal (a self-inflicted denial of service). Counting + flagging
  gives safe repeated-denial audit behavior without that risk.
- Throttle state is process-local and in-memory only. No schema, no file, no database row.

## Negative-Path Behavior

- Oversized bridge headers → `RemoteReadOnlyBridge.OversizedHeaderRejected` (fail-closed).
- Non-allow-listed category → `RemoteReadOnly.UnsafeCategoryDenied` (fail-closed).
- Malformed terminal / handshake GUID → treated as missing identity; existing granular
  denial codes apply.
- Hostile strings in origin / diagnostics are sanitized; secret markers become `[redacted]`.

## Forbidden Scope

LAN-M15-E does not implement, and must not implement:

- schema/migration changes, seed data changes, or any new persistent storage
- secret logging or SQLite path exposure
- default LAN bind, wildcard bind, or `0.0.` + `0.0` bind
- CORS relaxation or cookie-only LAN trust
- remote mutation, checkout, or remote cashier/admin execution
- official journal writes or official sequence allocation
- hard-locking terminals based on spoofable origin
- printer/receipt/PDF/Crystal/ESC-POS behavior
- QR, discovery, mobile, cloud, or network scan behavior
- LAN-M14-J or LAN-M15-G
- Sandbox promotion or Freehand reset

## Decision

Negative-path hardening is implemented as a self-contained, in-memory abuse guard plus
request-context classification and fail-closed rejection of oversized/malformed/bad-category
requests. It adds no schema and no persistence, keeps the soft-throttle non-locking to avoid
spoofed-origin lockout, and never logs secrets. This closes the abuse/flood hardening gap
for LAN-M15-E without weakening any safety boundary.
