# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA at ROLE-M2 closeout start: `bc01212ea4c6368f33ae9db460d7ec86001e1071`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate start for ROLE-M2 closeout: `399b2f23c9cad93d2a34e09f980227fda7078524`

## Stable Stack

Stable as of `bc01212ea4c6368f33ae9db460d7ec86001e1071`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary

`ROLE-M2` is candidate-ready only if the closeout implementation verifies on
`KIRCH-FREEHAND`. It must not be described as stable until Kirch promotes it.

## What Is Stable

- Local API remains loopback-only at `127.0.0.1:5299`.
- React/WebView is the operator UX target.
- Remote read-only LAN usability is status/read only.
- Store Host remains the authority for database, official sequence, journal,
  inventory writes, and backups.
- ROLE-M1 frontend page access uses `session.user.permissions`.
- ROLE-M1 keeps disabled future pages disabled.

## What ROLE-M2 Completed In Candidate

- Added reusable Api.Local backend permission guards.
- Added endpoint-level permission checks for existing protected read surfaces.
- Kept public and auth workflow endpoints reachable by design.
- Kept `/api/session` usable for auth-state discovery.
- Added source guard coverage and explicit CoreSmoke wiring.
- Updated this current-state index and the repo-wide confidence audit.

## What Remains Deferred

- Backend mutation authorization beyond existing read surfaces.
- Role editor or custom permission manager.
- POS checkout.
- Settings, Backup, Official Output, and Reports Web screens.
- Live LAN bind and live remote transport dispatch.
- React/WebView pairing activation.
- HTTP-level Api.Local integration harness for 401/403 behavior.

## Docs Safe To Trust

- This file.
- `00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md`
- `00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md`
- `00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md`
- LAN M9-M13 docs for current read-only LAN usability boundaries.

## Historical Handoffs Only

Older phase documents, master plans, and RMO reconciliation studies are useful for
intent and history, but they must not override the current source and the current
state docs above. In particular, old references to future mobile, cloud, Crystal,
PDF, printer, or broader RMO work are not current approval.

## Must Not Start Yet

- M3 implementation before Kirch reviews this ROLE-M2 closeout.
- LAN-M14 or any live LAN runtime expansion.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation.
- Role editor/custom permission manager.
- RMO, printer, PDF, Crystal, or ESC/POS work.

## Recommended M3 Planning Target

If ROLE-M2 is verified and Kirch accepts the report, the safest M3 planning target
is an HTTP-level Api.Local permission integration harness plus read-only endpoint
401/403 runtime tests. That planning should not introduce new business features.
