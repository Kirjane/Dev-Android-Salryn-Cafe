# LAN-M5 - Controlled Remote Transport Gate

Status: LAN-M5 candidate completed on `KIRCH-FREEHAND`.

## Implemented

- LAN remote transport configuration model and SQLite persistence.
- Default configuration is disabled and loopback-only.
- Explicit LAN bind is allowed only when remote transport is explicitly enabled.
- Wildcard bind addresses such as `0.0.0.0` are rejected.
- Remote gate checks terminal registry state before dispatch.
- Unknown, pending, disabled, and revoked terminals are rejected.
- Active registered terminals may pass the gate only for safe status/read command categories.
- Salryn Store shows and saves remote transport gate settings.
- CoreSmoke guard: `LanM5ControlledRemoteTransportGateChecks`.
- Infrastructure smoke command: `controlled-remote-transport-gate`.

## Non-Negotiable Scope

- LAN-M5 is a remote transport gate only.
- Remote business execution is still disabled.
- Remote official commands are blocked.
- Remote admin mutation commands are blocked.
- Store Host owns the database, official sequence allocation, journal writes, inventory writes, audit logs, and backups.
- Clients must never open the SQLite database directly.
- No shared SQLite over LAN.
- No QR pairing.
- No network discovery.
- No mobile/cloud endpoint.
- No BIR certification, accreditation, legal approval, or production compliance claim.

## UI Notes

Salryn Store exposes:

- Remote transport enabled.
- Bind mode.
- Bind address.
- Port.
- Require paired terminal.
- Reject unknown terminals.
- Official remote commands shown false/disabled.
- Admin remote commands shown false/disabled.
- Loopback-only save.
- Explicit LAN save gated by the local enabled checkbox.
- Disable remote transport.

The UI warning remains explicit: remote cashier/admin execution is not enabled, and clients must not open SQLite directly.

## LAN-M6 Recommendation

LAN-M6 should be Pairing Activation / Remote Client Handshake. It should not enable cashier execution unless explicitly approved after pairing consumption, terminal identity binding, transport authentication, and operation-level authorization are complete.
