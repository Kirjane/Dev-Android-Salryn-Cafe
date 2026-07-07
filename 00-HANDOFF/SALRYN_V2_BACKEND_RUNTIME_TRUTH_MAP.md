# SALRYN V2 BACKEND RUNTIME TRUTH MAP

Status: Review baseline
Date: 2026-07-07
Branch inspected: `KIRCH-SANDBOX`
Commit inspected: `5fb2f3d1016fc9e1cd683a42f3422a4e19948e1d`
Purpose: Establish an honest V2 backend/runtime baseline for React Host Console and LAN Client work.

---

## Source Stamp

This document is based on direct repo inspection only.

Primary inspected files:

- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`
- `src/Salryn.Api.Local/LocalUserAdminEndpoints.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosApiEndpoints.cs`
- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.Host.Local/Bootstrap/LocalHostHandlerBootstrapModels.cs`
- `src/Salryn.Host.Local/Contracts/LocalHostOperationContractModels.cs`

This is a V2 stable-candidate truth map, not a release-readiness claim.

---

## Executive Summary

Salryn already has a real local API spine that React can consume today for host login/session and multiple read-oriented operator screens.

The React Host Console is not blocked by a missing API foundation.
It is blocked by incomplete host-side write coverage, intentionally disabled screens, and placeholder-backed Local Host runtime seams that must not be misrepresented as complete.

Bottom line:

- Host auth/session for React: real
- Host read screens for React: largely real
- Host write workflows for React: only partially exposed
- Remote POS: real but bounded and intentionally limited
- Remote read-only: real and explicitly non-mutating
- Local Host runtime: still contains placeholder operations and must not be treated as V2-complete

---

## What React Can Honestly Use Now

### 1. Host login and session are real

The local API exposes real host-local auth/session routes:

- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/session`

Evidence:

- `LocalApiApplicationFactory` maps all three routes and issues a real session cookie through `LocalApiSessionService`.
- These routes are guarded as localhost operator routes, not fake frontend-only state.

Current truth:

- Good enough for V2 Host Console local auth baseline
- Not evidence for remote/LAN auth
- Not evidence that WinForms auth is acceptable for V2

---

### 2. Host Console read screens already have real local API routes

The API exposes real authenticated/permissioned read routes for:

- Dashboard summary
- Products list/detail/catalog
- Inventory low-stock and stock movement history
- Sales history and sale detail
- Users list/detail/roles
- Audit logs and error logs

Evidence:

- `MapReadOnlyOperatorEndpoints` in `ReadOnlyEndpointExtensions.cs`
- `App.tsx` wires these pages into the React console
- `navigation.ts` and `permissions.ts` gate access by actual session permissions

Current truth:

- These are real V2 Host Console backend surfaces
- These are not mock APIs
- React can continue wiring against these safely

---

### 3. Users create flow has a real local write endpoint

The API exposes:

- `POST /api/users`

Evidence:

- `LocalUserAdminEndpoints.cs`

Current truth:

- React already has one genuine host-side mutation surface beyond auth
- This does not imply broader host mutation coverage exists

---

### 4. Remote read-only is a real bounded surface

The API exposes `/api/remote-readonly` for:

- status
- capabilities
- dashboard summary
- products
- terminal status
- bind policy
- diagnostics
- preflight
- audit visibility

Current truth:

- This is intentionally read-only
- Disabled/denied mutations are explicit, not faked
- This is honest V2 bounded behavior

---

### 5. Remote POS is real, but intentionally limited

The API exposes `/api/remote-pos` for:

- pairing request/activate/status
- session login/logout/read
- capabilities
- cart lifecycle
- shift open/close/read
- checkout submit/status
- admin status read

Current truth:

- This is a real bounded LAN runtime
- It is not a full remote admin/operator replacement
- Unsupported actions are intentionally blocked or labeled unsupported

---

## What Is Real But Bounded

### 1. Remote POS manager override is intentionally not supported

`POST /api/remote-pos/manager/override` exists, but the surrounding comments and authorization matrix make clear that remote manager override is not supported in this build.

Important nuance:

- This is better than fake success
- It still means the surface is incomplete for any workflow expecting remote override support

Risk:

- The route exists, so anyone summarizing superficially could overstate support

V2 truth:

- Keep this classified as intentionally unsupported
- Do not market it as implemented manager override behavior

---

### 2. Remote POS checkout is real, but durability remains bounded

Remote POS checkout uses a real host-owned completion path, but known limitations remain:

- request idempotency is in memory
- cart state is in memory
- session state is in memory
- receipt allocation for remote POS is still process-scoped rather than durable sequence-backed

V2 truth:

- Real enough for bounded candidate/runtime testing
- Not yet strong enough to represent as fully durable business-complete checkout infrastructure

---

### 3. LAN control is real but host-local only

`/api/lan-control` is a real host-local control surface for:

- remote transport enable/disable
- remote POS runtime enable/disable
- join-code rotation and request review
- terminal disable/revoke

V2 truth:

- This is good and intentionally host-local
- It must never be represented as remote-admin capability

---

## What Is Not Yet Ready For Honest V2 Completion Claims

### 1. React Host Console mutation coverage is still narrow

From current repo inspection, React has strong read coverage, but host-side write coverage exposed through the local API is still narrow.

Clearly present local write routes:

- auth login/logout
- user creation

Clearly absent from the inspected local host operator API surface:

- product create/update/deactivate endpoints for the host console
- inventory adjustment write endpoints for the host console
- local host checkout submit endpoints for the host console
- shift open/close endpoints for the host console
- backup create/verify/restore endpoints for the host console
- settings update endpoints for the host console

V2 truth:

- React Host Console can authenticate and read substantial live data now
- It cannot yet claim end-to-end operational completeness for host mutations

---

### 2. Several React nav items are intentionally disabled

Current disabled nav items in `navigation.ts`:

- POS Checkout
- Official Output Text File
- Settings
- Backup

V2 truth:

- This is honest behavior
- These pages should remain disabled until backed by real local API/runtime behavior
- They must not be replaced with fake success flows

---

### 3. Local Host runtime still contains placeholder operations

The Local Host runtime contract/bootstrap layer still includes placeholder-backed operations, including:

- login
- complete sale
- open cashier session
- close cashier session
- daily sales summary
- backup health

Evidence:

- `LocalHostOperationContractModels.cs`
- `LocalHostHandlerBootstrapModels.cs`

V2 truth:

- This runtime seam is foundational, not complete
- It must not be used as evidence that equivalent React host flows are fully implemented
- If React needs these capabilities through Local Host, they must be implemented or clearly quarantined

---

### 4. WinForms session/bootstrap behavior is legacy only

The existing WinForms auto-session/bootstrap behavior remains legacy and must not be used as proof of V2 auth completeness.

V2 truth:

- React local auth evidence comes from the local API session routes
- WinForms auto-login remains out of scope as V2 completion evidence

---

## Honest V2 Classification By Area

### Safe For React Host Console Now

- Local login/session
- Dashboard summary
- Products read
- Inventory read
- Sales history read
- Users read
- Users create
- Audit/error log read
- Host LAN control read/manage surface

### Real But Bounded / Needs Careful Labeling

- Remote read-only LAN surface
- Remote POS runtime
- Remote POS checkout
- Remote POS admin-status visibility
- Manager override endpoint shape without real supported behavior

### Not Ready / Still Missing For V2 Host Completeness

- Host checkout local React flow
- Host shift management local React flow
- Host product management mutation API
- Host inventory adjustment mutation API
- Host settings mutation API
- Host backup mutation API
- Local Host placeholder operations required by future React flows
- Durable receipt numbering decision for remote/runtime integrity claims

---

## What Must Not Be Claimed Yet

- "React Host Console is fully operational"
- "Checkout is complete for V2 host operations"
- "Shift workflow is complete in React"
- "Backup is available in the React Host Console"
- "Settings are available in the React Host Console"
- "Local Host runtime is fully implemented"
- "Remote POS is fully durable across restart"
- "Receipt numbering is fully resolved for V2"

---

## Recommended Immediate Backend Runtime Priorities

### Priority 1. Decide the one authoritative host checkout path for React

Need:

- one honest local React checkout path
- no fallback to placeholder Local Host behavior
- no fake success handling

### Priority 2. Expose required local host mutation endpoints

Minimum high-value host write surfaces for V2 Host Console:

- checkout submit
- shift open/close
- product create/update/deactivate or a clearly smaller approved scope
- inventory adjustment if inventory is meant to be operational in V2

### Priority 3. Quarantine or replace Local Host placeholders

For each placeholder-backed Local Host operation:

- implement it if React genuinely needs it now
- otherwise classify it as out of V2 scope and keep it out of the React claim surface

### Priority 4. Keep disabled pages honest

Do not enable these until backend/runtime support exists:

- POS Checkout
- Settings
- Backup
- Official Output Text File

### Priority 5. Preserve explicit unsupported states

Good current pattern:

- disabled page
- read-only surface
- not supported response

Bad future pattern:

- fake success
- hidden placeholder
- UI pretending a workflow exists

---

## Final Verdict

Salryn V2 does not need a ground-up backend rewrite for React.
It already has a real auth/session and read-data API foundation.

The actual backend/runtime problem is narrower:

- host mutation coverage is incomplete
- Local Host placeholders still exist
- some remote/runtime surfaces are real but bounded
- disabled states are honest and should stay honest

Best next backend statement:

"React Host Console can already use real local auth and multiple real read endpoints. V2 backend work should focus on the missing host-side mutation path, placeholder quarantine, and durable runtime integrity rather than rebuilding the entire API surface."
