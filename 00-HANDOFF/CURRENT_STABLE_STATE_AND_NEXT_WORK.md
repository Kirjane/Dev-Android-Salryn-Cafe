# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `bde69e28faa4f92dd7b6765da9f45a0c5cf02056`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate SHA before LAN-M14-A: `bde69e28faa4f92dd7b6765da9f45a0c5cf02056`
- Ahead/behind before LAN-M14-A: `0 ahead / 0 behind`
- Status: post-M-series promotion; Sandbox and Freehand are aligned.

## Stable Sandbox Stack

Stable as of `bde69e28faa4f92dd7b6765da9f45a0c5cf02056`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness
- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter
- `M8` - M-Series Completion Criteria + LAN Transition Gate

M-series is stable on Sandbox after promotion. Sandbox and Freehand are aligned at `0 ahead / 0 behind`.

## Current Candidate Work

LAN-M14-A is current candidate work and is not verified until Codex verifies it.

LAN-M14-A adds a post-promotion LAN source stamp, current LAN baseline inventory, and first implementation cutline recommendation. It is study/cutline work only.

LAN completion series is now eligible for planning from fresh source truth, but LAN-M14 is not implemented yet.

Recommended first implementation-planning target after Codex verifies LAN-M14-A: `LAN-M14-B` - Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline.

## Current LAN Boundary

- LAN-M14-B is not started.
- LAN bind activation is not started.
- Live remote transport dispatch is not started.
- LAN mutation is not started.
- Remote cashier/admin behavior is not started.
- QR, discovery, mobile, and cloud behavior are not started.
- RMO, official-output, printer, PDF, Crystal, and ESC/POS work remain deferred.
- Certification, accreditation, legal-compliance, and production-approval claims remain forbidden.

## Live Coordination Docs

Only these docs are live coordination authority for the current post-promotion state:

- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md`

M-series docs remain useful historical context for what was promoted, but they are no longer the active current-candidate authority.

## Docs Safe To Trust For Context

- `00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`
- `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md`
- `00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md`
- `00-HANDOFF/ROLE_M3_HTTP_API_PERMISSION_RUNTIME_HARNESS_STUDY.md`
- `00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md`
- `00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md`
- LAN M9-M13 docs for current read-only LAN usability boundaries

## Historical Handoffs Only

Older phase documents, master plans, RMO reconciliation studies, pre-promotion M-series handoffs, and pre-M14 LAN plans remain useful for intent and history, but they must not override current source, current GitHub branch truth, this current-state file, or the LAN-M14-A post-promotion source stamp and cutline. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or live LAN execution are not current approval.

## Must Not Start Yet

- LAN-M14-B implementation.
- Any live LAN runtime expansion.
- LAN bind activation.
- Live remote transport dispatch.
- LAN mutation.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation.
- Role editor/custom permission manager.
- Settings, Backup, Official Output, or Reports implementation.
- RMO, official-output, printer, PDF, Crystal, or ESC/POS work.
- QR, discovery, mobile, or cloud behavior.

## Recommended Next Planning Target

After LAN-M14-A receives Codex verification, the safest next target is `LAN-M14-B` planning or implementation handoff focused on explicit host bind configuration readiness and guarded runtime cutline. Do not start LAN-M14-B until LAN-M14-A is verified.
