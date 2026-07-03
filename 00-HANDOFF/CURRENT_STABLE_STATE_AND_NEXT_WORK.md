# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate SHA before LAN-M15-A: `22052d4a9b8481d14988f7749bd66e5ed65951ff`
- Ahead/behind before LAN-M15-A: `2 ahead / 0 behind`
- Status: Sandbox remains stable at `e477699dca27a21954a5ad9d388307fdb9ed08fd`. no Sandbox promotion has occurred.

## Stable Sandbox Stack

Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness
- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter
- `M8` - M-Series Completion Criteria + LAN Transition Gate
- `LAN-M14-A` - Post-Promotion LAN Source Stamp + Completion Scope Audit / First Cutline
- `LAN-M14-B` - Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline
- `LAN-M14-C` - Remote Read-Only Client Session Bridge Cutline
- `LAN-M14-D` - Bridge Session Contract Study
- `LAN-M14-E` - Audit and Read-Attempt Policy Cutline

LAN-M14-A through LAN-M14-E are stable on Sandbox.

LAN-M14-D is stable.

LAN-M14-E is stable.

## Unpromoted Freehand Candidate Stack

Completed on Freehand, still unpromoted:

- `LAN-M14-F` - Host Bind Config Seam Implementation
- `LAN-M14-G` - Remote Read-Only Bridge Runtime
- `LAN-M14-H` - Pairing / Operator UX Surface
- `LAN-M14-I` - End-to-End LAN Read-Only Closure

LAN-M14-F through LAN-M14-I are completed candidate work on Freehand, still unpromoted.

LAN-M14-F through LAN-M14-I are candidate work completed on Freehand pending Codex verification and maintainer review.

LAN-M14-I remains the LAN-M14 cap.

LAN-M14-I is the LAN-M14 cap.

LAN-M14-J does not exist and must not be created.

LAN-M14-J is not started and must not exist.

Hybrid bridge is implemented on Freehand.

explicit operator enable is required before Api.Local adds a LAN listen URL.

## Current Candidate Work

LAN-M15-A is current candidate work on Freehand.

LAN-M15-A hardens remote read-only LAN diagnostics, operator status visibility, and fail-closed bridge denial messaging on top of the unpromoted LAN-M14-F through LAN-M14-I stack.

## Current LAN Boundary

- Hybrid bridge remains implemented but disabled by default.
- Api.Local defaults to `127.0.0.1:5299`; explicit LAN IP bind is optional only after operator enable.
- Wildcard bind (`0.0.0.0`) remains forbidden.
- Remote-readonly endpoints remain GET-only under `/api/remote-readonly`.
- Bridge reads require terminal identity and handshake headers.
- Localhost operator cookies are not remote LAN trust.
- Read-attempt audit records allow/deny without secret logging.
- Operator diagnostics are read-only and safe for field troubleshooting.
- No default LAN bind.
- No mutation.
- No checkout.
- No remote cashier/admin execution.
- No official journal writes.
- No official sequence allocation.
- QR, discovery, mobile, and cloud behavior are not started.
- RMO, official-output, printer, PDF, Crystal, and ESC/POS work remain deferred.
- Certification, accreditation, legal-compliance, and production-approval claims remain forbidden.
- Deferred unsafe work remains deferred.

## Live Coordination Docs

Only these docs are live coordination authority for the current post-LAN-M14-I state:

- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md`
- `00-HANDOFF/LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md`
- `00-HANDOFF/LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md`
- `00-HANDOFF/LAN_M14_D_BRIDGE_SESSION_CONTRACT_STUDY.md`
- `00-HANDOFF/LAN_M14_E_AUDIT_READ_ATTEMPT_POLICY_CUTLINE.md`
- `00-HANDOFF/LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md`
- `00-HANDOFF/LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md`
- `00-HANDOFF/LAN_M14_H_PAIRING_OPERATOR_UX_SURFACE.md`
- `00-HANDOFF/LAN_M14_I_END_TO_END_LAN_READONLY_CLOSURE.md`
- `00-HANDOFF/LAN_M15_A_REMOTE_READONLY_LAN_FIELD_HARDENING_AND_OPERATOR_DIAGNOSTICS.md`

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
- LAN M9-M15-A docs for current read-only LAN usability and post-promotion cutline boundaries

## Historical Handoffs Only

Older phase documents, master plans, RMO reconciliation studies, pre-promotion M-series handoffs, and pre-LAN-M14-I LAN plans remain useful for intent and history, but they must not override current source, current GitHub branch truth, this current-state file, or the LAN-M14-I end-to-end closure boundary. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or default LAN enablement are not current approval.

## Must Not Start Yet

- LAN-M14-J or any post-LAN-M14-I expansion under the LAN-M14 name.
- Sandbox promotion of LAN-M14-F through LAN-M14-I until maintainer review authorizes it.
- Default LAN bind enablement.
- LAN mutation.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation over LAN.
- Role editor/custom permission manager.
- Settings, Backup, Official Output, or Reports implementation.
- RMO, official-output, printer, PDF, Crystal, or ESC/POS work.
- QR, discovery, mobile, or cloud behavior.

## Recommended Next Planning Target

After LAN-M15-A receives Codex verification, stop for maintainer review. LAN-M14-I remains the LAN-M14 cap and does not authorize LAN-M14-J, default LAN enablement, mutation, checkout, or post-cap LAN expansion.
