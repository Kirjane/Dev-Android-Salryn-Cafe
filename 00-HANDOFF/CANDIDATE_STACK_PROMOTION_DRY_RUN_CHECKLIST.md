# Candidate Stack Promotion Dry-Run Checklist

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Sandbox SHA: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Candidate branch: `KIRCH-FREEHAND`
- Freehand SHA before M6: `b9edccddbd1d5754c4dc05ac79554591aa31905e`
- Remote M6 candidate SHA before local Codex verification: `d01f9b225895080d5bdf0aa52f45c54658943d34`
- Freehand SHA before M8: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Ahead/behind before M8: `26 ahead / 0 behind` from `KIRCH-SANDBOX`
- Promotion status: parked until full M-series completion

## Stable Sandbox Stack

Stable Sandbox stack only:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness

## Verified Candidate Stack on Freehand

Verified candidate stack on `KIRCH-FREEHAND`, not stable until promotion:

- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter

## Current M8 Candidate

- M8 is current candidate work and unverified until Codex verifies it.
- M9 is not started.
- LAN-M14 is not started.
- LAN completion series is not started.

## Candidate Stack Files

Current candidate-stack files since Sandbox before M8:

- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md`
- `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`
- `tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs`
- `tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs`
- `tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs`
- `tests/Salryn.Tests/RoleM4PermissionBoundaryRegressionMatrixChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

M8 files after this sprint:

- `00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`
- `tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs`

M8 also updates existing live coordination files:

- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`
- `00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`
- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Future Promotion Preconditions

Future promotion must not be attempted unless all conditions below are true:

- Kirch explicitly says promote after declaring the M-series complete.
- `KIRCH-SANDBOX` SHA matches the expected source stamp at promotion time.
- `KIRCH-FREEHAND` SHA matches the expected source stamp at promotion time.
- Freehand must be ahead-only and not behind before future promotion.
- The worktree is clean.
- build/test/CoreSmoke/InfrastructureSmoke/npm all pass.
- no schema/seed/RMO/printer/LAN mutation/feature-expansion drift is allowed.
- No schema or migration drift exists.
- No seed data drift exists.
- No RMO, official output, PDF, Crystal, ESC/POS, printer, or receipt drift exists.
- No LAN bind activation, live remote transport dispatch, LAN mutation, QR, discovery, mobile, or cloud drift exists.
- No role editor, custom permission editor, POS checkout, Settings, Backup, Official Output, Reports, or disabled future Web-screen implementation drift exists.
- Final promotion dry-run must check `M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`.
- Future promotion planning should review `CANDIDATE_STACK_PROMOTION_DEBT_METER.md`.
- HIGH or STOP / REVIEW conditions must be resolved before feature sprinting continues.
- LAN transition cannot begin before promoted M-series baseline exists.
- Stale LAN assumptions must be rejected.
- Promotion still requires explicit Kirch command.
- The final promotion handoff is generated from current GitHub truth, not stale chat text.

## Dry-Run Replay Commands

Before any future promotion, Codex must replay the full verification set from a clean local repo:

```powershell
git fetch origin KIRCH-SANDBOX KIRCH-FREEHAND --prune
git switch KIRCH-FREEHAND
git pull --ff-only origin KIRCH-FREEHAND
git status --short
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-FREEHAND
git rev-list --left-right --count origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND
git diff --check

dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build

dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj

dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-phase-b-regression
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- official-output-text-file
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-checkout-application-repository
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- rmo-engineering-candidate
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- host-command-loopback
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- terminal-registry-pairing
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- controlled-remote-transport-gate
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pairing-activation-handshake
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- safe-remote-readonly-client
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-web-shell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-usability

cd .\src\Salryn.App.Web
npm install
npm run build
```

## Squash Promotion Dry-Run Checks

Before any future squash promotion:

- Compare `KIRCH-SANDBOX...KIRCH-FREEHAND` from GitHub, not memory.
- Confirm the candidate stack file manifest still matches the diff.
- Confirm every changed file belongs to an approved candidate sprint.
- Confirm no forbidden path category appears in the diff.
- Confirm `M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md` says M-series completion criteria are met.
- Confirm the squashed promotion message names the verified candidate stack accurately.
- Confirm live docs are updated after promotion to distinguish newly stable work from future candidate work.
- Confirm Freehand is not reset unless Kirch explicitly commands it.

## Promotion Debt Review

- Future promotion planning should review `CANDIDATE_STACK_PROMOTION_DEBT_METER.md`.
- HIGH or STOP / REVIEW conditions must be resolved before feature sprinting continues.
- Promotion still requires explicit Kirch command.

## LAN Transition Review

- Final promotion dry-run must check `M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`.
- LAN transition cannot begin before promoted M-series baseline exists.
- Stale LAN assumptions must be rejected.
- LAN completion scope must be created from fresh GitHub truth after promotion.

## Risk Notes

- The larger the ahead count, the larger the eventual squash promotion.
- Stacked candidates are acceptable only because each sprint remains narrow and Codex verifies.
- Current live coordination authority remains `CURRENT_STABLE_STATE_AND_NEXT_WORK.md`, `VERIFIED_CANDIDATE_STACK_LEDGER.md`, `CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`, `CANDIDATE_STACK_PROMOTION_DEBT_METER.md`, and `M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`.
- Historical handoffs are context only.
- Promotion reports are valid only if they match current GitHub branch truth.
- Do not claim certification, accreditation, legal compliance, production approval, or deployment readiness from this checklist.

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
