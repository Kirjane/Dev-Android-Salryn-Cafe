# PRE-BETA-D — Portable Backup Import/Export Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-D-PORTABLE-BACKUP-IMPORT-EXPORT` |
| Base | `KIRCH-PRE-BETA-C-STORE-LOGO-BRANDING` @ `ffdf968d5ef143a25f9ef6e9c543b16eabd5b2ad` |
| Final SHA | *(branch tip after push)* |
| Protected SANDBOX | untouched |
| Protected INSTALLER-M1 | untouched |

## 2. Decision

**`PORTABLE_BACKUP_IMPORT_EXPORT_PASSED`**

## 3. Goal achieved

Client can **Download Backup File** (`.salryn-backup.zip`) and **Import Backup File**, verify before restore, see summary (store name, created date, schema/migration, users/products/sales, logo presence), type **RESTORE**, archive current DB before restore, reject corrupt / checksum mismatch / future schema packages.

## 4. Implementation

### Package format

- Portable package: **`.salryn-backup.zip`**
- Contents only: `manifest.json` + `salryn.db`
- Manifest includes backup id, createdAt, schemaVersion, file size + SHA-256
- Max import size: **100 MB**
- No private keys / LicensingOps secrets / source-tree paths

### Backend (`ControlledBackupPackageService`)

- `ExportPortable(id)` → zip of verified package
- `ImportPortable(stream)` → extract, validate entries/paths/hash, assign local id, verify, reject unsafe packages
- Explicit `BackupPackage.FutureVersion` when schema is newer than supported
- Verification summary includes `storeName` and `hasLogo` (Lane C DB-backed logo)
- Restore still archives current live DB via existing `SqliteBackupRecoveryService` safety backup

### API

- `GET /api/backups/{id}/download` — portable zip download
- `POST /api/backups/import` — multipart field `package`
- Existing create / list / verify / restore unchanged (permission + license guards)

### Frontend

- Client-friendly labels: Download Backup File, Import Backup File, Verify Backup, Restore Backup
- Summary panel before restore
- Type RESTORE confirmation retained

### Tests

- `PreBetaDPortableBackupImportExportChecks` (create/export/import/corrupt/hash/future/restore/archive)
- PosB1 frontend guards updated for portable import
- WEB-A3/A4 allow FormData only on logo + backup approved surfaces

## 5. Gates

| Gate | Result |
| --- | --- |
| `dotnet build` (CoreSmoke project) | PASS |
| `pre-beta-d-portable-backup-import-export` | PASS |
| **full CoreSmoke** | **PASS** |
| web build | PASS |

## 6. Explicit non-claims

- Not cloud backup
- Not installer migration proof
- Does not bundle host license private keys or LicensingOps secrets
- External host license behavior preserved (DB-only package)

## 7. DO LATER

- Client update rehearsal on real client machine
- Larger-package performance tuning if multi-GB DBs appear

## 8. Risks

- Import assigns a new local backup id (avoids overwrite collisions)
- Download uses cookie-authenticated same-origin GET link
- Zip only allows flat `manifest.json` + `salryn.db` entries
