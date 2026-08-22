# Verified Candidate Stack Ledger

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Stable Sandbox SHA: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Candidate branch: `KIRCH-FREEHAND`
- Remote M5 candidate SHA before local Codex verification: `a87d1d2b47e70c3f939b7f738cf18573906cfcf4`
- Remote M6 candidate SHA before local Codex verification: `d01f9b225895080d5bdf0aa52f45c54658943d34`
- Verified Freehand candidate SHA before M8: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Ahead/behind before M8: `26 ahead / 0 behind` from `KIRCH-SANDBOX`
- Date/context: July 3, 2026; promotion is intentionally parked until full M-series completion.
- Promotion status: parked until full M-series completion

## Stable Sandbox Stack

Stable on `KIRCH-SANDBOX` only:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness

## Verified Candidate Stack on Freehand

Verified candidate work on `KIRCH-FREEHAND`, not stable until promotion:

### ROLE-M4 - permission boundary regression matrix and operator role scenario coverage

- Candidate SHA: `cacc2ca44cfddf4b067c010d73917808fbfdaa7f`
- Status: verified candidate on Freehand
- Verification: verified by Codex after Freehand implementation
- Stability status: not stable until promotion
- Unsafe scope expansion: none recorded

### M5 - current-state drift guard and verified candidate stack ledger

- Candidate SHA: `b9edccddbd1d5754c4dc05ac79554591aa31905e`
- Status: verified candidate on Freehand
- Verification: verified by Codex after Freehand implementation
- Stability status: not stable until promotion
- Unsafe scope expansion: none recorded

### M6 - candidate stack verification replay and promotion-readiness dry run

- Candidate SHA: `daccf2970c4773c8be6314febeb246bc41ccb67a`
- Status: verified candidate on Freehand
- Verification: verified by Codex after Freehand implementation
- Stability status: not stable until promotion
- Unsafe scope expansion: none recorded

### M7 - candidate stack limit guard and promotion debt meter

- Candidate SHA: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Status: verified candidate on Freehand
- Verification: verified by Codex after Freehand implementation
- Stability status: not stable until promotion
- Unsafe scope expansion: none recorded

## Current Candidate Work

- M8 - M-Series Completion Criteria + LAN Transition Gate
- M8 is current candidate work and is not verified until Codex verifies it.
- M9 is not started.
- LAN-M14 is not started.
- LAN completion series is not started.

## Candidate Stack File Manifest

Files introduced by ROLE-M4:

- `00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md`
- `tests/Salryn.Tests/RoleM4PermissionBoundaryRegressionMatrixChecks.cs`

Files introduced by M5:

- `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`
- `tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs`

Files introduced by M6:

- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs`

Files introduced by M7:

- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs`

Files introduced by M8:

- `00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`
- `tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs`

Shared live coordination / wiring files updated by candidate work:

- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Candidate Stack Limit Policy

- Freehand may remain ahead only while every sprint is narrow and Codex-verified.
- New sprints may stack only when the previous candidate sprint has GO verification.
- Do not stack unverified candidate on top of unverified candidate.
- If ahead count enters HIGH, produce promotion planning or explicit stop/review before further feature work.
- If STOP / REVIEW condition appears, stop sprinting and report.
- Promotion remains parked unless Kirch explicitly says promote.
- Sandbox remains stable branch and must not be touched.

## M-Series Completion and LAN Transition Policy

- Promotion only happens after full M-series completion.
- M-series is not complete until M8 is verified, final M-series closure review passes, Kirch explicitly declares M-series complete, and Kirch explicitly authorizes final promotion.
- LAN completion only starts after M-series promotion into `KIRCH-SANDBOX`.
- Do not use LAN docs or LAN plans before the LAN transition gate passes.
- LAN-M14 is not started.
- LAN completion series is not started.
- Do not use stale M-series docs as LAN authority.

## Promotion Parked Policy

- Promotion is intentionally deferred until full M-series completion.
- Freehand may remain ahead of Sandbox while promotion is parked.
- Do not reset Freehand unless Kirch explicitly commands it.
- Do not promote unless Kirch explicitly commands it after M-series completion criteria are met.
- Future sprints must state whether they are stacking on Freehand candidate work.
- Future sprints must fetch GitHub branch truth before giving advice or writing code.

## Risk Notes

- The longer Freehand remains ahead, the larger the eventual promotion becomes.
- Current promotion debt is HIGH, but HIGH permits only narrow M-series completion/closure discipline work while Kirch keeps promotion parked until full M-series completion.
- Feature work and LAN work are blocked while promotion debt is HIGH.
- Each sprint must stay narrow.
- Each sprint requires Codex verification.
- Stable/candidate wording must remain precise.
- Do not describe Freehand candidate work as stable until promotion actually happens.
- Do not treat Sandbox as containing parked candidate work until GitHub branch truth confirms it.

## Historical Handoff Boundary

- Older handoff docs are historical records.
- Only `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`, `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`, `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`, `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`, and `00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md` are live coordination docs.
- Study docs are useful context but not branch authority.
- Promotion reports are valid only if they match current GitHub branch truth.
- Do not follow stale reports without checking GitHub compare.

## Deferred Unsafe Work

The following remain deferred unless Kirch explicitly starts a narrow post-promotion sprint from current GitHub truth:

- M9
- LAN-M14
- LAN completion series
- LAN bind activation
- live remote transport dispatch
- LAN mutation
- remote cashier/admin behavior
- QR, discovery, mobile, or cloud behavior
- schema or migrations
- seed data changes
- role editor
- custom permission manager
- POS checkout implementation
- Settings, Backup, Official Output, or Reports implementation
- RMO, official output, PDF, Crystal, ESC/POS, printer, receipt-printing, or certification wording
