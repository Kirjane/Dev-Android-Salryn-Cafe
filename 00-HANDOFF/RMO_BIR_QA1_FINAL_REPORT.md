# RMO-BIR-QA1 Final Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE`  
**Base M11:** `6d113697c6ae308cc423c0bd35c59381dd74089b`

**Disclaimer:** Engineering QA only. Not BIR certification. Not official issuance. Not accreditation.

---

## 1. Source stamp

| Item | Value |
|------|--------|
| Starting branch | `KIRCH-RMO-BIR-M11-GUARDED-LIVE-POSTING-CANDIDATE-HARDENING` |
| Starting SHA | `6d113697c6ae308cc423c0bd35c59381dd74089b` |
| Final branch | `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE` |
| **Main QA implementation commit** | `7dfe23fd55b719e207003cb67fafb517e04e87af` |
| Report-only SHA stamp commit | `649d818581700f99db4cc6b35f5bc39d53468b41` |
| RC1 closeout commit | *(branch tip after RC1 push — see CLI response / `git rev-parse HEAD`)* |
| origin/M11 | `6d113697c6ae308cc423c0bd35c59381dd74089b` |
| origin/Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| origin/Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` |
| M11 → QA1 | 0 behind / N ahead (see remote verification) |
| Worktree after push | clean expected |

Do **not** treat any single intermediate commit as a self-referential “final SHA.” Use the remote branch tip for verification.

---

## 2. Study decision

**GO WITH NARROW FIX** (original QA1) + **RC1 closeout** (test + report only)

### Production code

- Original QA1: one narrow restore fix in `SqliteBackupRecoveryService` (pool clear + WAL checkpoint before exclusive open).  
- RC1: **no** production code changes — only test assertions and this report.

### Defects

1. **Restore exclusive open** after safety backup — fixed in `7dfe23fd`.  
2. **Missing post-restore receipt/machine re-read** — fixed in RC1 test (`ReadSentinelState` + `AssertOfficialSetupSurvivedRestore`).

---

## 3. QA environment

| Item | Value |
|------|--------|
| OS | Microsoft Windows NT 10.0.19045.0 |
| .NET | 10.0.301 |
| Node | v24.16.0 |
| Isolation | `%TEMP%\Salryn-RMO-BIR-QA1-<guid>` only |
| Production ProgramData DB | **Not used** |

---

## 4. Migration evidence

| Scenario | Start → End | Key assertions |
|----------|-------------|----------------|
| A Clean | empty → **14** | `official_receipt_profiles`, `official_dry_run_packages`; integrity ok |
| B v12→v14 | real 1–12 → **14** | seq 42→42; sale/EJ preserved |
| C v13→v14 | real 1–13 → **14** | receipt title preserved; seq 42→42 |

---

## 5. Backup and restore evidence

| Check | Result |
|-------|--------|
| CreateBackup + no backup WAL/SHM | pass |
| VerifyBackup v14 restorable | pass |
| Future v15 rejected; live unchanged | pass |
| Restore + safety backup | pass |
| Dry-run packages ≥2 after restore | pass |
| **Post-restore receipt profile id/title/kind reread** | **pass (RC1)** |
| **Post-restore machine profile exists** | **pass (RC1)** |
| **Receipt + sequence still reference expected machine** | **pass (RC1)** |
| Sequence current_number unchanged after restore | pass |

---

## 6. Non-mutation evidence

| Protected state | Before | After dry-run/candidate | Result |
|-----------------|-------:|------------------------:|--------|
| Operational sale identity/status/total | sentinel | same | unchanged |
| Official sequence `current_number` | 42 | 42 | unchanged |
| EJ count | 1 | 1 | unchanged |
| Stock movement count | ≥1 | same | unchanged |
| Shift/session status | open | open | unchanged |
| Output / EIS transmit | none / false | none / false | unchanged |

Allowed writes: dry-run packages, backup_logs, QA backup files.

---

## 7. Manual UI evidence

| Check | Result |
|-------|--------|
| Official Setup opens | **NOT EXECUTED** |
| Business / machine / receipt / series display | **NOT EXECUTED** |
| Readiness live posting false | **NOT EXECUTED** |
| Numbering preview no advance | **NOT EXECUTED** |
| Integrity / spine cards | **NOT EXECUTED** |
| Dry-run + watermark | **NOT EXECUTED** |
| Source-sale read-only wording | **NOT EXECUTED** |
| Candidate blocked; checkout not connected | **NOT EXECUTED** |
| EIS JSON schema unresolved | **NOT EXECUTED** |
| No forbidden claims/actions | **NOT EXECUTED** |

Automated source gates still forbid many marketing tokens in `OfficialSetupPage.tsx`. That is **not** a substitute for Kirch visual confirmation.

---

## 8. Gate results

Recorded at RC1 closeout (rerun required after RC1 test change):

| Gate | Result |
|------|--------|
| `rmo-bir-qa1-real-db` | pass (expected after RC1) |
| Full CoreSmoke | pass (expected after RC1) |
| `npm run build` | pass (expected after RC1) |

*(CLI response lists exact observed results.)*

---

## 9. Honest status

- Live official checkout remains **absent**  
- Production official posting remains **absent**  
- Official sequence mutation during normal sale remains **absent**  
- Live EJ checkout binding remains **absent**  
- Official receipt/PDF/printer/Crystal remains **absent**  
- eSales/EIS live push remains **absent**  
- EIS JSON schema remains **unresolved**  
- Compliance/accreditation/certification is **not claimed**  
- Sandbox remains **untouched**  
- Installer remains **untouched**  
- Promotion was **not performed**

---

## 10. Remaining blockers

1. **Manual Official Setup UI acceptance** — still the visible incomplete acceptance gate.  
2. EIS JSON API field schema unresolved (product).  
3. Live official posting intentionally unimplemented.

**Automated QA1-RC1 complete; full QA1 acceptance remains pending manual Official Setup UI verification.**
