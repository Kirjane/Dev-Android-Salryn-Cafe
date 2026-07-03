# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate SHA before LAN-M14-F: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Ahead/behind before LAN-M14-F: `0 ahead / 0 behind`
- Status: Sandbox and Freehand are aligned at `e477699dca27a21954a5ad9d388307fdb9ed08fd` before LAN-M14-F through LAN-M14-I candidate work.

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

LAN-M14-A through LAN-M14-E are stable on Sandbox. LAN-M14-D is stable. LAN-M14-E is stable.

Sandbox and Freehand are aligned at `e477699dca27a21954a5ad9d388307fdb9ed08fd` before LAN-M14-F through LAN-M14-I candidate work.

## Current Candidate Work

LAN-M14-F through LAN-M14-I are candidate work completed on Freehand pending Codex verification.

LAN-M14-I is the LAN-M14 cap.

Hybrid bridge is implemented in `Salryn.Api.Local` (Api.Local HTTP facade + host command transport gate) but remains disabled by default; explicit operator enable is required before Api.Local adds a LAN listen URL.

LAN-M14-J is not started and must not exist.

## Current LAN Boundary

- Hybrid bridge is implemented but disabled by default.
- Api.Local defaults to `127.0.0.1:5299`; explicit LAN IP bind is optional only after operator enable.
- Wildcard bind (`0.0.0.0`) remains forbidden.
- Remote-readonly endpoints remain GET-only under `/api/remote-readonly`.
- Bridge reads require terminal identity and handshake headers.
- Read-attempt audit records allow/deny without secret logging.
- Pairing/status UX is read-only; no catalog mutation controls.
- LAN mutation is not started.
- Remote cashier/admin execution is not started.
- QR, discovery, mobile, and cloud behavior are not started.
- RMO, official-output, printer, PDF, Crystal, and ESC/POS work remain deferred.
- Certification, accreditation, legal-compliance, and production-approval claims remain forbidden.
- Deferred unsafe work remains deferred.

## Live Coordination Docs

Only these docs are live coordination authority for the current post-LAN-M14-E state:

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
- LAN M9-M14-I docs for current read-only LAN usability and post-promotion cutline boundaries

## Historical Handoffs Only

Older phase documents, master plans, RMO reconciliation studies, pre-promotion M-series handoffs, and pre-LAN-M14-I LAN plans remain useful for intent and history, but they must not override current source, current GitHub branch truth, this current-state file, or the LAN-M14-I end-to-end closure boundary. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or default LAN enablement are not current approval.

## Must Not Start Yet

- LAN-M14-J or any post-LAN-M14-I expansion.
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

After LAN-M14-I receives Codex verification, stop for maintainer review. LAN-M14-I does not authorize LAN-M14-J, default LAN enablement, mutation, checkout, or post-cap LAN expansion.
