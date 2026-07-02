# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Candidate branch: `KIRCH-FREEHAND`
- Remote M5 candidate SHA before local Codex verification: `a87d1d2b47e70c3f939b7f738cf18573906cfcf4`
- Remote M6 candidate SHA before local Codex verification: `d01f9b225895080d5bdf0aa52f45c54658943d34`
- Verified Freehand candidate SHA before M8: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Promotion status: parked until full M-series completion

## Stable Sandbox Stack

Stable as of `0ea9790d5c86bba350c9db800c101195fa658c0c`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness

This is the stable Sandbox stack only. Freehand is intentionally ahead of Sandbox while promotion is parked until full M-series completion.

## Verified Candidate Stack on Freehand

ROLE-M4 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.

M5 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.

M6 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.

M7 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.

Verified candidate pieces:

- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter

These remain candidate-only until promotion.

## Current Candidate Work

M8 is current candidate work and is not verified until Codex verifies it.

M8 adds M-series completion criteria and a LAN transition gate. Its purpose is to keep future agents from confusing:

- stable Sandbox work
- verified candidate work on Freehand
- current unverified candidate work
- M-series complete state
- final promotion authorization
- LAN transition readiness
- actual LAN completion implementation

M-series is not complete until M8 is verified, closure review passes, Kirch explicitly declares complete, and Kirch explicitly authorizes final promotion.

Promotion debt is `HIGH` at the current stack size, but allowed only because M8 is discipline-only and Kirch directed M-series completion before promotion.

M8 does not add production behavior, routes, DTO changes, schema changes, seed changes, role editor/custom permission management, POS checkout, disabled future Web screens, LAN bind activation, live remote transport dispatch, LAN mutation, QR/discovery/mobile/cloud behavior, RMO behavior, official-output behavior, or printer behavior.

## Promotion Parked Policy

- Promotion is parked until full M-series completion.
- Freehand is intentionally ahead of Sandbox.
- Do not promote unless Kirch explicitly commands it after M-series completion criteria are met.
- Do not reset Freehand unless Kirch explicitly commands it during or after final promotion handling.
- Future sprints must state whether they are stacking on Freehand candidate work.
- GitHub branch truth overrides stale handoff memory.

## What Remains Deferred

- M9 is not started.
- LAN-M14 is not started.
- LAN completion series is not started.
- Backend mutation authorization beyond existing read surfaces.
- Role editor or custom permission manager.
- POS checkout.
- Settings, Backup, Official Output, and Reports Web screens.
- Live LAN bind and live remote transport dispatch.
- React/WebView pairing activation.
- Remote cashier/admin behavior.
- QR, discovery, mobile, or cloud behavior.
- RMO, official-output, printer, PDF, Crystal, or ESC/POS work.
- Certification, accreditation, legal-compliance, or production-approval claims.

## Live Coordination Docs

Only these docs are live coordination authority:

- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`

Study docs are useful context, but they are not branch authority. Promotion reports are valid only if they match current GitHub branch truth.

## Docs Safe To Trust For Context

- `00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md`
- `00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md`
- `00-HANDOFF/ROLE_M3_HTTP_API_PERMISSION_RUNTIME_HARNESS_STUDY.md`
- `00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md`
- `00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md`
- LAN M9-M13 docs for current read-only LAN usability boundaries

## Historical Handoffs Only

Older phase documents, master plans, RMO reconciliation studies, and pre-M8 LAN planning remain useful for intent and history, but they must not override current source, current GitHub branch truth, this current-state file, the verified candidate stack ledger, the dry-run checklist, the promotion debt meter, or the M-series completion and LAN transition gate. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or live LAN execution are not current approval.

## Must Not Start Yet

- M9 implementation.
- LAN-M14 or any live LAN runtime expansion.
- LAN completion series.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation.
- Role editor/custom permission manager.
- Settings, Backup, Official Output, or Reports implementation.
- RMO, official-output, printer, PDF, Crystal, or ESC/POS work.

## Recommended Next Planning Target

After M8 receives Codex verification, Kirch can run M-series closure review or final M-series promotion planning only if Kirch says the M-series is complete. LAN completion work must not begin before M-series promotion.
