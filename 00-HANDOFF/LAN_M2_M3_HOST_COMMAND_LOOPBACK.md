# LAN-M2/M3 — Host Command Boundary and Loopback Transport

Status: **LAN-M2/M3 complete** on `KIRCH-FREEHAND`.

## Implemented

- `SalrynHostCommandEnvelope<T>` and `SalrynHostCommandResult<T>` with trace metadata (`StoreId`, `HostId`, `TerminalId`, `UserId`, `TimestampUtc`, `RequestId`)
- `SalrynHostCommandAuthorityGuard` extending LAN-M1 store authority with loopback-only transport rules
- `IHostCommandDispatcher` / `HostCommandDispatcher` with command registry and fail-closed validation
- In-process host command handlers for:
  - deployment status (read proof)
  - product lookup (read proof)
  - official checkout complete
  - official lifecycle append
  - official X-reading preview
  - official Z-reading finalize
  - backup authority probe (placeholder)
- `SalrynHostCommandLoopbackTransport` — same-PC loopback only (`127.0.0.1` / `localhost`)
- `host-command-loopback` infrastructure smoke

## Not Implemented (by design)

- **No cross-PC LAN** — binding or remote client acceptance
- `0.0.0.0` or LAN IP bind
- Pairing / QR / network discovery
- Remote client terminal execution
- Shared SQLite over network
- Client-side official journal writes or sequence allocation
- Mobile/cloud endpoints

## Store Host Doctrine

- **Store Host owns database, official sequence, journal, and backup authority.**
- Clients must send commands to the Store Host later; they must not open shared SQLite directly.
- `ClientTerminal` role fails DB/sequence/journal/backup host commands and loopback transport is disabled for client-only machines in LAN-M3.
- `LanTransportEnabled` remains false; loopback transport is not remote LAN.

## Traceability

Host command envelopes carry `StoreId`, `TerminalId`, `UserId`, and `TimestampUtc`. Results echo `RequestId` and trace metadata. Future journal/audit metadata should continue to include these fields.

## Next Step (LAN-M4)

Proceed with terminal registry / pairing or controlled remote client preparation depending on repository readiness. Do not enable cross-PC transport until pairing and terminal registry foundations are in place.
