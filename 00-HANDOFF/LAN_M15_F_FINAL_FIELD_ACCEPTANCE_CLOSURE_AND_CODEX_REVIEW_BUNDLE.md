# LAN-M15-F — Final Field Acceptance Closure and Codex Review Bundle

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Built on top of unpromoted LAN-M14-F through LAN-M14-I and LAN-M15-A through LAN-M15-E.
- Promotion status: parked. No Sandbox promotion has occurred.

## Final State Summary

- Sandbox remains `e477699dca27a21954a5ad9d388307fdb9ed08fd`.
- Freehand contains completed unpromoted LAN-M14-F through LAN-M14-I.
- Freehand contains completed unpromoted LAN-M15-A through LAN-M15-F.
- LAN-M14-I remains the LAN-M14 cap.
- LAN-M15-F is the LAN completion cap.
- Next step is one Codex verification of the full stacked candidate.
- No promotion has occurred. No Freehand reset has occurred. LAN-M14-J does not exist.
  LAN-M15-G does not exist.

## LAN Capability Summary (as delivered on Freehand)

- Hybrid bridge is implemented but disabled by default.
- Api.Local defaults to `127.0.0.1:5299`; explicit LAN IP bind is optional only after an
  explicit local operator enable. Wildcard / `0.0.` + `0.0` bind remains rejected.
- Remote-readonly endpoints remain GET-only under `/api/remote-readonly`.
- Remote reads require terminal identity and handshake headers; localhost operator cookies
  are never remote LAN trust.
- Operator diagnostics and preflight are read-only and safe for field troubleshooting.
- Local LAN control (`/api/lan-control`) is operator-only on the POS host and never reachable
  by remote bridge clients.
- Remote client session resilience surfaces expired / disabled / revoked / stale states with
  safe guidance, a last-successful-read timestamp, and no retry storm.
- Repeated denials are soft-throttled in memory (count + flag, no hard lock); oversized and
  malformed headers and non-allow-listed categories are rejected fail-closed.

## Complete Infrastructure Smoke Coverage

Run from repo root with:
`dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- <command>`

- `controlled-remote-transport-gate` — remote transport gate rejects `0.0.` + `0.0`, allows
  safe read after handshake, blocks official mutation.
- `pairing-activation-handshake` — handshake blocks checkout/admin, allows safe reads.
- `safe-remote-readonly-client` — safe read-only client boundary.
- `remote-readonly-web-shell` — web shell read-only surface.
- `remote-readonly-lan-usability` — LAN usability read surface.
- `lan-m14-end-to-end-closure` — end-to-end LAN read-only closure.
- `remote-readonly-lan-field-hardening` (LAN-M15-A) — diagnostics redaction, denial mapping.
- `remote-readonly-lan-preflight` (LAN-M15-B) — preflight readiness and redaction.
- `remote-readonly-lan-local-control` (LAN-M15-C) — local-only operator control authorization.
- `remote-readonly-lan-session-resilience` (LAN-M15-D) — safe lifecycle mapping, no secrets.
- `remote-readonly-lan-negative-path-hardening` (LAN-M15-E) — oversized/malformed/bad-category
  rejection, hostile-string redaction, soft-throttle counting.
- `remote-readonly-lan-final-acceptance` (LAN-M15-F) — aggregate acceptance across the stack.

CoreSmoke (`Salryn.Tools.CoreSmoke`) additionally runs the static LAN guards
`LanM15ARemoteReadonlyLanFieldHardeningChecks` through `LanM15FFinalAcceptanceChecks`.

## Operator Field Runbook

1. Confirm remote read-only transport is disabled by default. Open the Remote Read-Only
   Client page; the bind policy shows "Disabled by default".
2. To enable LAN reads, on the POS host localhost operator surface use Local LAN Control:
   enter an explicit LAN IP (never a wildcard), then enable. Wildcard / `0.0.` + `0.0` is
   rejected.
3. Pair a terminal on the POS host. Remote reads require terminal identity and handshake
   proof; operator cookies alone are never LAN trust.
4. On the remote client, use "Refresh read-only state" to read status/products/dashboard.
5. If a read is denied, read the "Session Resilience / Lifecycle" card: it shows the safe
   lifecycle state, denial reason, and whether a manual re-pair on the POS host is needed.
   There is no automatic re-pair, QR, or discovery.
6. To stop LAN access, disable transport or disable/revoke the terminal from Local LAN
   Control on the POS host.

## Acceptance Checklist

- [x] Remote transport disabled by default; explicit enable required.
- [x] Explicit LAN IP only; no `0.0.` + `0.0`, no wildcard, no default LAN bind.
- [x] Remote-readonly endpoints are GET-only.
- [x] Terminal identity + handshake proof required for remote reads.
- [x] Localhost operator cookies are not remote LAN trust.
- [x] Local control is operator-only and unreachable by remote clients.
- [x] Remote clients cannot enable/disable LAN, configure bind, or pair/disable/revoke terminals.
- [x] Session lifecycle surfaced safely with no retry storm and no secret exposure.
- [x] Oversized/malformed/bad-category requests rejected fail-closed.
- [x] Repeated denials counted and flagged without hard-locking terminals.
- [x] No mutation, checkout, cashier/admin, official journal writes, or sequence allocation.
- [x] No schema, migration, or seed data changes.
- [x] No printer/PDF/Crystal/ESC-POS, QR, discovery, mobile, cloud, or network scan.
- [x] Sandbox unchanged; no promotion; no Freehand reset; no LAN-M14-J; no LAN-M15-G.

## What Is Intentionally Not Implemented

- Default or wildcard LAN bind, and `0.0.` + `0.0` bind.
- Any remote mutation, checkout, cashier/admin execution, official journal writes, or
  official sequence allocation.
- Shared SQLite over LAN and any SQLite path exposure.
- Automatic re-pair, QR pairing, network discovery, or network scanning.
- Mobile, cloud, printer, receipt, PDF, Crystal, or ESC/POS output.
- CORS relaxation and cookie-only LAN trust.
- Hard-locking terminals based on spoofable origin.
- Certification, accreditation, legal-compliance, or production-approval claims.

## Forbidden Scope

LAN-M15-F is a closure and documentation phase. It does not implement, and must not
implement, any of the items in "What Is Intentionally Not Implemented", nor schema/migration
changes, Sandbox promotion, Freehand reset, LAN-M14-J, or LAN-M15-G.

## Decision

LAN implementation on Freehand is complete and capped at LAN-M15-F. The full stacked
candidate (LAN-M14-F through LAN-M14-I and LAN-M15-A through LAN-M15-F) is ready for a single
Codex verification, after which it stops for maintainer review. No promotion has occurred.
