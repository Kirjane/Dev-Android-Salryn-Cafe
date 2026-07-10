# RMO-BIR-QA1 Real-Machine Database Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE`  
**Base M11:** `6d113697`

**Disclaimer:** Engineering QA study only. Not certification. No production operator DB. No live official posting.

---

## Study decision: **GO WITH NARROW FIX**

Genuine v12/v13 fixtures can be built from real migration classes 1–N, upgraded via `DefaultDatabaseInitializationFactory` / full migration runner to v14, and backup/restore exercised through `SqliteBackupRecoveryService` on isolated `DbPaths` under `%TEMP%\Salryn-RMO-BIR-QA1-*`.

**Narrow fix applied during runtime QA:** after safety online backup, restore failed `CanOpenLiveDatabaseExclusively` because pooled/WAL handles remained. Fix: `ClearAllPools` + `PRAGMA wal_checkpoint(TRUNCATE)` before exclusive open in `SqliteBackupRecoveryService.RestoreVerifiedBackup`. No behavior expansion beyond enabling the existing guarded in-process restore path.

---

## Answers

1. **Genuine v12:** `MigrationRunner` with migrations versions 1–12 from `DefaultDatabaseInitializationFactory` order; no faked schema_migrations rows.  
2. **Genuine v13:** Same runner with versions 1–13 (includes `OfficialReceiptProfileSchemaMigration`).  
3. **Upgrade:** After fixture open, run full orchestrator (`InitializeDatabase`) which applies only pending migrations (13+14 or 14).  
4. **No falsified history:** Never delete migration rows from a v14 DB; only apply real `Up` methods.  
5. **Representative state:** Seed roles via `DatabaseSeeder`; fixture SQL inserts for user/product/sale/payment/stock_movement/cashier_session/bir_machine_profiles/bir_official_document_sequences/optional EJ row; dry-run packages via `SqliteHostOfficialDryRunPackageService`.  
6. **Direct SQL:** Fixture setup only (sentinel rows). Services do not expose bulk fixture APIs for every table.  
7. **Backup/restore:** `SqliteBackupRecoveryService.CreateBackup`, `VerifyBackup`, `RestoreVerifiedBackup` with confirmation `RESTORE SALRYN BACKUP`.  
8. **v15 rejection:** Copy backup, insert `schema_migrations` version 15; verify non-restorable; restore fails; live QA DB semantic state unchanged.  
9. **Non-mutation metrics:** sale id/status/total_cents; sequence `current_number`; EJ row count; stock_movements count / product stock if present; cashier_sessions status; candidate/output flags remain false.  
10. **Allowed writes:** `official_dry_run_packages` insert; `backup_logs` rows; safety backup file; pre-restore safety backup.  
11. **Restore in-process:** `SqliteConnectionFactory.ClearAllPools()` before exclusive open (already inside restore service).  
12. **PRAGMA:** `integrity_check` = `ok`; `foreign_key_check` empty.  
13. **Manual UI:** Not automatable in this CLI session without a human-operated browser; mark **NOT EXECUTED** with checklist for Kirch.  
14. **Production fix:** None planned.  
15. **STOP ambiguity:** None for automated path if isolation holds.

---

## Files planned for modification

| File | Role |
|------|------|
| `00-HANDOFF/RMO_BIR_QA1_REAL_MACHINE_STUDY.md` | This study |
| `tests/Salryn.Tests/RmoBirQa1RealMachineDatabaseChecks.cs` | On-disk QA1 runtime |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Wire `rmo-bir-qa1-real-db` + prior M9–M11 in full sequence |
| `00-HANDOFF/RMO_BIR_QA1_FINAL_REPORT.md` | Final evidence report |

**Production code:** none unless defect proven.

---

## Isolation policy

- QA root: `%TEMP%\Salryn-RMO-BIR-QA1-<guid>`  
- All DB/backup paths must start with that root.  
- Never use `DbPaths.CreateProduction()` / ProgramData.  
- Delete only the unique QA root created by the run.
