# LAN-M1 — Deployment Role and Salryn Store Foundation

Status: **LAN-M1 complete** on `KIRCH-FREEHAND`.

## Implemented

- Installation roles: `HostServer`, `AllInOne`, `ClientTerminal`
- Persisted deployment identity (`deployment_identity` table, migration v9)
- Machine authority flags derived from installation role
- Client terminal host connection config placeholders (no live transport)
- `Salryn Store` operator page and shell navigation
- `StoreDeployment.Manage` permission (admin-seeded)
- `SalrynStoreHostAuthorityGuard` wired into official checkout and lifecycle boundaries

## Not Implemented (by design)

- LAN transport (HTTP/TCP/SignalR/WebSocket)
- Pairing token validation
- Host connection testing or network discovery
- Client-side official checkout over the network
- Shared SQLite over LAN
- Mobile/cloud endpoints

## Store Host Doctrine

- **Store Host owns the database.**
- Clients must command the Store Host later; they must not open shared SQLite directly.
- Client terminals do not own official sequence or journal authority.
- `LanTransportEnabled` remains `false` for all roles until a transport sprint lands.

## Default Behavior

- Existing databases upgraded through migration v9 default to **All-in-One** authority.
- `CheckStatus` remains read-only; deployment identity is created only through explicit migration/initialization paths.

## Next LAN Sprint

Proceed with **Host Boundary / local command authority** wiring before remote transport unless the repository is already prepared for in-process host command routing.

Do not start remote transport until deployment identity and authority guards are exercised in operator workflows.
