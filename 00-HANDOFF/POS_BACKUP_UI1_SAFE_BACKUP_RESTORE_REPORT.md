# POS Backup UI1 Safe Backup/Restore Report

Date: 2026-07-15

Branch: `KIRCH-POS-BACKUP-UI1-SAFE-BACKUP-RESTORE`

Base branch: `KIRCH-REPAIR-CORESMOKE-DEVDB-ISOLATION`

Base SHA: `cf1d925a4ce05083f2263f7fd1b958d17db538ef`

Final SHA: supplied by final assistant response after this report commit.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Mission

Expose the existing controlled backup package backend through a safe admin-only UI.

This lane is UI/API-client/source-guard only. It does not implement a new backup engine, cloud backup, installer migration, licensing finalization, RMO/BIR/eSales behavior, or hardware support.

---

## Files changed

- `src/Salryn.App.Web/src/api/backups.ts`
- `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `tests/Salryn.Tests/PosB1BackupRestoreSafetyChecks.cs`
- `00-HANDOFF/POS_BACKUP_UI1_SAFE_BACKUP_RESTORE_REPORT.md`

No backend backup service semantics were changed.

No PRINTER1 runtime behavior was changed.

No Surface4 hardware-ban behavior was changed.

No CoreSmoke dev-DB isolation behavior was changed.

No LIC-FINAL, I1, RMO/BIR/eSales, official receipt/invoice, cloud backup, scheduled backup, file picker, or external drive discovery work was done.

---

## PASS 1 findings

### Endpoints found

Existing backend endpoint file:

- `src/Salryn.Api.Local/LocalBackupPackageEndpoints.cs`

Available routes:

- `POST /api/backups`
- `GET /api/backups`
- `POST /api/backups/{id}/verify`
- `POST /api/backups/{id}/restore`

### Permission model

All endpoint handlers call `LocalApiPermissionGuards.RequirePermission(..., LocalApiPermissions.BackupManage)`.

Frontend permission catalog already had `BackupManage: "Backup.Manage"`.

Admin seeded permissions include `Backup.Manage`.

Manager and Cashier seeded permissions do not include `Backup.Manage`.

### Backend safety model

Existing package service:

- creates controlled backup IDs shaped like `backup-yyyyMMdd-HHmmss-xxxxxxxx`;
- writes package content under the configured backup root;
- copies only `salryn.db` into the package;
- writes `manifest.json` with relative `salryn.db`, size, SHA-256, schema version, and backup ID;
- rejects blank IDs, rooted paths, path traversal, and IDs containing directory separators;
- verifies manifest structure, relative path, size, SHA-256, database restorable state, and migration compatibility before restore;
- delegates restore to `SqliteBackupRecoveryService.RestoreVerifiedBackup`;
- restore requires exact backend confirmation text `RESTORE SALRYN BACKUP`;
- restore creates a safety backup before replacement and fails if exclusive live DB replacement is not safe.

### Path exposure model

The controlled package endpoint returns package summary/verification models. The underlying lower-level service includes file paths in some lower-level records, but the new frontend API/page intentionally does not display raw file path fields and does not accept any path input.

### Current missing UI pieces

Before this lane, the backend existed and B1 smoke covered backend package safety, but there was no operator UI for:

- create backup;
- list known backup package IDs;
- verify by backup ID;
- restore with explicit UI confirmation.

### Safest UI location

Admin group, not Advanced compliance surfaces. Reason: backup/restore is an installation safety tool, not an RMO/BIR/reporting feature, not installer proof, and not a LAN/hardware surface.

---

## Implementation

### API client

Added `src/Salryn.App.Web/src/api/backups.ts`:

- `listBackups()` -> `GET /api/backups`
- `createBackup()` -> `POST /api/backups`
- `verifyBackup(backupId)` -> `POST /api/backups/{id}/verify`
- `restoreBackup(backupId)` -> `POST /api/backups/{id}/restore`

The client encodes backup IDs with `encodeURIComponent` and sends the backend confirmation text only from the restore helper. No raw path or upload API is exposed.

### UI page

Added `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx`.

Behavior:

- page title `Backup & Restore`;
- admin-only local backup package controls;
- create backup button;
- list known backup package IDs;
- verify per backup ID;
- show verification summary: schema, users, products, sales, migration version, SHA-256;
- restore button disabled until the backup has a restorable verification result;
- restore panel requires typing `RESTORE`;
- UI calls backend restore only after verification + typed confirmation;
- no raw path display;
- no file upload;
- no delete action;
- no scheduled backup;
- no cloud backup claim;
- no installer migration proof claim;
- no licensing final claim;
- no legal/BIR archive/eSales/official output claim.

### Navigation and permissions

Updated:

- `navigation.ts`
- `permissions.ts`
- `App.tsx`
- `AppShell.tsx`

Route/page ID:

- `backup-restore`

Visible label:

- `Backup & Restore`

Nav group:

- `Admin`

Required frontend permission:

- `Backup.Manage`

Role behavior:

- Admin: visible if seeded/assigned `Backup.Manage`.
- Manager: not in manager allowlist, and lacks `Backup.Manage` by seed.
- Cashier: not in cashier allowlist, and lacks `Backup.Manage` by seed.

### Source guards

Updated existing `PosB1BackupRestoreSafetyChecks.cs` rather than creating a new brittle CoreSmoke target.

B1 now guards:

- Backup UI nav label and admin group;
- Backup page requires `Backup.Manage`;
- cashier allowlist remains checkout/sales only;
- App renders `BackupRestorePage`;
- AppShell has Backup & Restore icon/admin badge logic;
- API client uses `/api/backups` and encoded backup IDs;
- UI requires typed `RESTORE`;
- UI warns restore replaces the current local database;
- UI rejects cloud/installer/licensing/legal/BIR/eSales/official-output claims;
- UI has no `type="file"` upload;
- UI does not expose raw file path fields.

---

## PASS 2 findings

### Cashier visibility

Cashier allowlist remains `checkout` and `sales`. Backup & Restore is not available to Cashier.

### Manager visibility

Manager allowlist does not include `backup-restore`. Manager seeded permissions also do not include `Backup.Manage`.

### Admin visibility

Admin seeded permissions include `Backup.Manage`; Backup & Restore is under Admin navigation.

### Restore confirmation

Restore requires:

1. selected backup ID;
2. successful/restorable verification result;
3. typed `RESTORE` in the UI;
4. backend confirmation text sent by the API client.

### Raw path and upload exposure

The UI does not expose path entry, file picker, arbitrary JSON upload, arbitrary ZIP upload, or raw backend file path fields.

### Backup backend semantics

No backup backend service, endpoint, manifest, verify, or restore logic was changed.

### Claim boundaries

The page explicitly says it is not cloud backup, installer migration proof, licensing final, legal archive, BIR archive, eSales submission, or official output retention.

### Remaining risks

- Local build/web build was not executed from this chat.
- Runtime restore behavior still needs SOL/local verification against the existing backend.
- The backend may require app restart/manual operator handling if live DB exclusive replacement is blocked; the UI surfaces restore errors and tells the operator to restart if stale data is reported.
- No backup delete/import/upload/offsite/cloud/scheduled behavior exists in this lane.

---

## Current backup claim

Allowed claim:

- local backup/restore UI only;
- controlled backup package IDs;
- backend manifest/hash/database-shape verification;
- destructive restore requires explicit UI confirmation and backend confirmation.

Not claimed:

- cloud backup;
- legal/BIR archive;
- eSales retention/submission;
- official output retention;
- installer migration proof;
- licensing final;
- disaster recovery unless user copies backups outside the computer.

---

## Not touched

- PRINTER1 runtime
- Surface4 hardware scope ban
- CoreSmoke dev-DB isolation beyond B1 source guard additions
- LIC-FINAL
- I1 installer
- RMO/BIR implementation
- official receipt/invoice implementation
- eSales implementation/transmission
- cloud backup
- scheduled backup
- file picker/import/upload
- hardware support
- protected refs

---

## Required gates

Local gates were not run from this chat.

Required SOL/local gate block:

```powershell
cd C:\Users\kirch\Salryn

git fetch origin
git checkout KIRCH-POS-BACKUP-UI1-SAFE-BACKUP-RESTORE
git reset --hard origin/KIRCH-POS-BACKUP-UI1-SAFE-BACKUP-RESTORE

git rev-parse HEAD
git status

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-b1-backup-restore-safety
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only user-m1-host-only-user-creation
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-printer1-windows-printer-wiring
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-r3-frontend-safety-ux-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-surface1-role-page-lock-store-nav
dotnet run --project tools\Salryn.Tools.CoreSmoke
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status:

- build: `NOT RUN / NEEDS SOL`
- B1: `NOT RUN / NEEDS SOL`
- Backup UI1: `NOT ADDED`; covered by updated B1 source guard
- USER-M1: `NOT RUN / NEEDS SOL`
- PRINTER1: `NOT RUN / NEEDS SOL`
- R3: `NOT RUN / NEEDS SOL`
- SURFACE1: `NOT RUN / NEEDS SOL`
- full CoreSmoke: `NOT RUN / NEEDS SOL`
- web build: `NOT RUN / NEEDS SOL`
- diff check: `NOT RUN / NEEDS SOL`
- git status: `NOT RUN / NEEDS SOL`

No automated-proven claim is made from this chat.

---

## Protected refs

No write operation was issued to:

- `KIRCH-SANDBOX`
- `KIRCH-INSTALLER-M1`

---

## Final status

`READY_FOR_SOL_VERIFICATION`
