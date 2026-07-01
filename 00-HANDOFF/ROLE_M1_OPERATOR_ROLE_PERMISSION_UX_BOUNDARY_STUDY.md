# ROLE-M1 Operator Role Permission UX Boundary Study

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Sandbox SHA at study start: `56fd24c0a3463775ccdcf643cbdaee22614759cf`
- Freehand SHA at study start: `56fd24c0a3463775ccdcf643cbdaee22614759cf`
- Ahead/behind at study start: `0 ahead / 0 behind`
- Stable milestone: `LAN-M13 — remote read-only LAN usability`
- Study decision: no schema, auth, session, or backend blocker found for a frontend-only role-aware UX boundary.

## Current Role Model

Current seeded system roles are:

- `Admin`
- `Manager`
- `Cashier`

Role authority is represented by seeded permission strings assigned to each role. Role names may be displayed to the operator, but they must not be used as the access authority for the React/WebView shell.

## Current Permission Model

The existing seeded permission strings are:

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

No new permission strings are introduced by ROLE-M1.

## Current UI Exposure

The current shared React/WebView shell contains one navigation model and the following implemented available pages:

- Dashboard
- Remote Read-Only Client
- Products
- Inventory
- Sales History
- Users
- Audit

The current shell also lists disabled/later future entries:

- POS Checkout
- Official Output Text File
- Settings
- Backup

No implemented Reports page exists in the current App.Web shell, so Reports remains later/deferred even though `Reports.View` exists in the seed model.

## Current Session Shape

The frontend session contract exposes:

- `userId`
- `username`
- `displayName`
- `roleName`
- `roleId`
- `status`
- `permissions`

ROLE-M1 uses `session.user.permissions` as the primary access source.

## Proposed Screen-to-Permission Matrix

| Screen or action area | Access rule |
| --- | --- |
| Dashboard | Authenticated user; no mutation |
| Remote Read-Only Client | Authenticated/local operator read-only surface |
| Products page | `Products.View` |
| Product management actions | `Products.Manage` |
| Inventory page | `Inventory.View` |
| Inventory adjustment actions | `Inventory.Adjust` |
| Sales History | `Sales.ViewHistory` |
| Users | `Users.Manage` |
| Audit | `Audit.View` |
| POS Checkout | Disabled/later; not implemented in ROLE-M1 |
| Official Output Text File | Disabled/later; not implemented in ROLE-M1 |
| Settings | Disabled/later; not implemented in ROLE-M1 |
| Backup | Disabled/later; not implemented in ROLE-M1 |
| Reports | Disabled/later because no implemented page exists |

## Proposed Role Modes

### Admin mode

Admin has all existing seeded permissions and can access all currently implemented available pages in the shared shell.

### Manager mode

Manager has operational permissions and can access operational pages where the current seeded permissions allow it. Manager does not receive user management, settings, backup, deployment management, pairing administration, or full system-control surfaces.

### Cashier mode

Cashier remains checkout-first. The current shell does not implement POS checkout yet, so Cashier is limited to authenticated dashboard/remote read-only context and currently permitted safe read surfaces such as product lookup and sales history where the seed permissions allow them.

## Cashier Boundary Decision

Cashier must not get inventory mutation.

Cashier must not get product management.

Cashier must not get users, audit, settings, backup, or system administration screens.

Cashier remains checkout-first. POS checkout itself is not implemented in ROLE-M1.

## Manager Boundary Decision

Manager may manage operational work only within current seeded permissions.

Manager must not manage users, settings, backup, deployment, pairing administration, or system administration.

Manager must not receive full admin-only official document mutation surfaces.

## Admin Boundary Decision

Admin can access all currently implemented available pages because Admin carries all current seeded permissions.

## Backend Enforcement Note

UI hiding is not security.

ROLE-M1 implements frontend role-aware UX only. It prevents the shared React/WebView shell from rendering unauthorized pages and from listing unauthorized enabled navigation entries. Backend-wide permission enforcement is a future separate batch unless a narrow existing backend pattern is found and explicitly assigned.

## Explicit Non-Goals

- No schema or migration change
- No seed data change
- No role editor
- No custom permission editor
- No POS checkout implementation
- No LAN-M14
- No LAN mutation
- No RMO behavior
- No official-output behavior
- No printer behavior
- No PDF behavior
- No Crystal behavior
- No ESC/POS behavior
- No certification, accreditation, legal-compliance, or production-approval claim

## Implemented in ROLE-M1

ROLE-M1 implemented a narrow frontend role-aware UX boundary:

- Added `src/Salryn.App.Web/src/security/permissions.ts`
  - Mirrors existing seed permission strings only.
  - Exports `hasPermission`, `hasAnyPermission`, `canAccessPage`, and `getFirstAllowedPage`.
  - Uses `session.user.permissions` for access decisions.
  - Does not use `roleName` for authorization.
- Updated `src/Salryn.App.Web/src/navigation.ts`
  - Keeps one shared navigation model.
  - Filters implemented enabled pages through `canAccessPage`.
  - Keeps future/later entries disabled.
- Updated `src/Salryn.App.Web/src/components/AppShell.tsx`
  - Renders permission-aware navigation from the current session.
  - Still displays role name only as operator context.
- Updated `src/Salryn.App.Web/src/App.tsx`
  - Adds centralized page access guard.
  - Blocks unauthorized page render.
  - Redirects to the first allowed page when possible.
  - Provides an `AccessDenied` state as a fail-closed fallback.
- Added `tests/Salryn.Tests/RoleM1OperatorRolePermissionUxCompileChecks.cs`
  - Source-level guard coverage for permission constants, navigation gating, page guard presence, role boundaries, duplicate role-shell prevention, and scope containment.
  - No new infrastructure smoke command was registered in this sprint.

## Recommended Next Batch

After ROLE-M1 is reviewed, built, and verified by Codex, schedule a separate backend permission enforcement batch.

That future batch should inspect existing backend auth/session patterns, define narrow endpoint-level enforcement rules, and add backend tests/smokes without mixing in role editing, custom permissions, POS checkout, LAN write behavior, RMO, official output, or printer work.
