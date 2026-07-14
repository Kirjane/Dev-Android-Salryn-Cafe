# POS-B1 Backup / Restore Safety Blocked Report

Branch: `KIRCH-POS-B1-BACKUP-RESTORE-SAFETY`  
Base SHA: `a14620db7faee9dcdae2563720ac5b4ce1338dce`

## Status

`BLOCKED`

## Repo-backed finding

`SqliteBackupRecoveryService` already provides a sound database-only foundation: online backup, schema/migration verification, explicit `RESTORE SALRYN BACKUP` confirmation, fresh re-verification, pre-restore safety backup, WAL/SHM cleanup, exclusive-open enforcement, and future-version rejection.

It does not satisfy the requested B1 export boundary yet:

- backup output is a single `.db`, with no manifest or SHA-256 hash
- tampering is detected only when SQLite/schema validation fails; byte-level tampering is not proven
- `VerifyBackup(string backupFilePath)` accepts an arbitrary caller path
- no API-safe backup identifier maps back to the controlled backup root
- no API permission surface exists for Backup.Manage

Exposing the current service directly would create an arbitrary-path read boundary and would not meet the manifest/hash acceptance criteria. No partial API or UI was added.

## Minimal safe repair lane

1. Add a controlled-root package service that returns opaque backup IDs, never caller paths.
2. Create a timestamped package containing the SQLite backup and JSON manifest.
3. Record SHA-256, size, schema version, and creation timestamp in the manifest.
4. Resolve IDs with canonical-path containment under `DbPaths.BackupPath`.
5. Verify manifest and hash before delegating to existing SQLite shape verification.
6. Require Backup.Manage plus the existing restore confirmation phrase.
7. Keep license/config outside database untouched and state that rule explicitly.
8. Add temporary-directory behavior tests for valid, missing, tampered, traversal, confirmation, restore readability, settings survival, and unchanged ledger counts.

## Stack decision

POS-T1 and POS-I1 were not started because the hard rule forbids stacking on a blocked B1 branch.

## Non-claims

No manual proof, store readiness, installer proof, clean-machine proof, cloud backup, production claim, or branch promotion is made.

## Protected refs

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`
