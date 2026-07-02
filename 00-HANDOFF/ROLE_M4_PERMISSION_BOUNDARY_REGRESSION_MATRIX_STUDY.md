# ROLE-M4 Permission Boundary Regression Matrix Study

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Sandbox SHA at sprint start: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Freehand SHA at sprint start: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Ahead/behind at sprint start: `0 ahead / 0 behind`
- Current stable stack:
  - `LAN-M13` - remote read-only LAN usability
  - `ROLE-M1` - role-aware operator UX boundary
  - `ROLE-M2` - backend permission enforcement and confidence baseline
  - `ROLE-M3` - HTTP-level Api.Local permission runtime harness

## Existing Role Source

Existing seeded roles only, from `src/Salryn.Infrastructure/Data/Seeding/DefaultSeedData.cs`:

| Role | Seed status | Seeded permission posture |
| --- | --- | --- |
| `Admin` | Active default role | Broadly allowed where seeded permission includes access. Includes `Users.Manage`, `Products.View`, `Inventory.View`, `Sales.ViewHistory`, `Audit.View`, and `StoreTerminal.View`. |
| `Manager` | Active default role | Allowed for product, inventory, sales history, reports-related/dashboard if authenticated-only allows it, and terminal view. Does not include `Users.Manage` or `Audit.View`. |
| `Cashier` | Active default role | Allowed for products and sales history where seeded. Does not include `Inventory.View`, `Users.Manage`, `Audit.View`, or `StoreTerminal.View`. |

No seed data is changed by ROLE-M4.

## Existing Permission Source

### Seeded permission strings

Current seed truth includes:

- `Settings.Manage`
- `Users.Manage`
- `Products.View`
- `Products.Manage`
- `Inventory.View`
- `Inventory.Adjust`
- `Sales.Create`
- `Sales.Void`
- `Sales.ViewHistory`
- `Reports.View`
- `OfficialOutput.Run`
- `OfficialDocument.Reprint`
- `OfficialDocument.Void`
- `OfficialDocument.Cancel`
- `OfficialDocument.Correction`
- `OfficialDocument.Refund`
- `OfficialReading.View`
- `OfficialReading.Finalize`
- `OfficialEvidence.Export`
- `Backup.Manage`
- `Audit.View`
- `ImportExport.Products`
- `Shift.OpenClose`
- `StoreDeployment.Manage`
- `StoreTerminal.View`
- `StoreTerminal.Manage`
- `StoreTerminal.PairingIssue`
- `StoreTerminal.PairingRevoke`

### Frontend page catalog

`src/Salryn.App.Web/src/security/permissions.ts` uses these page semantics:

| Page | Frontend access rule |
| --- | --- |
| `dashboard` | Authenticated-only |
| `remote-readonly` | Authenticated-only |
| `products` | `Products.View` |
| `inventory` | `Inventory.View` |
| `sales` | `Sales.ViewHistory` |
| `users` | `Users.Manage` |
| `audit` | `Audit.View` |

### Backend permission catalog

`src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs` defines the backend permission constants used by current endpoint gates:

- `Products.View`
- `Inventory.View`
- `Sales.ViewHistory`
- `Users.Manage`
- `Audit.View`
- `StoreTerminal.View`

### Backend endpoint gates

`src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs` gates current operator read endpoints with:

- Product reads: `Products.View`
- Inventory reads: `Inventory.View`
- Sales history/detail reads: `Sales.ViewHistory`
- User reads: `Users.Manage`
- Audit/error reads: `Audit.View`

`src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs` gates remote-readonly endpoints with:

- Status/capabilities/dashboard-summary/bind-policy: authenticated-only
- Products: `Products.View`
- Terminal status: `StoreTerminal.View`
- Audit visibility: `Audit.View`

### ROLE-M3 runtime harness

`tests/Salryn.Tests/RoleM3HttpApiPermissionRuntimeChecks.cs` already proves representative 401/403 behavior against the real Api.Local endpoint pipeline. ROLE-M4 broadens that confidence into Admin, Manager, and Cashier seeded-role scenarios.

## Role-to-Page Matrix

ROLE-M1 frontend semantics, applied to current seed truth:

| Page | Admin | Manager | Cashier | Rule |
| --- | --- | --- | --- | --- |
| `dashboard` | Allowed | Allowed | Allowed | Authenticated-only |
| `remote-readonly` | Allowed | Allowed | Allowed | Authenticated-only |
| `products` | Allowed | Allowed | Allowed | `Products.View` |
| `inventory` | Allowed | Allowed | Denied | `Inventory.View` |
| `sales` | Allowed | Allowed | Allowed | `Sales.ViewHistory` |
| `users` | Allowed | Denied | Denied | `Users.Manage` |
| `audit` | Allowed | Denied | Denied | `Audit.View` |

## Role-to-Endpoint Matrix

Expected HTTP permission-boundary behavior for authenticated role sessions. `Allowed` means the route must not return `401` or `403`; downstream data state may still produce another non-auth response.

| Endpoint | No session | Admin | Manager | Cashier | Rule |
| --- | --- | --- | --- | --- | --- |
| `GET /api/dashboard/summary` | `401` | Allowed | Allowed | Allowed | Authenticated-only |
| `GET /api/products` | `401` | Allowed | Allowed | Allowed | `Products.View` |
| `GET /api/inventory/low-stock` | `401` | Allowed | Allowed | `403` | `Inventory.View` |
| `GET /api/sales/history` | `401` | Allowed | Allowed | Allowed | `Sales.ViewHistory` |
| `GET /api/users` | `401` | Allowed | `403` | `403` | `Users.Manage` |
| `GET /api/audit/logs` | `401` | Allowed | `403` | `403` | `Audit.View` |
| `GET /api/remote-readonly/status` | `401` | Allowed | Allowed | Allowed | Authenticated-only |
| `GET /api/remote-readonly/products` | `401` | Allowed | Allowed | Allowed | `Products.View` |
| `GET /api/remote-readonly/terminal-status` | `401` | Allowed | Allowed | `403` | `StoreTerminal.View` |
| `GET /api/remote-readonly/audit` | `401` | Allowed | `403` | `403` | `Audit.View` |

## Explicit Matrix Notes

- Admin should be broadly allowed where seeded permissions include access.
- Manager should be allowed for products, inventory, sales history, reports-related/dashboard if authenticated-only allows it, and terminal view where `StoreTerminal.View` is seeded.
- Manager should not get `Users.Manage` unless seeded; current seed truth does not seed it.
- Manager should not get `Audit.View` unless seeded; current seed truth does not seed it.
- Cashier should be allowed for products and sales history where seeded.
- Cashier should not get `Inventory.View` unless seeded; current seed truth does not seed it.
- Cashier should not get `Users.Manage` or `Audit.View` unless seeded; current seed truth does not seed them.
- Cashier should not get `StoreTerminal.View` unless seeded; current seed truth does not seed it.
- Dashboard remains authenticated-only because ROLE-M2 and ROLE-M3 kept it that way.
- Remote-readonly status, capabilities, dashboard-summary, and bind-policy remain authenticated-only.
- Remote-readonly products, terminal-status, and audit visibility remain permission-gated.
- ROLE-M4 does not invent role semantics beyond seed truth.

## Non-Goals

- No schema or migration change
- No seed data change
- No role editor
- No custom permission editor
- No POS checkout implementation
- No Settings, Backup, Official Output, or Reports implementation
- No LAN-M14
- No LAN bind activation
- No live remote transport dispatch
- No LAN mutation
- No remote cashier/admin behavior
- No QR, discovery, mobile, or cloud behavior
- No RMO behavior
- No official-output behavior
- No PDF behavior
- No Crystal behavior
- No ESC/POS behavior
- No printer or receipt-printing behavior
- No certification, accreditation, legal-compliance, or production-approval claim

## Implementation Decision

Proceed with a surgical test/harness implementation using the existing ROLE-M3 runtime seam.

`LocalApiApplicationFactory` already supports creating the real Api.Local endpoint pipeline on an ephemeral loopback URL while injecting an `InMemoryLocalApiSessionStore`. ROLE-M4 can reuse that seam without changing production Api.Local files, routes, DTOs, schema, seed data, LAN behavior, or mutation behavior.
