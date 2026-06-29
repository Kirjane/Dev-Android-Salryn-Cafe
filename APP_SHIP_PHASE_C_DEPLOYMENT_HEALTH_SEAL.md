# APP-SHIP-C Deployment Health Seal

## 1. Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Verified stable SHA: `cdddaeb`
- Date/context: APP-SHIP-C deployment health seal after C1/C2/C3 non-GUI production/startup readiness verification, recorded on 2026-06-29.

## 2. Scope

This seal covers the APP-SHIP-C deployment-health checkpoint.

Scope is limited to non-GUI production/startup readiness only.

## 3. Commands Recorded

The APP-SHIP-C commands are:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-production-readiness
```

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-first-run-bootstrap-readiness
```

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-startup-failure-diagnostics
```

## 4. C1 Summary

APP-SHIP-C1 (Production Deployment Readiness Smoke) proves:

- Production DB path readiness using existing production path logic.
- App data folder (database directory) can be created or already exists and is writable.
- Backup folder can be created or already exists and is writable.
- Database initialization status can be checked without launching WinForms.
- First-run required is detectable.
- Clear failure output if a path is unwritable (via probe file and exception text).
- No development-only path is required for the production readiness check.
- No DB initialization/migration unless existing status check safely does so.
- No users created, no fake data seeded, no schema mutated.
- Only a safe temp probe file (`.salryn-write-test.tmp`) is written and immediately deleted.
- Does not launch Salryn.App.WinForms.

## 5. C2 Summary

APP-SHIP-C2 (First-Run / Admin Bootstrap Recovery Smoke) proves:

- Uses an isolated temporary production-like app data root (never the real production DB).
- Resolves DB/backup paths using existing abstractions.
- Confirms no DB initially exists in the temp root.
- Runs existing database initialization/status check path.
- First-run required is detectable before admin bootstrap.
- Uses existing bootstrap/admin creation service path.
- Creates a temporary admin user only inside the isolated temp DB.
- Confirms the admin user can authenticate through the existing `AuthenticationService` path.
- Confirms store settings/bootstrap foundation exists or is detectable.
- Confirms first-run required becomes false after bootstrap (when architecture supports it).
- Clear failure messages on bootstrap/status failures.
- Cleans up the temporary folder/database at the end.
- Does not touch the real development or production DB.
- Does not launch Salryn.App.WinForms.

## 6. C3 Summary

APP-SHIP-C3 (Runtime Startup Failure Diagnostics Smoke) proves:

- Startup-adjacent failure diagnostics are exercised via existing initialization/path/status services.
- Invalid/unavailable database directory scenarios produce clear diagnostics (e.g., directory/file name conflict IOExceptions).
- Invalid DB file scenario (garbage content written to the .db path) surfaces a clear failure (`DatabaseInitialization.SqliteFailed` with "file is not a database").
- Backup path conflict scenario (file where directory is expected) produces clear diagnostic output from `EnsureDirectories` and connection factory paths.
- A normal success (happy path) comparison is also run on a clean temp root to demonstrate the smoke is not failure-only.
- All checks use isolated temp folders/files only.
- Does not launch Salryn.App.WinForms.
- Source guard enforces no references to forms, presenters, RMO, PDF, Crystal, ESC/POS, printer/scanner/hardware, LAN/mobile, or frontend redesign.

## 7. Verification Summary

Verification commands were executed and passed:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-phase-b-regression`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-production-readiness`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-first-run-bootstrap-readiness`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- app-startup-failure-diagnostics`

## 8. Known Warning

`SQLitePCLRaw.lib.e_sqlite3` NU1903 may appear and was intentionally left untouched.

## 9. Explicit Boundaries

- This seal does not claim BIR/RMO certification.
- This seal does not claim full RMO compliance.
- This seal does not certify production deployment.
- This seal does not include an installer.
- This seal does not include real production-machine smoke.
- This seal does not include GUI/manual smoke.
- This seal does not cover printer/scanner/hardware behavior.
- This seal does not cover PDF, Crystal, ESC/POS, or official output behavior.
- This seal does not cover LAN/mobile behavior.
- This seal does not include schema or migration work.
- This seal does not include frontend/aesthetic redesign work.

## 10. Remaining Risks

- Real installer/package is still missing.
- Production machine smoke is still needed.
- Backup restore operator procedure and manual proof are still needed.
- Hardware integration is still deferred.
- Frontend aesthetic transformation is still deferred.
