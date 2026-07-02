# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA before ROLE-M3 promotion review: `34fa718160d44c9cb66b408b963de56710d08594`
- Candidate branch: `KIRCH-FREEHAND`
- ROLE-M3 status: candidate-ready after local verification; not stable until promoted

## Stable Stack

Stable as of `34fa718160d44c9cb66b408b963de56710d08594`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline

`ROLE-M3` is not stable yet. It becomes stable only after Kirch promotes the verified candidate to `KIRCH-SANDBOX`.

## What Is Stable

- Local API remains loopback-only at `127.0.0.1:5299`.
- React/WebView is the operator UX target.
- Remote read-only LAN usability remains status/read only.
- Store Host remains the authority for database, official sequence, journal, inventory writes, and backups.
- ROLE-M1 frontend page access uses `session.user.permissions`.
- ROLE-M1 keeps disabled future pages disabled.
- ROLE-M2 backend read-surface access uses `LocalApiSession.Permissions`.
- ROLE-M2 keeps public and auth workflow endpoints reachable by design.
- ROLE-M2 keeps `/api/session` usable for auth-state discovery.

## ROLE-M3 Candidate Status

ROLE-M3 now implements the Api.Local HTTP permission runtime harness on `KIRCH-FREEHAND`.

Implemented candidate pieces:

- `LocalApiApplicationFactory` testability seam for real `WebApplication` creation without calling `Run()`.
- Production `Program.cs` delegates to the factory and still calls `app.Run()`.
- Runtime HTTP harness starts the real endpoint pipeline on ephemeral loopback.
- Runtime HTTP checks prove public, authenticated-only, permission-gated, and role-name-not-authorizing behavior.
- CoreSmoke explicitly runs `RoleM3HttpApiPermissionRuntimeChecks.RunAsync()`.
- Existing LAN/web source guards were adjusted to follow the factory extraction without weakening localhost/read-only boundaries.

ROLE-M3 does not add production routes, DTO changes, seed changes, schema changes, LAN bind activation, live remote transport dispatch, or mutation behavior.

## What Remains Deferred

- Backend mutation authorization beyond existing read surfaces.
- Role editor or custom permission manager.
- POS checkout.
- Settings, Backup, Official Output, and Reports Web screens.
- Live LAN bind and live remote transport dispatch.
- React/WebView pairing activation.
- LAN-M14.
- M4 work.

## Docs Safe To Trust

- This file.
- `00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md`
- `00-HANDOFF/ROLE_M3_HTTP_API_PERMISSION_RUNTIME_HARNESS_STUDY.md`
- `00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md`
- `00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md`
- LAN M9-M13 docs for current read-only LAN usability boundaries.

## Historical Handoffs Only

Older phase documents, master plans, and RMO reconciliation studies remain useful for intent and history, but they must not override current source and the current state docs above. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or live LAN execution are not current approval.

## Must Not Start Yet

- M4 implementation.
- LAN-M14 or any live LAN runtime expansion.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation.
- Role editor/custom permission manager.
- Settings, Backup, Official Output, or Reports implementation.
- RMO, printer, PDF, Crystal, or ESC/POS work.

## Recommended Next Planning Target

After ROLE-M3 is promoted, the safest next planning target is M4: define the next permission-backed operator workflow boundary before implementation. M4 should start from the runtime-proven Api.Local permission harness and must not expand into checkout/admin mutation, live LAN bind, or RMO/printer work without explicit approval.
