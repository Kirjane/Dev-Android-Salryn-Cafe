# POS-T1 Full Store Runtime Torture Report

## Candidate

- Branch: `KIRCH-POS-T1-FULL-STORE-RUNTIME-TORTURE`
- Base: `KIRCH-POS-B1-BACKUP-RESTORE-SAFETY`
- Base SHA: `9efcc865c64d5e1f0b11fd841269b1051f0df371`
- Final SHA: recorded after the final commit

## Implementation

POS-T1 adds an executable CoreSmoke workflow, `pos-t1-full-store-runtime-torture`, against an isolated temporary SQLite store. It chains real bootstrap and authentication, shift opening, repeated-quantity checkout, Fast Add, Stock-In, adjustment, a second sale, disabled-printer reprint, void/reversal, settings changes, another sale, owner reporting, and controlled backup validation.

The check asserts exact sale, payment, movement, and stock outcomes. It also proves receipt snapshot immutability, new-receipt settings consumption, void exclusion from active report totals, disabled reprint side-effect safety, controlled backup side-effect safety, and SHA-256 tamper rejection.

## Boundaries

- No production code was changed.
- No physical scanner or printer was exercised.
- No manual UI proof was performed.
- No external-drive or clean-machine backup proof was performed.
- No installer/package work, RMO/BIR/eSales work, cloud work, or branch promotion was performed.

## Verification

Local automated results on 2026-07-14:

- `dotnet build Salryn.sln`: PASS, 0 warnings, 0 errors on the final run.
- Full CoreSmoke: PASS.
- Focused `pos-t1-full-store-runtime-torture`: PASS.
- Focused B1, S1, R1, SURFACE1, M7, M6, M5, M4, M3, M2, and M1 gates: PASS.
- `npm --prefix src/Salryn.App.Web run build`: PASS; Vite reports the existing chunk-size advisory.
- `git diff --check`: PASS.
- Final clean worktree and protected-ref checks are recorded after commit.

The initial compile/run cycle exposed two test-only integration mistakes: database initialization returns `void`, and role permissions are stored directly in `role_permissions.permission`. Both were corrected in the T1 check before the complete gate block was run.

Manual store-day, visual, hardware, external-drive, and clean-machine proof remain deferred to Kirch. POS-I1 was not started.
