# DB-A2 — Database Safety Implementation

Implementation date: 2026-07-07

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch | `KIRCH-FREEHAND` |
| DB-A1 base HEAD | `a4d699d128df79bd3cb017ae2d8c10af66d13e3f` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` (untouched) |
| Prior audit | `00-HANDOFF/DB_A1_DATABASE_INTEGRITY_AND_BACKUP_REVIEW.md` |
| Scope | Bounded DB safety batch. No schema migration. No RMO/licensing. `KIRCH-SANDBOX` not modified. |
| DB-A1 remote-back | Already at `a4d699d` before DB-A2 commit; no separate push required |

---

## DB-A1 Findings Addressed

| DB-A1 risk | DB-A2 action | Status |
| --- | --- | --- |
| WAL `File.Copy` backup without checkpoint | `SqliteDatabaseBackupUtility.CreateOnlineBackup()` uses `BackupDatabase` API | **Addressed** |
| Restore leaves stale `-wal`/`-shm` | `RemoveWalShmSidecars()` before and after live DB overwrite | **Addressed** |
| Backup verify only checks 5 core tables | `SalrynDatabaseMigrationCatalog` + expanded `VerifyBackup` | **Addressed** |
| `backup_logs` never written | `SqliteBackupLogWriter.TryWrite()` on create/restore/failure | **Addressed** |
| No migration version check on restore | `ReadMaxMigrationVersion`, `HasFutureMigrationVersion`, `IsRestorable` guard | **Addressed** |
| `receipt_sequences` seeded but unused | Documented + guard tests; no format change | **Documented** |
| Remote POS idempotency in-memory only | Documented + guard tests; no persistence redesign | **Documented** |
| `IErrorLogWriteRepository` dead seam | `SqliteErrorLogWriteRepository` + factory method | **Partial** (repo exists; not broadly wired) |

---

## Files Changed

### New

| Path | Purpose |
| --- | --- |
| `src/Salryn.Infrastructure/Data/SqliteDatabaseBackupUtility.cs` | WAL-safe online backup + WAL/SHM sidecar cleanup |
| `src/Salryn.Infrastructure/Data/SalrynDatabaseMigrationCatalog.cs` | Migration v12 critical-table catalog |
| `src/Salryn.Infrastructure/Data/Services/SqliteBackupLogWriter.cs` | Best-effort `backup_logs` persistence |
| `src/Salryn.Infrastructure/Data/Repositories/System/SqliteErrorLogWriteRepository.cs` | `IErrorLogWriteRepository` SQLite implementation |
| `tests/Salryn.Tests/DbA2DatabaseSafetyChecks.cs` | Source guards + runtime backup/verify tests |
| `00-HANDOFF/DB_A2_DATABASE_SAFETY_IMPLEMENTATION.md` | This report |

### Modified

| Path | Purpose |
| --- | --- |
| `src/Salryn.Core/Services/BackupRecoveryService.cs` | `CreateBackup()`; expanded `BackupVerificationSummary` |
| `src/Salryn.Infrastructure/Data/Services/SqliteBackupRecoveryService.cs` | Full DB-A2 backup/verify/restore hardening |
| `src/Salryn.App.WinForms/Composition/SalrynAppServices.cs` | Delegates backup to recovery service (no raw `File.Copy`) |
| `src/Salryn.App.WinForms/Features/Backup/BackupForm.cs` | Displays migration/critical-table verification |
| `src/Salryn.Infrastructure/Data/Repositories/SqliteRepositoryFactory.cs` | `CreateErrorLogWriteRepository()` |
| `tests/Salryn.Tests/PosStabB2BackupUiChecks.cs` | Updated for recovery-service delegation |
| `tests/Salryn.Tests/PosStabB5BackupRecoveryChecks.cs` | Updated for DB-A2 patterns |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Wires `DbA2DatabaseSafetyChecks.Run()` |
| `tools/Salryn.Tools.InfrastructureSmoke/PosAdminOperationsRegressionSmokeChecks.cs` | Uses `CreateBackup()`, checks `CriticalTablesExist` |

---

## WAL-Safe Backup Implementation

- `SalrynAppServices.CreateBackup()` now routes through `BackupRecoveryAppServices.CreateBackupRecoveryService(this).CreateBackup()`.
- `SqliteBackupRecoveryService.CreateBackup()` calls `SqliteDatabaseBackupUtility.CreateOnlineBackup(source, destination)`.
- Uses `Microsoft.Data.Sqlite` `BackupDatabase` API after clearing connection pools.
- Captures committed WAL pages without requiring app shutdown for normal backup creation.
- Backup destination must not pre-exist (no silent overwrite).
- Host-local only; existing operator workflow preserved.

---

## Restore WAL/SHM Safety

- Pre-restore safety copy also uses online backup API (not raw `File.Copy`).
- `RemoveWalShmSidecars(liveDatabasePath)` runs **before** and **after** `File.Copy` of verified backup onto live DB.
- Exclusive lock check (`CanOpenLiveDatabaseExclusively`) still required before overwrite.
- Typed confirmation (`RESTORE SALRYN BACKUP`) unchanged.
- Restore still requires app stopped / exclusive access when live DB is locked.

---

## Expanded Backup Verification

`SalrynDatabaseMigrationCatalog` defines critical tables by introducing migration version (v1–v12):

- **Core:** roles, users, store_settings, products, sales, sale_items, payments, stock_movements, cashier_sessions, receipt_sequences, audit_logs, error_logs, backup_logs
- **LAN:** deployment_identity, salryn_lan_transport_config, salryn_terminal_registrations, salryn_terminal_pairing_invites, salryn_terminal_credentials, salryn_remote_client_handshakes
- **BIR:** bir_machine_profiles, bir_official_document_sequences, bir_electronic_journal_entries, bir_electronic_journal_preservation_batches, bir_electronic_journal_entry_events, bir_z_readings

Verification tolerates missing tables only when backup migration version indicates they should not exist. At v12, all listed tables are required for `CriticalTablesExist = true`.

---

## Migration Version Restore Guard

- `VerifyBackup` reads `MAX(version)` from `schema_migrations`.
- `BackupVerificationSummary.HasFutureMigrationVersion` when backup version > app supported v12.
- `IsRestorable` requires: legacy core tables + critical tables + migration compatible + no future version.
- Restore rejects backups that fail `IsRestorable` with explicit operator message.

---

## Backup Logs Behavior

`SqliteBackupLogWriter.TryWrite()` inserts into existing `backup_logs` schema (no migration):

- **Backup create success:** file path, size, `Completed` status
- **Backup create failure:** folder path, `Failed` status, error message
- **Restore success:** restored backup path, size, `Completed`
- **Restore blocked/failed:** backup path, `Failed`, error message

Best-effort: resolves first seeded user as `created_by_user_id`; silently skips if no user. No secrets logged.

---

## Error Log Write Seam Status

- `SqliteErrorLogWriteRepository` implements `IErrorLogWriteRepository.AddAsync()`.
- `SqliteRepositoryFactory.CreateErrorLogWriteRepository()` exposes factory method.
- Secret-marker rejection on password/token/credential/connection-string patterns.
- **Not broadly wired into runtime composition** — follow-up needed to connect where callers expect it. Low-risk seam closure only.

---

## Numbering Ownership Status

**No receipt format changes in DB-A2.**

Guard tests and source checks document:

| Surface | Authority |
| --- | --- |
| WinForms checkout | Not `receipt_sequences` (seeded but unused) |
| Remote POS | `RemotePosReceiptNumberAllocator` → `RPOS-*` prefix |
| Official BIR | `bir_official_document_sequences` |
| Collision backstop | `sales.receipt_no UNIQUE` |

Remote POS source guards confirm no reference to `bir_official_document_sequences`. Product decision on `receipt_sequences` disposition deferred.

---

## Remote POS Idempotency Restart Status

**No persistence redesign in DB-A2.**

Documented via source guards on `RemotePosCheckoutService`:

- `_idempotency` cache is in-memory
- Committed success memoized before side effects
- `sales.receipt_no UNIQUE` prevents receipt collision but not duplicate business intent after process restart
- DB-backed idempotency table recommended for later sprint if Kirch approves migration

---

## Tests Added

| Test surface | Coverage |
| --- | --- |
| `DbA2DatabaseSafetyChecks` | Source guards for all DB-A2 patterns |
| `DbA2DatabaseSafetyChecks.RunBackupUtilityChecks` | Online backup, critical tables, future-migration rejection |
| `DbA2DatabaseSafetyChecks.RunNumberingBoundaryChecks` | RPOS prefix, no official sequence |
| `DbA2DatabaseSafetyChecks.RunIdempotencyDocumentationChecks` | In-memory idempotency + UNIQUE backstop |
| `PosStabB5BackupRecoveryChecks` | Updated for migration catalog + WAL-safe restore |
| `PosStabB2BackupUiChecks` | Updated for recovery-service delegation |
| `PosAdminOperationsRegressionSmokeChecks` | `CreateBackup()` + `CriticalTablesExist` |
| CoreSmoke | Wires `DbA2DatabaseSafetyChecks.Run()` |

No schema migration added. Tests use temp dirs under project `.salryn-db-a2-check`.

---

## Verification Results

Executed on `KIRCH-FREEHAND` after DB-A2 implementation.

| Command | Result |
| --- | --- |
| `git diff --check` | Passed (CRLF warnings only) |
| `dotnet build .\Salryn.sln --no-restore` | Passed — 0 errors, 1 pre-existing warning |
| `dotnet test .\Salryn.sln --no-build` | Passed — exit 0 |
| `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj` | Passed — `SALRYN Core smoke checks passed.` |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance` | Passed |
| `npm install` in `src\Salryn.App.Web` | Passed — 0 vulnerabilities |
| `npm run build` in `src\Salryn.App.Web` | Passed — built in 7.04s |

---

## Remaining Risks

| Risk | Severity | Notes |
| --- | --- | --- |
| Remote POS idempotency lost on restart | Medium | Documented; DB persistence needs Kirch-approved migration |
| `receipt_sequences` unused | Low | Seeded table with unclear ownership; product decision pending |
| `IErrorLogWriteRepository` not wired to runtime | Low | Implementation exists; composition follow-up |
| `backup_logs` uses first user as operator | Low | Best-effort; no dedicated operator context in backup flow |
| Restore still needs exclusive DB access | Medium | By design; operator must stop app or accept manual step |
| Older backup versions (< v12) | Low | Restorable if critical tables for that version exist; no auto-upgrade |

---

## GO / NO-GO for RMO-A1 Study

**GO** — DB safety batch closed the highest operational backup/restore risks identified in DB-A1. Schema and services remain suitable for RMO engineering study. No production certification claim.

---

## GO / NO-GO for LIC-M1 Design

**GO** — No license implementation added. `deployment_identity` and `app_metadata` hooks unchanged. Design planning can proceed independently.

---

## GO / NO-GO for Frontend Polish Later

**GO** — DB-A2 touched only backup verification display strings in WinForms. No frontend polish scope opened or blocked.

---

## Promotion Recommendation

**Do not promote to `KIRCH-SANDBOX` yet.**

DB-A2 is a bounded safety hardening batch on `KIRCH-FREEHAND`. Kirch should review this report and verification evidence before any sandbox promotion. `KIRCH-SANDBOX` remains at `93a2f921` — untouched.

---

*DB-A2 implementation completed by batch sprint executor. Kirch remains project lead.*