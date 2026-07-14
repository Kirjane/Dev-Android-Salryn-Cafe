# POS-B1 Controlled Backup / Restore Safety Report

Branch: `KIRCH-POS-B1-BACKUP-RESTORE-SAFETY`  
Base SHA: `a14620db7faee9dcdae2563720ac5b4ce1338dce`

## Implementation

- Controlled package folders under `DbPaths.BackupPath` with opaque generated IDs.
- JSON manifest containing relative database path, byte size, SHA-256, timestamp, and schema version.
- Canonical containment rejects rooted IDs, traversal, separators, and unknown IDs.
- Verification rejects missing/malformed manifests, missing database files, invalid relative paths, size/hash mismatch, and invalid SQLite shape/version.
- Restore revalidates package immediately, requires the existing `RESTORE SALRYN BACKUP` phrase, then delegates to the proven safety-backup/exclusive-restore service.
- Loopback/session/`Backup.Manage` guarded create/list/verify/restore endpoints expose IDs only, never caller filesystem paths.
- Database backup includes S1 settings. License and external config are outside this database package and remain untouched.

## Automated proof

Temporary-database behavior proves creation, manifest/hash correctness, valid verification, traversal/absolute/unknown rejection, confirmation enforcement, settings restore, database readability, unchanged ledger counts, tamper rejection, missing manifest/file rejection, and endpoint permission guard presence.

## Gates

Solution build, full CoreSmoke, POS-B1 focused smoke, all requested POS-M1 through POS-S1/R1/SURFACE1 predecessor gates, web build, and diff check pass. Web build retains the existing Vite chunk-size advisory.

## Manual proof deferred

External-drive behavior, operator workflow, retention policy, separate-machine restore, and long-term package custody remain deferred to Kirch.

## Limitations

Packages are integrity-checked, not encrypted. UI is deferred. Conservative controlled-root creation avoids caller paths; reparse-point/junction administration remains an operating-system deployment concern.

## Status

`CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`
