# LAN-M15-D — Remote Client Resilience and Session Lifecycle Hardening

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Built on top of unpromoted LAN-M14-F through LAN-M14-I, LAN-M15-A, LAN-M15-B, and LAN-M15-C.
- Promotion status: parked. No Sandbox promotion has occurred.

## Goal

Make the remote read-only client handle real LAN session lifecycle problems clearly and
safely. When a handshake expires, a terminal is disabled/revoked, or a session is stale,
the operator sees a plain, safe explanation, a clear denial reason, and safe next steps —
never a raw exception, never a secret, and never an automatic re-pair, QR, or discovery
attempt.

## Runtime Surface

- New mapper `RemoteReadOnlySessionLifecycle` (pure, side-effect-free) classifies a safe
  denial code (or message) into a lifecycle state and operator guidance:
  - `SessionActive`, `LocalOnlyReadMode`, `HandshakeExpired`, `HandshakeMissing`,
    `TerminalPending`, `TerminalDisabled`, `TerminalRevoked`, `TerminalUnknown`,
    `RemoteDisabled`, `CookieRejected`, `Unknown`.
  - Every guidance string is passed through `RemoteReadOnlyBridgeDiagnostics.SanitizePublicText`.
  - `AutomaticRepairDisabled` is always `true`. Re-pairing is always described as a manual
    operation on the POS host.
- New contract `RemoteReadOnlySessionResilienceResponse` carries `LifecycleState`,
  `Guidance`, `ReadOnlyRefreshSafe`, `RepairRequired`, `AutomaticRepairDisabled`,
  `StaleDataWarning`, `DenialCode`, and `DenialMessage`.
- `RemoteReadOnlyApiService` populates `SessionResilience` on the status and terminal-status
  responses:
  - success paths report `SessionActive`,
  - non-bridge localhost reads report `LocalOnlyReadMode`,
  - bridge denials are classified from the safe denial code/message.
- The remote-readonly endpoints remain GET-only under `/api/remote-readonly`. No new route
  and no write path is added.

## Operator UX

The Remote Read-Only Client page gains a "Session Resilience / Lifecycle" card that shows:

- Lifecycle state and safe operator guidance.
- Last successful safe read timestamp (from the read-only status query update time).
- Whether a read-only refresh is safe, and whether a manual re-pair on the POS host is
  required.
- A clear, safe denial reason when a read is denied.
- A stale-data warning when the last read-only refresh failed, telling the operator to use
  "Refresh read-only state" — automatic retry is disabled to avoid request floods.

## Resilience / No-Retry-Storm Model

- All read-only queries use `retry: false`; there is no automatic retry loop and no polling
  interval, so a denied or failed read cannot produce a retry storm.
- Recovery is operator-driven: the existing "Refresh read-only state" action is the only
  retry path.
- No token, cookie, or header value is shown in the UI. Denial reasons are sanitized safe
  text only.

## Forbidden Scope

LAN-M15-D does not implement, and must not implement:

- automatic re-pair, automatic QR, or automatic network discovery
- retry storms or background polling
- schema/migration changes or seed data changes
- remote mutation, checkout, or remote cashier/admin execution
- official journal writes or official sequence allocation
- raw exception display or secret/token/cookie exposure
- any hidden write endpoint; the remote client remains GET/read-only only
- printer/receipt/PDF/Crystal/ESC-POS behavior
- mobile, cloud, or network scan behavior
- LAN-M14-J or LAN-M15-G
- Sandbox promotion or Freehand reset

## Decision

Session lifecycle handling is implemented as a pure, testable mapper plus read-only UI
surfacing. It reuses the safe denial codes already produced by the bridge authorization
path, adds no write capability, and keeps recovery operator-driven with no retry storm.
This closes the field-resilience gap for LAN-M15-D without weakening any safety boundary.
