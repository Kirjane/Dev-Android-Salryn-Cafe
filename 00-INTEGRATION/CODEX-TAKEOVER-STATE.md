# CONTINUATION CHECKPOINT — TENDER PROOFS / SHARED MOBILE PERSISTENCE

Primary local candidate d86128be27b03f31f3a6987474269cf314f00106; separate primary
implementation branch codex/cafe-mobile-shared in sibling codex-mobile-shared at
b3224236cef2baae51d2861aea5a5f17fb438e4a. Both preserved, no destructive Git.

3547018b adapts obsolete U1 GCash/Maya/Debit success expectations to locked Cafe
rejection plus unchanged balance/rows. Reviewer requested preservation of altered
payload replay conflict. Retained AR52-11 already covers Cash mismatch with snapshot
parity; d86128be additionally adds mismatch/row/balance proof directly in U1 helper
and adapts AR52-07 noncash-success case to rejection/no mutation, removing only its
now-unneeded compensating adjustment. CoreSmoke built/runs these successfully but
FAILS later OwnerAnalyticsW0W3RuntimeChecks.cs:141: fixture inserts Cash collection
without collector open cashier session, which canonical Cafe trigger correctly
rejects. Do not remove trigger or activate noncash to pass. Repair fixture with
legitimate session attribution/cash facts, retaining aging/read-only assertions.
Log cafe-cash-replay-coresmoke.log retained. Exact full CoreSmoke still FAIL.

Shared mobile extraction b3224236: recovered original portable Persistence project
and friend declarations; current Data/DbPaths/ProductIdentity compiled once there,
Infrastructure excludes and references Persistence. Existing source paths/namespaces
and Cafe current migrations/financial code unchanged. Restored original
MobileProductionComposition over current C1–C11 stores with app-private Cafe paths,
read-only Cafe lineage/readiness, required v23+v41 tables/immutable triggers and
rejection of p0/mobile_* duplicate authority. Runtime has no Infrastructure/PDF/
ASP.NET/Desktop reference. Solution includes Persistence/Runtime/Sync.Contracts.
.gitignore exception prevents portable project directory being mistaken for *.sqlite.
Exact b3224236: dotnet test tests/Salryn.Cafe.I2Tests/Salryn.Cafe.I2Tests.csproj -c Release
-m:1 --no-restore --logger trx --results-directory <recovery>/test-results =>17 PASS,
exit0, Windows/.NET10.0.404; b3224236-mobile-persistence.log and matching TRX.
Infrastructure and Runtime individual builds PASS (existing nullable warnings).
Independent temporary review_shared_mobile inspecting frozen b3224236; verdict
pending, not integrated/published. Native Android adaptation/APK boundary still NOT RUN.
Next: close independent review findings, integrate extraction only after clearance,
reconcile preserved Android1f34b694 with canonical composition; do not retain duplicate
mobile business ledger. Mobile and sync proof recovery sequence below still applies.

---

# BACKUP CORRECTION SLICE CHECKPOINT — 2026-11-21

Latest tested source7ec74074 (git rev-parse for full SHA). Clean source at test.
Portable correction1b0866f7 verified with true export/import roundtrip; explicit
wrong-product legacy import negative regression added and passed. Restore now
read-only admits destination, snapshots source via SQLite BackupDatabase (includes
committed WAL), upgrades/reverifies staged copy to41 and holds Windows FileShare.Delete
exclusion through sidecar cleanup and atomic File.Replace. Original source and
safety backup retained. 16 I2 tests PASS, exit0, Windows/.NET10.0.404:
`dotnet test tests/Salryn.Cafe.I2Tests/Salryn.Cafe.I2Tests.csproj -c Release -m:1
--no-restore --logger trx --results-directory <recovery>/test-results`.
Log backup-final-regressions.log; TRX backup-final-regressions.trx. Earlier12/14/15
passing runs preserved. Independent backup_review_resume scoped PASS at53af9ae6;
last7ec74074 test-only addition has not been independently rereviewed. Concurrent
writer/interrupted atomic swap and Linux restore execution remain NOT RUN.

Full CoreSmoke post-restore run FAIL: progressed beyond previous backup failure,
now U1AccountsReceivableRuntimeChecks.cs:800 expects GCash missing-reference error;
Cafe locked tender policy rejects GCash outright. Log post-restore-coresmoke.log.
Do not activate noncash tenders to satisfy inherited retail expectations; port
those assertions to Cafe rejection + zero persisted mutation proof, retaining
all Cash/AR invariants. Full suite not green. No release candidate acceptance.

Next: bounded independent test-only delta review, adapt obsolete tender checks,
continue shared Persistence/mobile extraction and semantic Central.Sqlite proof
reconciliation per CODEX-MOBILE-CONTINUATION, then host atomicity/RBAC/pairing,
live frontend and Android completion. Existing worktrees/harness/source retained.

---

# RESUMED — EXPLICIT INTERRUPTED-CHECKPOINT RECOVERY (2026-11-21)

User explicitly authorized CONTINUE; the historical safe-exit pause below is
superseded for execution only. Recovery verified clean local182a51fe, remote
candidate ae169420, Android1f34b694 and unchanged remote main16ce5f29. Current
GitHub actor observed Kirch-Nairu. Original harness and completed work retained;
no repeated adoption/discovery. Existing journal hash chain verified and
HOST-COMPOSITION-02 transitioned BLOCKED→ACTIVE under continuation authority.

New source43387b9c43ab8cf8fcb1daf173adeb04d6a45e01 binds controlled manifests to
product_id=Salryn.Cafe, rejects wrong product, and accepts absent ID only when
read-only classifier confirms legacy Yza. Focused I2 run on this source: 11 passed,
0 failed/skipped, exit0, Windows/.NET10.0.404; command dotnet test
 tests/Salryn.Cafe.I2Tests/Salryn.Cafe.I2Tests.csproj -c Release -m:1 --no-restore
 --logger trx --results-directory <restricted recovery>/test-results.
Actual log resume-product-binding.log and TRX resume-product-binding.trx retained.
Independent bounded reviewer backup_review_resume inspecting frozen43387b9c and
previous6770e5f9; verdict pending. No remote publication of unreviewed source.

Next: stage/migrate/verify legacy backup copy before replacing live destination,
protect live destination against wrong-product/hot-journal writes, close review
findings, focused regressions then full CoreSmoke; remaining dependency sequence
below unchanged. Product-bound manifest does not complete restore hardening.

---

# SAFE EXIT — ACCOUNT TRANSITION (2026-11-21, Asia/Manila)

**Execution PAUSED by Technical Authority. Do not auto-resume until the next
explicit authorized continuation. No release candidate readiness claim.**

- Exact local candidate source/integration SHA at safe-exit entry:
  `ad2ab978ffc837d4d92a76b4fb17b7a149646fdb` on `forge/candidate-salryn-cafe`.
  The later commit containing this checkpoint changes documentation only; use
  `git rev-parse HEAD` for its exact checkpoint SHA (also in the external receipt
  and published documentation checkpoint branch).
- Verified remote candidate: `ae169420e590a729ccabd8f07518d0050bbd1260`.
  Published only through independently reviewed source. Local `6770e5f9` backup
  repair and its following legacy merge remain ahead locally, not accepted or
  published as a validated candidate. No uncommitted source at pause entry.
- `main` unchanged: local `a85fb2ec92651e3255f901acae6221928098b764`, remote
  `16ce5f2947f44d3b571147d69a599c5f3dc94517`. No production deployment.
- Authenticated publishing actor observed: `Kirch-Nairu`. Next account must verify
  its own authorized identity; no credential copying or account impersonation.

## Branch/worktree custody

All existing worktrees preserved. Paths below are under
`C:/Users/arane/.gemini/antigravity/scratch/Dev-Salryn-Cafe-wt/` except local main.
SHA prefixes are unambiguous; full SHAs and all local/remote archival refs are in
restricted `safe-exit/worktrees.json`, `local-refs.txt` and remote-ref snapshots.

| Worktree | Branch | Local head | Verified remote head / disposition |
|---|---|---|---|
| i0 | forge/candidate-salryn-cafe | ad2ab978 at code checkpoint | ae169420; local backup repair unreviewed |
| i1 | forge/i1-product-isolation | c78e4d94 | c78e4d94 |
| i2 | forge/i2-schema-reconciliation | b84d7c04 | b84d7c04 |
| i3 | forge/i3-fb-domain-authority | d5d63c87 | d5d63c87 |
| i4 | forge/i4-device-pairing | 89a463b5 | 89a463b5 |
| i6a | forge/i6a-central-sync | 50e3f442 | 50e3f442 |
| i6b | forge/i6b-android-cashier | 1f34b694 | No remote; preserved local only, review pending |
| ci-gates | forge/i7-ci-gates | fc0998b5 | fc0998b5, reviewed and pushed |
| codex-legacy-qa | codex/cafe-legacy-verification | 3a930e24 | 3a930e24, reviewed and pushed |
| agents-doc | forge/reconcile-agents-md | e7a239cb | e7a239cb |
| ux-a | forge/ux-a-design-system | 11ce49b3 | 11ce49b3 |
| ux-b | forge/ux-b-cashier-kitchen | dd399e38 | dd399e38 |
| ux-c | forge/ux-c-backoffice-devices | 38f1535a | 38f1535a |

Read-only recovery adapter checkout is detached at `ec8ea8cb` under the recovery
root. A documentation-only `codex/safe-exit-20261121` branch may be published from
reviewed `ae169420` to make this handoff available remotely without publishing
unreviewed code. Its source parent/disposition is explicit in its receipt.
Archival refs/history remain protected; no branch, worktree, tag or commit deleted.

## Preserved recovery material (LOCAL RESTRICTED — DO NOT UPLOAD)

Root: `C:/Users/arane/SalrynForgeRecovery/20261121-takeover`.

- Issue 27 body/all five comments, all 138 board records, swarm metadata, issues
  1–15 and PR receipts. Original harness archive/source pin `0e8765de...` and
  disposable qualification clones. No raw transcripts/recovery archives in Git.
- Android original 20-file byte-parity backup + staged/working binary patches,
  preserved source commit `0438c7244abef1e0b670f7cfb1b0b342fb0eaf1c`.
- All test logs, TRX, failure evidence, independent hot-journal reproduction,
  legacy-qa log hashes, Android final receipt, and Windows runner artifacts.
- `safe-exit/engineering-journal.db` is a consistent SQLite online backup;
  `.forge/state/journal.db` and backup/mission packets remain in the project.
  Hash chain verified. HOST-COMPOSITION-02 is BLOCKED solely for explicit user
  safe exit (factory has no PAUSED lifecycle state); not a technical deadlock.
- All-ref Git bundle will preserve published/unpublished commits; consult the
  external safe-exit receipt for its exact hash and verification result.
- Installed project harness, 32 skills, pinned vendor source, source manifest,
  `.gitattributes`, Codex config and hook adapter all retained. Fresh Windows
  checkout at `ec8ea8cb` independently of the original file materialization ran
  all 6 adapter checks successfully. Real hook trust/execution and effective
  host concurrency-cap validation remain NOT RUN; manual policy stays required.

## Worker closure and verification frontier

All temporary engineering/review workers have returned authority or were stopped;
no persistent Swarm was created. Do not reactivate it. No additional expensive
verification was initiated for safe exit. Push-triggered runs `44805551262` and
`44805539697` received cancellation requests to avoid further verification work.

- Guard delta `ae169420`: independent scoped PASS; I3 **20/20**, exit0. Singleton,
  missing-registration fail-closed and no 401/403 caching verified. This does not
  close atomic mutation/response persistence or replay RBAC.
- Legacy delta `3a930e24`: independent scoped PASS. Build PASS; focused CoreSmoke
  **52 explicit typed assertions**, exit0. Full CoreSmoke **FAIL** at U1 backup
  restore verification because catalog was still retail v32 while Cafe is v41.
- Primary backup repair `6770e5f9`, integrated locally into `ad2ab978`: recovered
  Yza critical-table catalog, reconciled v40/v41 tables/current ceiling, added
  read-only Cafe/Yza lineage + integrity admission and source backup guard.
  I2 **10/10**, exit0 at local `ad2ab978`; log `backup-catalog-i2.log`, TRX retained.
  **Independent review and full CoreSmoke rerun NOT RUN.** Do not interpret this
  as completion of backup/restore hardening.
- Exact `59ebdcde` Windows gate: **FAIL, exit1** (clean source before/after).
  Solution build passed; **78** xUnit tests passed, both frontend test/build
  gates passed (**MOCKED_UI**). CoreSmoke failed. Full raw evidence remains
  `i0/artifacts/verification/f397257291fc40ba953bd644fbf55e35/`.
- Original portable harness **16 tests PASS**, 6 adapter tests PASS; earlier
  Git-fixture/CRLF failures preserved, not hidden. Native host-specific tests,
  live frontend E2E/full C1–C11 proofs, installer execution and Linux CI NOT RUN.

## Android custody and acceptance limits

Full local head `1f34b6946e0b7fa75ab6086c5c8764b924ae6b01`, clean and unpushed.
Bounded fixes compile paths, real boot count, removal of seeded production sample
menu/false readiness, independently supplied issuer SPKI, serialized anti-rollback
watermark and clock rejection. Exact-SHA **10 tests PASS**, Debug signed APK build
PASS (0 warnings/errors); apksigner v2/v3 verification PASS, **Android Debug cert**.
No independent security review or emulator execution; Issue14 remains incomplete.

APK `i6b/src/Salryn.App.Android/bin/Debug/net10.0-android/ph.kirjanelabs.salryncafe-Signed.apk`,
8,264,230 bytes, SHA256
`CA2C3605E208652848096809E9B1D04655A72E920EEC4D6A93760F8224B3EDAE`.
A hash-verified copy is under restricted `safe-exit/`; no private signing key copied.
Receipt: `android-completion/final-receipt.json` and adjacent logs/TRX.

Open Android defects: no complete native enrollment/operator auth/cashier screens;
platform-protected asymmetric identity/trust provisioning incomplete; caller-created
principals/prices/quantities insufficiently validated; checkout/refund payload
mismatch not enforced; pull/bootstrap advances cursor without proper projection;
print retry lacks durable unknown-outcome recovery; watermark deletion/reboot
limits remain. Current `mobile_*` ledger must not become a second authority.

## EXACT CONTINUATION POINT / REMAINING DEPENDENCIES

1. Reverify this local candidate, all worktrees, remote heads, account authority,
   journal hashes and ownership. Do not replay imports or discard local ahead work.
   No development until explicit authorized resume.
2. Review local backup patch `6770e5f9`/candidate `ad2ab978` and complete controlled
   backup/restore hardening: manifest still lacks product_id, missing product_id
   must be legacy-Yza-only, legacy restore must migrate/verify a staged copy before
   replacing live data; validate live destination before writable recovery; retain
   safety backup/failure evidence. Do not merely bump a version ceiling.
3. Rerun impacted I2/backup and full CoreSmoke after repair (NOT during safe exit),
   resolve further genuine defects without weakening assertions, review and publish
   the resulting exact candidate. Android head needs nonauthor security review.
4. Recover shared Persistence/mobile composition as detailed in
   `CODEX-MOBILE-CONTINUATION.md`. All authoritative Data source files already exist;
   do not replace them with old Yza versions. Port missing C1–C11/F1 proofs and
   reconcile omitted Central.Sqlite semantic processors against central Server.
5. Finish host composition: atomic mutation+receipt+outbox crash/restart proof,
   replay permission checks before cache, approved Cafe endpoint registration/RBAC,
   durable pairing/HTTPS5400, host sync/outbox and one-active-offline-cashier fencing.
   Windows must not become an unauthorized independent second cashier.
6. Finish native Android from shared canonical services, then full cashier/pairing,
   secure local auth, sync projections, print queue recovery, APK/emulator tests.
   Wire unified live frontend/routes/owner remote APIs and all accessibility fixes.
7. Exact-SHA full C1–C11/F1A–E/security/partition/recovery/live Playwright and
   independent adversarial review/repair; installer/APK/central assets/checksums,
   rollback and human checklist. Hardware/physical BT/tablet/coexistence/signing
   remain HUMAN_REQUIRED. No main promotion or production deployment authorized.

---

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
