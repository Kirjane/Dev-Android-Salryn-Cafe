# ROLE-M2 Backend Permission Enforcement Boundary

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Stable baseline at closeout start: `bc01212ea4c6368f33ae9db460d7ec86001e1071`
- Freehand study-start commit: `399b2f23c9cad93d2a34e09f980227fda7078524`
- Stable stack before ROLE-M2:
  - `LAN-M13` - remote read-only LAN usability
  - `ROLE-M1` - role-aware operator UX boundary

## Study-State Finding

The initial ROLE-M2 study said implementation should wait because the source for
`MapReadOnlyOperatorEndpoints()` had not been located. Codex local inspection found
the source in:

- `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`

That removed the study blocker. ROLE-M2 was then implemented as a narrow backend
permission enforcement closeout.

## Implemented during ROLE-M2 closeout

- Added `src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs`.
- Kept auth/session source in the existing `LocalApiSessionService` and
  `LocalApiSessionStore`.
- Reused only existing seeded permission strings.
- Added fail-closed helpers:
  - `RequireAuthenticatedSession`
  - `RequirePermission`
  - `RequireAnyPermission`
- Missing or invalid session returns HTTP `401`.
- Authenticated session missing the required permission returns HTTP `403`.
- Permission checks use `LocalApiSession.Permissions`.
- Backend authorization does not use `RoleName`.
- Public/auth-workflow endpoints remain public or auth-workflow appropriate.
- `/api/session` remains usable for auth-state discovery.
- No DTO shape change was needed.
- No schema, migration, or seed data change was made.

## Endpoint Permission Matrix

| Endpoint | ROLE-M2 policy |
| --- | --- |
| `GET /health` | Public |
| `POST /api/auth/login` | Public auth workflow |
| `POST /api/auth/logout` | Auth workflow; tolerates missing/expired session |
| `GET /api/session` | Auth-state discovery |
| `GET /api/dashboard/summary` | Authenticated-only |
| `GET /api/products` | `Products.View` |
| `GET /api/products/{id:guid}` | `Products.View` |
| `GET /api/products/catalog/categories` | `Products.View` |
| `GET /api/products/catalog/brands` | `Products.View` |
| `GET /api/inventory/low-stock` | `Inventory.View` |
| `GET /api/inventory/movements` | `Inventory.View` |
| `GET /api/sales/history` | `Sales.ViewHistory` |
| `GET /api/sales/{id:guid}` | `Sales.ViewHistory` |
| `GET /api/users` | `Users.Manage` |
| `GET /api/users/roles` | `Users.Manage` |
| `GET /api/users/{id:guid}` | `Users.Manage` |
| `GET /api/audit/logs` | `Audit.View` |
| `GET /api/audit/errors` | `Audit.View` |
| `GET /api/remote-readonly/status` | Authenticated-only |
| `GET /api/remote-readonly/capabilities` | Authenticated-only |
| `GET /api/remote-readonly/dashboard-summary` | Authenticated-only |
| `GET /api/remote-readonly/products` | `Products.View` |
| `GET /api/remote-readonly/terminal-status` | `StoreTerminal.View` |
| `GET /api/remote-readonly/bind-policy` | Authenticated-only |
| `GET /api/remote-readonly/audit` | `Audit.View` |

## Guard Coverage

ROLE-M2 adds:

- `tests/Salryn.Tests/RoleM2BackendPermissionEnforcementChecks.cs`
- explicit CoreSmoke wiring in `tools/Salryn.Tools.CoreSmoke/Program.cs`

The guard proves:

- backend permission guard exists
- 401 and 403 behavior are represented in source
- protected routes use required permissions
- auth/session workflow endpoints remain reachable by design
- backend authorization avoids role-name checks
- no mutation routes were introduced in the read-only endpoint files
- no schema/migration/seed files are in the ROLE-M2 changed-file scope
- no LAN bind, QR, discovery, mobile/cloud, WinForms dependency, RMO, or printer
  behavior was introduced

## Explicit Non-Goals

- No schema or migration change
- No seed data change
- No role editor
- No custom permission editor
- No POS checkout implementation
- No Settings, Backup, Official Output, or Reports implementation
- No LAN-M14
- No live LAN bind
- No live remote transport dispatch
- No LAN mutation
- No QR, discovery, mobile/cloud, wildcard bind, or default LAN bind
- No RMO behavior
- No official-output behavior
- No PDF, Crystal, ESC/POS, or printer behavior
- No certification, accreditation, legal-compliance, or production-approval claim

## Promotion Posture

ROLE-M2 is candidate-ready only after local verification passes. It is not stable
until Kirch promotes `KIRCH-FREEHAND` to `KIRCH-SANDBOX`.
