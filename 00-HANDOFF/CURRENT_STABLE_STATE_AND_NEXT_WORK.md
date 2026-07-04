# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate SHA before LAN-M15-C: `8f325acf1cb001e9a16955058730008aa2eee629`
- Ahead/behind before LAN-M15-C: `4 ahead / 0 behind`
- Status: Sandbox remains stable at `e477699dca27a21954a5ad9d388307fdb9ed08fd`. no Sandbox promotion has occurred.
- Sandbox remains `e477699dca27a21954a5ad9d388307fdb9ed08fd`.
- Freehand contains completed unpromoted LAN-M14-F through LAN-M14-I.
- Freehand contains completed unpromoted LAN-M15-A through LAN-M15-F.
- LAN-M14-I remains the LAN-M14 cap.
- LAN-M15-F is the LAN completion cap.
- Next step is one Codex verification of the full stacked candidate.
- No promotion has occurred.

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
- `LAN-M15-A` - Remote Read-Only LAN Field Hardening and Operator Diagnostics
- `LAN-M15-B` - Remote Read-Only LAN Preflight and Operator Troubleshooting Flow
- `LAN-M15-C` - Local-Only LAN Control Panel and Terminal Trust Management
- `LAN-M15-D` - Remote Client Resilience and Session Lifecycle Hardening
- `LAN-M15-E` - Abuse / Flood / Negative-Path Hardening
- `LAN-M15-F` - Final Field Acceptance Closure and Codex Review Bundle

LAN-M14-F through LAN-M14-I remain completed but unpromoted on Freehand.

LAN-M14-F through LAN-M14-I are completed candidate work on Freehand, still unpromoted.

LAN-M14-F through LAN-M14-I are candidate work completed on Freehand pending Codex verification and maintainer review.

LAN-M15-A remains completed but unpromoted on Freehand.

LAN-M15-B remains completed but unpromoted on Freehand.

LAN-M15-C remains completed but unpromoted on Freehand.

LAN-M15-D remains completed but unpromoted on Freehand.

LAN-M15-E remains completed but unpromoted on Freehand.

LAN-M15-F remains completed but unpromoted on Freehand.

LAN-M15-A through LAN-M15-F are completed candidate work on Freehand, still unpromoted.

LAN-M14-I remains the LAN-M14 cap.

LAN-M14-I is the LAN-M14 cap.

LAN-M14-J does not exist and must not be created.

LAN-M14-J is not started and must not exist.

Hybrid bridge is implemented on Freehand.

explicit operator enable is required before Api.Local adds a LAN listen URL.

## Current Candidate Work

LAN-M15-C through LAN-M15-F are completed candidate work on Freehand and are the final LAN completion sprint.

- LAN-M15-C added a local-only operator LAN control panel and terminal trust management surface under `/api/lan-control`, guarded to the localhost operator only.
- LAN-M15-D added remote read-only client resilience and session lifecycle hardening (expired/disabled/revoked/stale messaging, last successful read timestamp, safe retry guidance) with no retry storm and no write path.
- LAN-M15-E added abuse/flood/negative-path hardening (in-memory soft deny throttling, oversized/malformed header rejection, hostile-string diagnostics redaction) with no schema and no new persistent storage.
- LAN-M15-F is the final field acceptance closure and Codex review bundle.

LAN-M15-F is the LAN completion cap.

LAN-M15-G does not exist and must not be created unless a hard blocker proves LAN-M15-F cannot close the work.

Next step is one Codex verification of the full stacked candidate.

## LAN-M16 Host-Owned Multi-Terminal Role Runtime Candidate

- Sandbox remains `e477699dca27a21954a5ad9d388307fdb9ed08fd`.
- Freehand remains unpromoted.
- LAN-M14-F through LAN-M14-I remain unpromoted foundation work.
- LAN-M15-A through LAN-M15-F remain unpromoted read-only LAN hardening work.
- LAN-M16 is current host-owned multi-terminal role runtime candidate.
- LAN-M16 adds actual remote cashier/manager/admin POS operation against host DB.
- LAN-M16 does not use shared SQLite.
- LAN-M16 does not imply sync between two independent hosts.
- LAN-M16 keeps LAN bind explicit and host-controlled.
- LAN-M16 adds a separate host-owned `/api/remote-pos` command surface; the `/api/remote-readonly` bridge remains read-only and GET-only.
- LAN-M16 remote POS runtime is disabled by default and resets on host restart.
- No promotion has occurred.

## Current LAN Boundary

- Hybrid bridge remains implemented but disabled by default.
- Api.Local defaults to `127.0.0.1:5299`; explicit LAN IP bind is optional only after operator enable.
- Wildcard bind (`0.0.0.0`) remains forbidden.
- Remote-readonly endpoints remain GET-only under `/api/remote-readonly`.
- Bridge reads require terminal identity and handshake headers.
- Localhost operator cookies are not remote LAN trust.
- Read-attempt audit records allow/deny without secret logging.
- Operator diagnostics and preflight are read-only and safe for field troubleshooting.
- Local LAN control (`/api/lan-control`) is operator-only on the POS host and never reachable by remote bridge clients.
- Remote clients cannot enable/disable LAN, configure the bind address, or pair/disable/revoke terminals.
- Remote read-only client resilience surfaces expired/disabled/revoked/stale session states with safe retry guidance and no retry storm.
- Repeated denied remote reads are soft-throttled in memory with no schema, and oversized/malformed headers are rejected fail-closed.
- No default LAN bind.
- No mutation.
- No checkout.
- No remote cashier/admin execution.
- No official journal writes.
- No official sequence allocation.
- QR, discovery, mobile, cloud, and network scan behavior are not started.
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
- `00-HANDOFF/LAN_M15_B_REMOTE_READONLY_LAN_PREFLIGHT_AND_OPERATOR_TROUBLESHOOTING.md`
- `00-HANDOFF/LAN_M15_C_LOCAL_ONLY_LAN_CONTROL_PANEL_AND_TERMINAL_TRUST_MANAGEMENT.md`
- `00-HANDOFF/LAN_M15_D_REMOTE_CLIENT_RESILIENCE_AND_SESSION_LIFECYCLE_HARDENING.md`
- `00-HANDOFF/LAN_M15_E_ABUSE_FLOOD_NEGATIVE_PATH_HARDENING.md`
- `00-HANDOFF/LAN_M15_F_FINAL_FIELD_ACCEPTANCE_CLOSURE_AND_CODEX_REVIEW_BUNDLE.md`
- `00-HANDOFF/LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md`

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
- LAN M9-M15-F docs for current read-only LAN usability and post-promotion cutline boundaries

## Historical Handoffs Only

Older phase documents, master plans, RMO reconciliation studies, pre-promotion M-series handoffs, and pre-LAN-M14-I LAN plans remain useful for intent and history, but they must not override current source, current GitHub branch truth, this current-state file, or the LAN-M14-I end-to-end closure boundary. Old references to future mobile, cloud, Crystal, PDF, printer, broader RMO work, or default LAN enablement are not current approval.

## Must Not Start Yet

- LAN-M14-J or any post-LAN-M14-I expansion under the LAN-M14 name.
- LAN-M15-G, unless a hard blocker proves LAN-M15-F cannot close the work.
- Sandbox promotion of LAN-M14-F through LAN-M14-I or LAN-M15-A through LAN-M15-F until maintainer review authorizes it.
- Default LAN bind enablement.
- LAN mutation.
- Remote cashier/admin execution.
- Shared SQLite over LAN.
- POS checkout implementation over LAN.
- Role editor/custom permission manager.
- Settings, Backup, Official Output, or Reports implementation.
- RMO, official-output, printer, PDF, Crystal, or ESC/POS work.
- QR, discovery, mobile, cloud, or network scan behavior.

## Recommended Next Planning Target

LAN-M15-C through LAN-M15-F are complete on Freehand. Next step is one Codex verification of the full stacked candidate (LAN-M14-F through LAN-M14-I and LAN-M15-A through LAN-M15-F), then stop for maintainer review. LAN-M14-I remains the LAN-M14 cap and LAN-M15-F is the LAN completion cap; neither authorizes LAN-M14-J, LAN-M15-G, default LAN enablement, mutation, checkout, or post-cap LAN expansion.
