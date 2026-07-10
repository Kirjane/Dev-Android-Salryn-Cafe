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
| Final SHA | `7dfe23fd55b719e207003cb67fafb517e04e87af` |
| origin/M11 | `6d113697c6ae308cc423c0bd35c59381dd74089b` |
| origin/Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| origin/Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` |
| M11 → QA1 | 0 behind / 1 ahead (after commit) |
| Sandbox → QA1 | 0 behind / 12 ahead (after commit) |
| Worktree after push | clean expected |

---

## 2. Study decision

**GO WITH NARROW FIX**

### Files inspected (primary)

- Migration catalog, DefaultDatabaseInitializationFactory, MigrationRunner, v13/v14 migrations  
- SqliteBackupRecoveryService, SqliteDatabaseBackupUtility  
- Official setup / dry-run / source-sale / candidate services  
- DbA2, B5, RMO M4–M11 gates  

### Files changed

| File | Change |
|------|--------|
| `00-HANDOFF/RMO_BIR_QA1_REAL_MACHINE_STUDY.md` | Study |
| `00-HANDOFF/RMO_BIR_QA1_FINAL_REPORT.md` | This report |
| `tests/Salryn.Tests/RmoBirQa1RealMachineDatabaseChecks.cs` | On-disk QA1 runtime |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Wire `rmo-bir-qa1-real-db` + M9–M11 in full sequence |
| `src/Salryn.Infrastructure/Data/Services/SqliteBackupRecoveryService.cs` | **Narrow fix:** pool clear + WAL checkpoint before exclusive restore open |

### Production code

- **Yes — one narrow fix** in restore path (see §5).  
- No checkout/LAN/installer/output/EIS activation changes.

### Defect found

- **Yes:** In-process restore after online safety backup failed exclusive `FileShare.None` open due to residual pooled/WAL handles.  
- **Fix:** `ClearAllPools` + `PRAGMA wal_checkpoint(TRUNCATE)` before exclusive open.  
- **Retest:** restore succeeded; safety backup created; semantic state restored.

---

## 3. QA environment

| Item | Value |
|------|--------|
| OS | Microsoft Windows NT 10.0.19045.0 |
| .NET | 10.0.301 |
| Node | v24.16.0 |
| Isolation | `%TEMP%\Salryn-RMO-BIR-QA1-<guid>` only |
| Production ProgramData DB | **Not used** |
| Paths | All DB/backup under unique TEMP root; root deleted after run |

---

## 4. Migration evidence

### A. Clean → v14

| Field | Observed |
|-------|----------|
| Start | empty |
| End | max `schema_migrations` = **14** |
| Migrations | versions 1–14 present once each |
| Tables | `official_receipt_profiles`, `official_dry_run_packages` present |
| Integrity | `ok` |
| Foreign keys | empty |

### B. Genuine v12 → v14

| Field | Observed |
|-------|----------|
| Start | real migrations 1–12 only |
| End | **14** |
| Applied | v13 + v14 |
| Sequence `current_number` | **42 → 42** |
| Sale sentinel | preserved |
| EJ count | **1 → 1** |
| Integrity / FK | ok / empty |

### C. Genuine v13 → v14

| Field | Observed |
|-------|----------|
| Start | real migrations 1–13 |
| End | **14** |
| Receipt profile title | preserved |
| Sequence | **42 → 42** |
| Integrity / FK | ok / empty |

---

## 5. Backup and restore evidence

| Check | Result |
|-------|--------|
| CreateBackup | success; file under QA backups; no leftover `-wal`/`-shm` on backup |
| VerifyBackup | version 14; critical tables present; compatible; restorable |
| Future v15 | `HasFutureMigrationVersion=true`, `IsRestorable=false`, restore rejected; live QA DB unchanged |
| Restore | success after narrow fix |
| Safety backup | created (`salryn-pre-restore-*.db`) |
| Semantic restore | sale total/status, sequence, EJ count, stock movements, session restored to pre-backup values |
| Dry-run packages | ≥2 survived restore |

---

## 6. Non-mutation evidence

| Protected state | Before | After dry-run/candidate | Result |
|-----------------|-------:|------------------------:|--------|
| Operational sale identity/status/total | sentinel sale / status 2 / 15000 | same | **unchanged** |
| Official sequence `current_number` | 42 | 42 | **unchanged** |
| EJ count | 1 | 1 | **unchanged** |
| Stock movement count | ≥1 | same | **unchanged** |
| Shift/session status | open (1) | same | **unchanged** |
| Output artifacts | none | none | **unchanged** |
| EIS transmissions | none | none (`eisWillTransmit=false`) | **unchanged** |

**Allowed writes:** `official_dry_run_packages` inserts (synthetic + OperationalSaleReadOnly); `backup_logs`; safety/backup files under QA root.

**Candidate tracks:** Traditional and EIS both `Blocked=true`, terminal `BlockBeforeMutation`.

---

## 7. Manual UI evidence

| Surface | Result |
|---------|--------|
| Official Setup page loads | **NOT EXECUTED** |
| Readiness / numbering / integrity / spine / dry-run / source-sale / reports / candidate cards | **NOT EXECUTED** |
| Forbidden claims/actions visual check | **NOT EXECUTED** |

**Reason:** Grok CLI session cannot visually operate a browser UI for human confirmation. Automated source gates still enforce forbidden tokens in `OfficialSetupPage.tsx`.

**Minimal manual checklist for Kirch:** start host-local API + web against isolated QA DB; open Official Setup; confirm blocked candidate wording, internal dry-run watermark, no go-live/issue/print/compliant claims; create one internal dry-run package.

---

## 8. Gate results

| Command / gate | Result |
|----------------|--------|
| `dotnet build` Tests/CoreSmoke | **pass** |
| `--only rmo-bir-qa1-real-db` | **pass** |
| `--only rmo-bir-official-readiness` | **pass** |
| `--only rmo-bir-official-setup` | **pass** |
| `--only rmo-bir-official-numbering` | **pass** |
| `--only rmo-bir-official-integrity` | **pass** |
| `--only rmo-bir-official-spine` | **pass** |
| `--only rmo-bir-endgame` | **pass** |
| `--only rmo-bir-full-closure` | **pass** |
| `--only rmo-bir-source-acquisition` | **pass** |
| `--only rmo-bir-live-posting-candidate` | **pass** |
| `--only v2-lan-bind-truth` | **pass** |
| `--only v2-lan-client-pairing` | **pass** |
| `--only v2-terminal-lifecycle` | **pass** |
| `--only v2-host-lan-control-ux` | **pass** |
| `--only v2-host-shift` | **pass** |
| `--only v2-host-checkout` | **pass** |
| `--only v2-react-checkout` | **pass** |
| Full CoreSmoke | **pass** |
| `npm run build` | **pass** |

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

1. **Manual Official Setup UI acceptance** — not visually executed in this CLI session.  
2. EIS JSON API field schema still unresolved (product, not QA1).  
3. Live official posting remains intentionally unimplemented.

**Automated QA1 complete; manual Official Setup UI acceptance remains pending.**

Do not treat QA1 as fully accepted until Kirch completes the manual UI checklist.
