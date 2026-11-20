# Codex takeover state / decision ledger

Authority: Kirch Ivan Balite, Issue #27 comment `7048261423` (2026-11-21).
Role: primary Maintainer; bounded preservation/harness writer. No live Swarm.

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
