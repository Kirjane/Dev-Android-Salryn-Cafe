# POS-STAB-BATCH-2 Operational Safety UI Seal

## Scope

POS-STAB-BATCH-2 added small operator safety/support surfaces after the basic store-day workflow seal. This is a local POS stabilization milestone, not an RMO phase and not a hardware phase.

Included commits:

- `bc3bd54012d1a1226f1062a86c15f73e922a2f14` Add operator database backup UI
- `e92436aa79afd6d0095c4c8ed693aac69c21881f` Add audit and error log viewer

## Backup UI

The shell Backup module now opens a real backup-only form.

Behavior:

- Shows the live database path from `DbPaths.DatabasePath`.
- Shows the backup folder from `DbPaths.BackupPath`.
- Creates local backup files named `salryn-backup-yyyyMMdd-HHmmss.db`.
- Uses `overwrite: false` so an existing destination is not silently replaced.
- Keeps backup execution behind `SalrynAppServices`, so the feature form does not reference Infrastructure directly.
- Does not add restore behavior.
- Does not delete, overwrite, or mutate the live database.
- Does not add cloud, network, schema, or migration behavior.

Manual smoke created:

`C:\Users\kirch\Salryn\.salryn-dev\backups\salryn-backup-20260628-164011.db`

The live dev database remained present at:

`C:\Users\kirch\Salryn\.salryn-dev\data\salryn.db`

## Audit / Error Log Viewer

The shell Audit module now opens a real read-only support viewer.

Behavior:

- Reads audit logs through `IAuditReadRepository`.
- Reads error logs through `IErrorLogReadRepository`.
- Exposes separate read-only grids for Audit Logs and Error Logs.
- Provides a Refresh action.
- Shows row-count status.
- Does not mutate logs.
- Does not issue direct SQL from the UI.
- Does not add schema or migration behavior.

Manual smoke loaded:

- `1` audit row.
- `0` error rows.
- Error Logs tab opened without crashing.

## Verification

Both implementation phases passed:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj`

Manual smoke passed:

- Dev app launched.
- Login succeeded.
- Backup module opened.
- Backup file was created.
- Live DB remained intact.
- Audit module opened.
- Refresh worked.
- Audit and Error Log grids stayed read-only.
- App did not crash.

## Boundaries

This milestone did not add or touch:

- RMO official output behavior
- Barcode scanner UX
- Printer configuration
- Printer hardware smoke
- Raw USB
- ESC/POS
- PDF
- Crystal Reports
- `.rpt` templates
- Schema migrations
- Permission model overhaul
- Product edit/deactivate
- Settings edit
- Users admin UI

## Known Warning

`SQLitePCLRaw.lib.e_sqlite3` NU1903 remains intentionally untouched.

## Remaining Gaps

- Users UI
- Product edit/deactivate
- Settings edit
- Scanner UX polish deferred
- Printer hardware/configuration deferred
- Production DB writability validation
- Backup restore policy remains deferred
- Audit filtering/paging/export remains deferred
