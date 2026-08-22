# DB-A1 — Database Integrity, Backup Safety, Numbering Ownership, and Restore Risk Review

Review date: 2026-07-07

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch reviewed | `KIRCH-FREEHAND` |
| Local HEAD | `5f47c95f464690678d916f4ddca37db77acefd9b` |
| `origin/KIRCH-FREEHAND` | `5f47c95f464690678d916f4ddca37db77acefd9b` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| Compare `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` | `0 3` |
| Prior audits | `BACKEND_A1_ARCHITECTURE_AND_BAD_CODE_AUDIT.md`, `FULL_SYSTEM_A1_MASTER_REVIEW_AND_RELEASE_RISK_AUDIT.md`, `CLEANUP_A1_OPERATIONAL_HARDENING.md` |
| Scope | Review-first. No schema migration. No RMO/licensing implementation. `KIRCH-SANDBOX` not modified. |
| Code changes in DB-A1 | **None** — report-only |

---

## Verification Results

Executed on `KIRCH-FREEHAND` at `5f47c95` during DB-A1 review.

| Command | Result |
| --- | --- |
| `git diff --check` | Passed |
| `dotnet build .\Salryn.sln --no-restore` | Passed — 0 warnings, 0 errors |
| `dotnet test .\Salryn.sln --no-build` | Passed — exit 0 |
| `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj` | Passed — `SALRYN Core smoke checks passed.` |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance` | Passed |
| `npm install` in `src\Salryn.App.Web` | Passed — 0 vulnerabilities |
| `npm run build` in `src\Salryn.App.Web` | Passed |
| `git status --short` after review | Clean (before report commit) |

---

## Executive Summary

Salryn's SQLite schema foundation is **strong for POS + BIR engineering work**, with deliberate separation between normal POS receipts, remote POS receipts, and official BIR document numbering. Migration ownership is explicit and version-gated. BIR journal event tables have append-only triggers at the database layer.

**Blocker count: 0**

The highest operational risks are **WAL-mode backup/restore mechanics**, **narrow backup verification scope**, and **in-memory-only remote checkout idempotency** (documented restart caveat). These are high-risk non-blockers suitable for narrow DB-A2 implementation — not reasons to halt RMO-A1 study or LIC-M1 design planning.

**No code changes were made in DB-A1.** Kirch approval is required before any schema migration or restore-semantics change.

---

## Migration Ownership Review

### Authoritative migration order

Fixed in `DefaultDatabaseInitializationFactory.cs` (12 migrations, versions 1–12):

| Ver | Migration | Primary effect |
| --- | --- | --- |
| 1 | `InitialSchemaMigration` | 23 core POS tables |
| 2 | `BirMachineProfileSchemaMigration` | `bir_machine_profiles` |
| 3 | `BirOfficialDocumentSequenceSchemaMigration` | `bir_official_document_sequences` |
| 4 | `BirElectronicJournalSchemaMigration` | journal entries, preservation batches, events |
| 5 | `BirElectronicJournalSchemaHardeningMigration` | append-only triggers + partial unique index |
| 6 | `BirZReadingSchemaMigration` | `bir_z_readings` |
| 7 | `BirElectronicJournalCorrectionEventSchemaMigration` | rebuilds events table for `CORRECTION` type |
| 8 | `ProductTaxClassificationSchemaMigration` | `products.tax_classification` |
| 9 | `DeploymentIdentitySchemaMigration` | `deployment_identity` + inline seed |
| 10 | `TerminalRegistrySchemaMigration` | terminal registrations + pairing invites |
| 11 | `LanRemoteTransportSchemaMigration` | `salryn_lan_transport_config` |
| 12 | `TerminalCredentialHandshakeSchemaMigration` | credentials + handshakes |

### `schema_migrations` behavior

- Runner-owned table: `version INTEGER PRIMARY KEY`, `name`, `applied_at` (ISO UTC).
- Created via `CREATE TABLE IF NOT EXISTS` before migration loop.
- Each migration runs in its own transaction with `RecordMigration` in the same transaction.
- Applied versions are skipped on subsequent startups.
- **No `Down()` / rollback migrations** — forward-only posture.

### Transaction and idempotency posture

| Property | Finding |
| --- | --- |
| Per-migration atomicity | Yes — DDL + version row commit together |
| Cross-migration atomicity | No — partial upgrade possible if mid-chain failure |
| DDL idempotency | Mostly **no** — bare `CREATE TABLE` except `schema_migrations` and v11 `salryn_lan_transport_config` |
| Seeding idempotency | `DatabaseSeeder` uses `INSERT OR IGNORE`; v9/v10/v11 have inline existence guards |
| Legacy DB without `schema_migrations` | All migrations reported pending; v1 `CREATE TABLE` would fail on existing tables |

### Startup when DB file is missing

1. `SqliteConnectionFactory` opens with `ReadWriteCreate` — file created on first connection.
2. `MigrationRunner.RunPendingMigrations()` applies all 12 migrations.
3. `DatabaseSeeder.SeedDefaultData()` runs reference seed (roles, permissions, units, `receipt_sequences` row).
4. First admin/store created later by `FirstRunBootstrapper.CreateFirstAdmin()` — not during init.

### Inconsistency noted

- `DatabaseInitializationOrchestrator.CheckStatus()`: when DB file absent, `PendingMigrationCount = ConfiguredMigrationCount` (12).
- `MigrationRunner.GetPendingMigrationCount()`: when DB file absent, returns **0**.

This is a status-reporting inconsistency only; `InitializeDatabase()` still runs all migrations correctly on first open.

---

## Table and Constraint Inventory

### Core POS (v1 — 23 tables)

| Group | Tables | Notable constraints |
| --- | --- | --- |
| App/auth | `app_metadata`, `roles`, `users`, `role_permissions`, `store_settings` | `users.username` UNIQUE; `role_permissions (role_id, permission)` UNIQUE |
| Catalog | `categories`, `brands`, `units`, `suppliers`, `products` | `products` partial unique on active `barcode`; FKs to catalog parents |
| Sales | `receipt_sequences`, `cashier_sessions`, `sales`, `sale_items`, `payments` | `sales.receipt_no` UNIQUE; session/user FKs |
| Ops | `stock_movements`, `audit_logs`, `error_logs`, `backup_logs`, `import_export_jobs`, `print_jobs`, `custom_field_*` | `error_logs.user_id` nullable FK |

**v1 gaps:** no CHECK constraints; `suppliers.name` and `products.sku` not unique; `store_settings` not enforced singleton; `payments` lacks index on `sale_id`.

### BIR / official (v2–v7)

| Table | Notable integrity |
| --- | --- |
| `bir_machine_profiles` | Multiple CHECKs; accredited output gating; partial unique active scope |
| `bir_official_document_sequences` | Range CHECKs; optimistic `current_number` updates; partial unique active scope |
| `bir_electronic_journal_entries` | FKs to machine profile, sale, sequence; unique doc identity index |
| `bir_electronic_journal_entry_events` | Append-only triggers (v5/v7); marker CHECKs per event type |
| `bir_electronic_journal_preservation_batches` | Append-only triggers |
| `bir_z_readings` | Extensive CHECKs; partial unique finalized range |

**Note:** `bir_electronic_journal_entries` rows themselves do not have update/delete triggers — only events and preservation batches are append-only at DB level.

### LAN / deployment (v9–v12)

| Table | Notable integrity |
| --- | --- |
| `deployment_identity` | PK only; seeded once if empty |
| `salryn_terminal_registrations` | PK `terminal_id`; **no FK** to `deployment_identity` |
| `salryn_terminal_pairing_invites` | PK `pairing_id`; **no FKs** |
| `salryn_lan_transport_config` | PK `id`; default row `INSERT OR IGNORE` |
| `salryn_terminal_credentials` | UNIQUE `credential_hash` |
| `salryn_remote_client_handshakes` | Index on `(terminal_id, handshake_status, expires_at_utc)`; **no FKs** |

### Pragmas (connection open)

From `SqlitePragma.cs` on every repository/migration/seeder open:

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA busy_timeout = 5000;
```

---

## Backup Safety Review

### Backup creation path

**`SalrynAppServices.CreateLocalDatabaseBackup()`** (`SalrynAppServices.cs:368-414`):

1. `SqliteConnectionFactory.ClearAllPools()`
2. `File.Copy(liveDatabasePath, salryn-backup-{timestamp}.db, overwrite: false)`

**No WAL checkpoint.** No SQLite backup API. No copy of `-wal` / `-shm` sidecars.

### Backup verification path

**`SqliteBackupRecoveryService.VerifyBackup()`**:

**Required for restore (`IsRestorable`):** only these 5 tables:

- `roles`, `users`, `store_settings`, `products`, `sales`

**Informational only:** `app_metadata` exists, `schema_migrations` exists, row counts for users/products/sales.

**Not verified:** BIR tables, terminal tables, `cashier_sessions`, `receipt_sequences`, `bir_official_document_sequences`, `audit_logs`, `error_logs`, `backup_logs`, LAN config.

### Restore path

**`SqliteBackupRecoveryService.RestoreVerifiedBackup()`**:

1. Requires prior verification + `IsRestorable`
2. Requires typed confirmation: `RESTORE SALRYN BACKUP`
3. Re-verifies backup immediately before restore
4. `ClearAllPools()` + safety copy `salryn-pre-restore-{timestamp}.db`
5. Exclusive lock test on live DB (`FileShare.None`)
6. `File.Copy(backup, liveDatabasePath, overwrite: true)`

**Not done on restore:**

- No deletion of stale `.db-wal` / `.db-shm` beside live file
- No migration version parity check (backup vs expected)
- No post-restore migration run (relies on next app startup)
- No `backup_logs` row written

### `backup_logs` table

Schema exists in v1 (`backup_logs` with `created_by_user_id` FK). **No `INSERT INTO backup_logs` anywhere in `src/`.** Backups are filesystem-only with no durable operator audit row.

### WAL risk assessment

| Risk | Severity | Evidence |
| --- | --- | --- |
| `File.Copy` on WAL-mode DB without checkpoint | **High** | Recent committed pages may live only in `-wal` file |
| Backup omits `-wal`/`-shm` sidecars | **High** | Only main `.db` copied |
| Restore overwrites `.db` but leaves old sidecars | **High** | Orphan WAL/SHM can corrupt reopened database |
| Pool clear before copy | **Low** (mitigation) | Reduces open-handle risk but does not checkpoint WAL |

**Recommendation for DB-A2:** Use `PRAGMA wal_checkpoint(FULL)` or SQLite backup API (`sqlite3_backup_*`) via an open connection; on restore, delete WAL/SHM sidecars or require full app shutdown.

---

## Restore Blast-Radius Review

### Operator safeguards (present)

| Guard | Status |
| --- | --- |
| Typed confirmation `RESTORE SALRYN BACKUP` | Present |
| Pre-restore safety copy | Present |
| Exclusive lock gate (fail closed) | Present |
| Re-verification immediately before restore | Present |
| UI warns restore replaces all data | Present (`BackupForm`) |

### Blast radius

**Full database replacement** — all tables roll back together: sales, inventory, BIR journal, official sequences, terminal trust, LAN config, users, audit history.

### Sequence-state integrity after restore

| Sequence system | Post-restore behavior |
| --- | --- |
| `bir_official_document_sequences.current_number` | Restored to backup snapshot — may rewind or jump vs live |
| `receipt_sequences` | Restored but **unused at runtime** |
| WinForms `POS-...` receipts | Non-sequential timestamp format — no DB counter |
| Remote POS `RPOS-...` | In-memory counter resets on API restart regardless of restore |
| `sales.receipt_no UNIQUE` | Prevents duplicate receipt strings, not logical duplicate checkout |

**Risk:** Restoring an older backup while API process still holds in-memory idempotency state is mitigated by exclusive lock gate, but operator must restart all Salryn processes after restore.

### Migration version compatibility

Restore does not compare `schema_migrations` max version in backup vs application expectation. A backup from an older schema can restore if 5 core tables exist; newer app migrations run on next startup. A backup from a **newer** schema onto older app code is not guarded.

### Audit trail after restore

No automatic `audit_logs` entry for restore completion. Operator action is not durably logged in DB.

---

## Numbering Ownership Review

### Four parallel numbering paths

| Path | Format | Allocator | DB table used? | Authoritative for |
| --- | --- | --- | --- | --- |
| Legacy seed | `SLRN-{year}-######` | **None** | `receipt_sequences` seeded only | **Nothing** — dead schema |
| WinForms POS | `POS-{timestamp}-{session}-{random}` | `CheckoutReceiptNumberBoundary` | No | Normal host-local checkout |
| Remote POS | `RPOS-{epoch}-{token}-{seq}` | `RemotePosReceiptNumberAllocator` (in-memory) | No | Remote LAN checkout |
| Official BIR | `{PREFIX}-{YEAR}-{PADDED}` | `SharedSqliteOfficialDocumentSequenceAllocationParticipant` | `bir_official_document_sequences` | Official documents / journal |

### Is `receipt_sequences` actually used?

**No.** Evidence:

- Seeded in `DatabaseSeeder.cs` (`INSERT OR IGNORE INTO receipt_sequences`)
- **Zero** `SELECT`/`UPDATE`/`FROM receipt_sequences` in runtime `src/` outside seed/migration DDL

### Collision handling

| Scenario | Mitigation |
| --- | --- |
| Cross-path receipt strings | Distinct prefixes (`POS-`, `RPOS-`, official prefix) + `sales.receipt_no UNIQUE` |
| Official sequence race | Optimistic `UPDATE ... WHERE current_number = $n` in shared transaction |
| Remote idempotency retry (same process) | In-memory memo + per-key lock |
| Remote restart + client retry after commit | **Weak** — new `RPOS-...` receipt, potential second sale |

### Official vs non-official

- Remote POS and WinForms paths are explicitly non-official (`RemotePosReceiptNumberAllocator.cs` documents scope).
- Official numbering requires accredited machine profile and runs inside `SqliteOfficialCheckoutPersistenceService` shared transaction.
- Remote/LAN cannot invoke official checkout over HTTP (proven in prior LAN security audits).

### Before RMO-A1

Document that:

1. `receipt_sequences` is legacy/dead — do not wire RMO to it without explicit redesign.
2. Official authority is `bir_official_document_sequences` only.
3. Normal POS receipt numbers are not sequential counters — acceptable for non-official retail POS but must not be presented as BIR official numbers.

---

## Remote POS Persistence Review

### Survives host restart (SQLite)

| Data | Table |
| --- | --- |
| Terminal registrations | `salryn_terminal_registrations` |
| Terminal credentials (hashed) | `salryn_terminal_credentials` |
| Pairing invites | `salryn_terminal_pairing_invites` |
| Handshakes (15 min TTL) | `salryn_remote_client_handshakes` |
| LAN transport config | `salryn_lan_transport_config` |
| Cashier shifts | `cashier_sessions` |
| Completed sales | `sales` + items/payments/stock |
| Audit entries | `audit_logs` (best-effort post-commit) |

### Intentionally in-memory (lost on restart)

| Component | Notes |
| --- | --- |
| `RemotePosRuntimeGate` | Disabled by default after restart (fail-safe) |
| Operator sessions | 30 min TTL; `ConcurrentDictionary` |
| Carts | Per session |
| Checkout idempotency cache | Completed results only |
| Join codes / pending pairing UI state | `RemotePosPairingGateService` |
| `RemotePosReceiptNumberAllocator` | New epoch/token per API process |

### Duplicate checkout after restart

**Documented caveat** (`RemotePosCheckoutService.cs:321-322, 483-486`):

1. Sale commits and is memoized in `_idempotency`
2. Host restarts — memo cleared
3. Client retries same `ClientRequestId` — allocates **new** receipt number
4. `sales` has no `client_request_id` column — no DB idempotency backstop
5. Second sale is possible if cart is rebuilt and shift still open

**Mitigation today:** In-process idempotency + `sales.receipt_no UNIQUE` (prevents same receipt string, not duplicate logical checkout).

**DB-A2 candidate:** Persist idempotency keys or document accepted operator procedure (restart window = manual reconciliation).

---

## Error/Audit Logging Review

### `error_logs` schema

```sql
error_logs (id, user_id NULL, area, message, technical_detail NULL, created_at)
```

### `IErrorLogWriteRepository`

- Defined at `WriteRepositoryInterfaces.cs:143`
- **No SQLite implementation**
- `SqliteRepositoryFactory` exposes `CreateErrorLogReadRepository()` only
- **Zero** `INSERT INTO error_logs` in `src/`

**Verdict:** Dead seam. Error log viewer reads an empty table unless populated externally.

### `audit_logs`

- `SqliteAuditWriteRepository` implements `IAuditWriteRepository` — writes `audit_logs` only
- Used by: remote POS checkout audit, remote read-only audit recorder, official output workflow

### Can sale commit without audit?

**Yes, by design.** `RemotePosCheckoutService` memoizes completed sale immediately after commit; audit is best-effort afterward (`WriteAuditAsync` swallows failures). Sale durability does not depend on audit success.

### DB-A1 recommendation for error logs

**Report-only in DB-A1.** Implement `SqliteErrorLogWriteRepository` in DB-A2 only if:

- Trivial wiring with tests
- No schema change required (schema already exists)
- Clear call sites identified (global exception handler or service boundary)

Alternatively, remove the dead interface in a later cleanup sprint.

---

## BIR/RMO Persistence Boundary Review

**Study only. No RMO implementation. No certification claim.**

### What exists (engineering-ready)

| Area | Evidence |
| --- | --- |
| Machine profiles | `bir_machine_profiles` + CHECK constraints |
| Official sequences | Transactional allocation with optimistic concurrency |
| Electronic journal | Entries + append-only event/batch triggers |
| Z-reading | `bir_z_readings` with finalized range uniqueness |
| Official checkout persistence | `SqliteOfficialCheckoutPersistenceService` shared transaction |
| Tax classification | `products.tax_classification` (app-layer validation) |
| Official output / evidence | Renderer adapters, text-file workflow, engineering evidence packages |

### Remote/LAN official access

**Proven absent** in prior audits:

- `SalrynLanRemoteTransportGate` rejects official commands remotely
- Remote POS does not invoke `official.checkout.complete`
- BIR tables writable only via host-local services

### Classification

| Item | Verdict |
| --- | --- |
| RMO-A1 source-backed study | **GO** — substantial engineering schema and services exist |
| DB integrity fix required first | WAL backup/restore only — does not block RMO **study** |
| Production/legal certification | **NO-GO** — unchanged; `NON_CERTIFICATION_FOOTER` in evidence builder |
| Journal entry row immutability | **Medium gap** — events protected; entry rows not trigger-protected |

---

## Blockers

**Count: 0**

No finding prevents continuing to DB-A2 narrow implementation, RMO-A1 study, or LIC-M1 design planning.

---

## High-Risk Non-Blockers

| ID | Area | Finding | Recommended owner |
| --- | --- | --- | --- |
| H1 | Backup | WAL-mode `File.Copy` without checkpoint or SQLite backup API | DB-A2 |
| H2 | Restore | Overwrites `.db` but does not remove stale `-wal`/`-shm` sidecars | DB-A2 |
| H3 | Remote POS | Idempotency in-memory only; restart + client retry can double-post sale | DB-A2 or documented ops |
| H4 | Backup verify | Only 5 MVP tables required; BIR/terminal tables not gated | DB-A2 |
| H5 | Numbering | `receipt_sequences` seeded but unused — operator confusion risk | DB-A2 docs or removal study |

---

## Medium-Risk Non-Blockers

| ID | Area | Finding |
| --- | --- | --- |
| M1 | Restore | No `schema_migrations` version parity check at restore time |
| M2 | Backup ops | `backup_logs` schema exists but never written |
| M3 | Migrations | `GetPendingMigrationCount()` returns 0 when DB file missing (status inconsistency) |
| M4 | Migrations | No cross-migration transaction — partial upgrade on mid-chain failure |
| M5 | LAN schema | Terminal/handshake tables lack FK enforcement to `deployment_identity` |
| M6 | Products | `tax_classification` has no DB CHECK constraint |
| M7 | Remote POS | Runtime gate + sessions reset on restart (intentional fail-safe, operational friction) |
| M8 | BIR journal | Entry rows lack append-only triggers (events/batches protected) |

---

## Low-Risk Cleanup

| ID | Finding |
| --- | --- |
| L1 | `IErrorLogWriteRepository` dead interface — implement or remove in DB-A2 |
| L2 | Restore safety copy + confirmation text + exclusive lock — good guards already present |
| L3 | Official sequence allocation — well-implemented optimistic concurrency |
| L4 | Seeding uses `INSERT OR IGNORE` consistently |
| L5 | Per-migration version recording co-located with DDL in one transaction |

---

## Recommended DB Fix Order (DB-A2)

1. **WAL-safe backup** — checkpoint or SQLite backup API; document operator shutdown procedure
2. **WAL-safe restore** — delete/clear sidecars; require process shutdown; post-restore migration check
3. **Expand backup verification** — add `schema_migrations`, `bir_*`, `salryn_terminal_*`, `cashier_sessions` presence checks
4. **Write `backup_logs` rows** — on backup create, verify, restore (no schema change)
5. **Remote idempotency study** — persist `client_request_id` vs document restart window (Kirch decision)
6. **`receipt_sequences` disposition** — document as dead, deprecate, or wire to WinForms allocator (Kirch decision)
7. **`IErrorLogWriteRepository`** — implement minimal SQLite writer or remove interface
8. **Restore audit row** — append `audit_logs` entry on successful restore

---

## GO / NO-GO Decisions

| Item | Verdict | Rationale |
| --- | --- | --- |
| **DB-A2 implementation** | **GO** | High-risk items are narrow and well-scoped; no schema blocker identified |
| **RMO-A1 study** | **GO** | Engineering schema/services substantial; study does not require certification claim |
| **LIC-M1 design later** | **GO** | `app_metadata` + `deployment_identity` hooks exist; no license implementation needed now |
| **Production/RMO certification** | **NO-GO** | Unchanged |
| **Schema migration in DB-A2** | **Conditional** | Only with Kirch approval per change; idempotency persistence may need migration |
| **`KIRCH-SANDBOX`** | **Untouched** | Review-only on FREEHAND |

---

## Files Reviewed (Key Evidence Index)

| Topic | Path |
| --- | --- |
| Migration runner | `src/Salryn.Infrastructure/Data/Migrations/MigrationRunner.cs` |
| Migration order | `src/Salryn.Infrastructure/Data/Initialization/DefaultDatabaseInitializationFactory.cs` |
| Init orchestrator | `src/Salryn.Infrastructure/Data/Initialization/DatabaseInitializationOrchestrator.cs` |
| v1 schema | `src/Salryn.Infrastructure/Data/Migrations/InitialSchemaMigration.cs` |
| WAL pragmas | `src/Salryn.Infrastructure/Data/SqlitePragma.cs` |
| Backup create | `src/Salryn.App.WinForms/Composition/SalrynAppServices.cs` |
| Backup verify/restore | `src/Salryn.Infrastructure/Data/Services/SqliteBackupRecoveryService.cs` |
| Verification model | `src/Salryn.Core/Services/BackupRecoveryService.cs` |
| WinForms receipt | `src/Salryn.App.WinForms/Features/Checkout/CheckoutReceiptNumberBoundary.cs` |
| Remote receipt | `src/Salryn.Api.Local/RemotePos/RemotePosReceiptNumberAllocator.cs` |
| Official sequence | `src/Salryn.Infrastructure/Data/Services/SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs` |
| Remote checkout | `src/Salryn.Api.Local/RemotePos/RemotePosCheckoutService.cs` |
| Error log seam | `src/Salryn.Core/Repositories/WriteRepositoryInterfaces.cs` |
| Audit write | `src/Salryn.Infrastructure/Data/Repositories/System/SqliteAuditWriteRepository.cs` |
| Seeding | `src/Salryn.Infrastructure/Data/Seeding/DatabaseSeeder.cs` |

---

## DB-A1 Code Changes

**None.** Report-only per implementation rule (no blocker requiring immediate code change).

---

*Review completed by DB-A1 executor. Do not push until Kirch approves.*