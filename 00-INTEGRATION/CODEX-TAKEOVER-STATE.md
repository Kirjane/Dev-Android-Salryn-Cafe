# Codex takeover state / decision ledger

Authority: Kirch Ivan Balite, Issue #27 comment `7048261423` (2026-11-21).
Role: primary Maintainer; bounded preservation/harness writer. No live Swarm.

## Latest checkpoint (2026-11-21)

Candidate source at `59ebdcde85653307ec5aa99ff94684ec69d33382` now contains
history-preserving merges of PR24/I1, PR25/I3, PR23/I6A and repaired PR26/I7.
No remote writes or main promotion have occurred during takeover.

Windows scoped runner:
`pwsh -NoProfile -File scripts/verify-candidate.ps1 -Mode Windows` on exact
`59ebdcde` completed **FAIL, exit 1**, clean initial/final source identity.
Artifacts: `artifacts/verification/f397257291fc40ba953bd644fbf55e35/` includes
machine JSON, raw logs, command/environment identities, TRX and Vitest JSON.
Solution build succeeded; all five xUnit suites passed (31+9+17+13+8 = 78 tests),
both frontend suites/builds passed (**MOCKED_UI**; counts in machine report).
CoreSmoke failed at inherited `LanM8ReactRemoteReadOnlyShellRuntimeChecks.cs:53`,
which requires obsolete literal `http://127.0.0.1:5299` in API source. It has not
proved a live Cafe defect. Bounded test-only repair is assigned in new isolated
`codex-legacy-qa`, branch `codex/cafe-legacy-verification`, base `59ebdcde`;
owned paths `tests/Salryn.Tests/**`, `tools/Salryn.Tools.CoreSmoke/**`, 3-cycle cap.
No assertion removal/weakening or production edits authorized in that lane.

Independent reviews and completed repairs:

- Adapter `bf28d49f` + journal test delta `cc3d0d63`: independent scoped PASS;
  6 adapter tests independently rerun. Added populated event corruption rejection.
- Integration `1a35431e` wired canonical Cafe catalog and pending semantics and
  added silent-install PrepareToInstall guard. Review found status/bootstrap WAL
  mutation and non-resumable interrupted fresh installs; repaired in `c6e9c057`.
- Reviewer then reproduced hot rollback journal recovery before lineage rejection.
  `a0f476a2e9b791188f5cdd8d589845edfe2371dd` now performs read-only preflight
  before writable open, and validates again before WAL. Independent new-fixture
  proof: DB bytes and journal bytes unchanged; journal retained; SQLite Error8
  rejection; shell exit0; scoped PASS. Script is in restricted recovery root as
  `a0f476a2-hotjournal-proof.ps1`. I2 now has 9 passing tests including atomic fresh
  rollback/retry and unknown-db byte parity. Earlier mixed-source combined run
  was diagnostic only; later exact-SHA runs supersede it.
- CI worker `9b351f72` repaired required suite inventories, source binding,
  platform modes/counts/raw logs/run-produced APK provenance. Review found its
  expected-negative tests leaked LASTEXITCODE=1. Final `fc0998b5` adds explicit
  success exit; independent workflow-equivalent wrapper passed 39 assertions,
  exit0 (**MOCKED_RUNNER**, not application or live GitHub CI proof). Merged into
  candidate as `59ebdcde`.

Android branch: original source checkpoint full SHA
`0438c7244abef1e0b670f7cfb1b0b342fb0eaf1c`; reviewed candidate `59ebdcde` merged
into it as `26bf422651610ccceae13aebfdbbe292ef3239f1` without conflict. Temporary
Android specialist owns only App.Android/Mobile.Foundation/Mobile.Runtime/I6BTests
for a bounded 3-cycle compile/test/correctness slice. Preview UI, self-asserted
lease issuer key, constant boot count and seeded production menu are identified
as unfinished/unaccepted; no APK/full-cashier acceptance claim exists.

Actual portable factory journal initialized at ignored `.forge/state/journal.db`;
`HOST-COMPOSITION-01` packet registered PLANNED, hash chain verified and SQLite
online backup saved. Original persona strings are only schema vocabulary, not
reactivated workers. The next host mission is not accepted or completed.

Immediate next: close CoreSmoke regression, then host composition/atomic mutation
idempotency/RBAC/pairing, native Android completion, live UI and full C1–C11 proof
coverage. Root now pins imported skill/vendor checkout bytes with `.gitattributes`
because Windows autocrlf otherwise invalidates the exact-byte manifest in a new
checkout. Validate the adapter from a fresh disposable checkout after this doc
change. Native hook trust/execution and effective host cap remain NOT RUN.

## Observed starting state

- Candidate `b803910502dee3b1e5fc7c1b15603427d8e3c9b6`, clean at intake,
  branch `forge/candidate-salryn-cafe`, correct private destination remote.
- Remote main `16ce5f2947f44d3b571147d69a599c5f3dc94517`; local main
  `a85fb2ec92651e3255f901acae6221928098b764`. Neither changed by takeover.
- All 12 existing worktrees preserved. No reset/clean/stash/rebase/force push.
- Android original base `30346b506ce2cf261514fec7bd8806b421c3107d` had
  staged/unstaged/untracked WIP. Preserved binary HEAD diff and index diff plus
  20 source files with byte/hash parity. Local source-only preservation commit
  `0438c724` on existing `forge/i6b-android-cashier`; unbuilt/unreviewed, not RC.
- Restricted recovery root `C:\Users\arane\SalrynForgeRecovery\20261121-takeover`
  holds Issue #27 body/all five comments, complete 138-record board, members,
  swarm metadata, original harness archive/Git copies, installer and logs.
  No raw transcripts are committed. Correct board path includes
  `brain/d38d4b68-5298-4c19-9b3d-b45dd68b8083/.system_generated/swarm`.
- Preserved I1 installer hash
  `58d745bba912278be82bb37d069f16402bd8b1506aaa1c1886ccfcf895229268`;
  explicitly NOT RC. Historical safe-exit checkpoint predates Wave 2.

## Evidence and decisions

- Board 1–51: roll call/unrelated product deliberations; archival, not Cafe scope.
- Board 52–65: Yza audit. Retail tender expansion/pruning/rebase proposals
  superseded by Cafe Cash/AR-only policy and preserved-history boundaries.
- Board 66–82: authority, contracts and six adversarial spec resolutions.
  Locked: HTTPS 5400, loopback 5399, v40 lineage/parity, v41 unified idempotency,
  device/operator separation, revoked quarantine and single offline cashier.
- Board 83–138 and Issue #27: completed component reviews are REPORTED historical
  evidence at their source SHAs, not newly observed combined acceptance.
- Original harness source pin matches and is clean. Portable qualification and
  adapter limits: `CODEX-FORGE-ADAPTER.md`.
- Toolchains observed: .NET 10.0.404, android 36.1.118, JDK 17.0.21.1,
  Node 24.22.1/npm 11.10.1, Python 3.13.5, Codex CLI 0.164.0.
  Dot-source local `SalrynForgeTools/forge-env.ps1` per PowerShell session.
- Independent temporary reviewer `review_ci_gate`: PR #26 at exact
  `93761db744cbc56bf8330f7c271671263ede3225` **REWORK** (source review and
  in-memory aggregation reproduction; application builds/tests NOT RUN).
  P1: missing-suite overall PASS; zero-test Android; WindowsDesktop CoreSmoke
  on Linux; no clean/final SHA binding. P2: synthetic counts/stale artifact
  manifest and discarded raw logs. Do not trust this gate before repair.

## Remaining ordered execution

1. Finish independent adapter review/qualification; keep real hook trust gap
   explicit and continue manual policy under takeover authority.
2. Preserve reviewed exact heads and PR receipts. Integrate I1 PR24 `c78e4d94`
   then I3 PR25 `d5d63c87`, I6A PR23 `50e3f442`, repaired I7 PR26 `93761db7`
   onto candidate only; test combined result. No main promotion.
3. Fix silent installer protected-root guard, missing-DB pending semantics,
   singleton idempotency/no auth-error caching plus atomic crash/restart proof.
   Wire canonical Cafe migrations, domain endpoints/authorization, durable
   pairing/HTTPS and shared sync contracts. No duplicate ledger authority.
4. Finish Android from preserved WIP with security delta, actual cashier journeys,
   APK/emulator/simulated printer tests. Finish host outbox/pull and unified live
   frontend/router, role/pairing/unknown-outcome handling and accessibility.
5. Run exact-candidate nonzero C1–C11/F1A–E tests, live Playwright, migration,
   restore/replay/partition/security and packaging; independent review/repair.
6. Freeze RC, installer/APK/central templates, SHA-256 manifest, rollback and
   human checklist. Physical BT/tablet/coexistence/signing remain HUMAN_REQUIRED
   until observed. No complete release-candidate claim yet.
