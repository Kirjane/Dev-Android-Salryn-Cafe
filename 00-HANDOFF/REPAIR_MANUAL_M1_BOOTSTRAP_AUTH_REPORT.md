# REPAIR-MANUAL-M1 — Bootstrap Admin Authentication Repair

## 1. Branch / base

Branch: `KIRCH-REPAIR-MANUAL-M1-FIRST-RUN-DEV-SEED`

Starting SHA for this repair: `599dae9dd2b5655b3c7f32ce14a918274398e088`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the final response or repository branch head after this report commit for exact final SHA.

## 2. Scope

Bug ID: `MP-M1-006 — First-run bootstrap admin cannot authenticate.`

This is a narrow P1 repair for the runtime defect where first-run setup completed but the created admin could not authenticate through `/api/auth/login`.

This lane does not seed products, resume full manual proof, start beta, start Cloud, modify installer/update behavior, create a launcher, promote branches, modify protected refs, change RMO/BIR/eSales semantics, change reporting/export, change licensing behavior, or add product images.

## 3. Root cause

Manual runtime proof showed:

1. fresh dev DB initialized correctly
2. `/api/bootstrap/status` reported first-run required with no users/store settings
3. `/bootstrap` created the first admin and completed first-run state
4. `/api/bootstrap/status` then reported `firstRunRequired=false`, `hasUsers=true`, `hasStoreSettings=true`
5. normal login and direct `/api/auth/login` with `admin / StrongAdmin123!` returned `401 Unauthorized`

Code investigation found a password hash format mismatch:

- `FirstRunBootstrapper` created the bootstrap admin password hash through `Infrastructure.Security.Pbkdf2PasswordHasher`.
- That hasher emits pipe-delimited hashes with marker `PBKDF2-SHA256|...`.
- Normal authentication verifies through `Core.Security.SalrynPasswordHasher`.
- `SalrynPasswordHasher` expects SALRYN's dollar-delimited local auth format: `PBKDF2-SHA256.V1$...`.

Result: bootstrap inserted a user row with correct username, active status, admin role id, and store/settings completion, but the stored password hash was in a format normal login rejects.

## 4. Exact mismatch found

Bootstrap insert path before this repair:

```csharp
string passwordHash = _passwordHasher.HashPassword(request.AdminPassword);
```

Normal login path:

```csharp
bool passwordValid = SalrynPasswordHasher.VerifyPassword(
    request.Password,
    credential.PasswordHash);
```

The mismatch was hash format/provider, not username casing, status, role join, or first-run metadata.

## 5. Fix summary

Changed `FirstRunBootstrapper` so it stores bootstrap admin hashes using the same `SalrynPasswordHasher.HashPassword(...)` provider that normal login verifies with `SalrynPasswordHasher.VerifyPassword(...)`.

Also updated `DefaultDatabaseInitializationFactory` and `Phase8FirstRunBootstrapCompileChecks` for the simplified bootstrapper constructor.

The admin insert still uses:

- trimmed username
- trimmed display name
- `DefaultSeedData.AdminRoleId`
- `status = 1`
- existing store/settings insert
- existing first-run metadata completion
- existing audit log insert

No permanent unauthenticated admin endpoint was added. Existing bootstrap endpoint still fails closed after first-run is complete/users exist.

## 6. Tests added

Added actual temp-DB automated smoke check:

- `tests/Salryn.Tests/ManualM1BootstrapAuthRepairChecks.cs`

The check performs this end-to-end flow:

1. creates a temporary development SQLite path
2. initializes the database
3. verifies first-run is required and no users/store settings exist
4. calls `DatabaseInitialization.CreateFirstAdmin(...)` with:
   - username `admin`
   - display name `DEV Admin`
   - password `StrongAdmin123!`
   - store name `Salryn Dev General Merchandise`
   - receipt width `80`
5. verifies first-run is completed and users/store settings exist
6. authenticates through the same `composition.Authentication.LoginAsync(...)` path used by `/api/auth/login`
7. asserts authenticated role is `Admin`
8. asserts wrong password fails
9. asserts duplicate first-admin bootstrap fails after completion
10. clears SQLite pools and deletes the temp DB directory

Focused CoreSmoke runner was not registered in this connector pass. Sol should run this check through CoreSmoke registration or direct local harness wiring before GREEN is claimed.

## 7. Duplicate bootstrap proof

The new automated check calls `CreateFirstAdmin(...)` a second time after first-run completion and fails the test if that duplicate bootstrap succeeds.

Expected behavior remains: first-admin bootstrap is only available while first-run is required and no users exist.

## 8. Wrong password proof

The new automated check attempts login with `admin / WrongAdmin123!` after bootstrap and fails the test if wrong password authentication succeeds.

## 9. Gates

No local PowerShell commands were run by this ChatGPT connector session.

Required Sol/local reverify:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-REPAIR-MANUAL-M1-FIRST-RUN-DEV-SEED
git status
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-INSTALLER-M1

dotnet restore Salryn.sln
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m3-local-enforcement-boundaries
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only harden-a1-a6-pre-beta-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-lan-bind-truth
dotnet run --project tools/Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.DevData -- --help
dotnet run --project tools\Salryn.Tools.DevData -- reset-db
dotnet run --project tools\Salryn.Tools.DevData -- seed-general-store --count 1000
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

Do not run confirmed seed until manual/API login proof passes.

## 10. Required manual/API proof after fix

From fresh DB:

1. reset dev DB
2. launch app
3. `/api/bootstrap/status` must show `firstRunRequired=true`, `hasUsers=false`
4. create first admin through `/bootstrap`
5. direct `POST /api/auth/login` with `admin / StrongAdmin123!` must return authenticated session
6. browser login must work
7. only then product seed may proceed

This connector session did not launch the app, reset DB with confirmation, create admin, or seed products.

## 11. Non-claims

This lane does not claim:

- GREEN local verification
- manual/API login proof
- product seed success
- full Manual Proof M1 completion
- beta readiness
- Cloud readiness
- installer/update implementation
- launcher creation
- branch promotion
- RMO/BIR/eSales changes
- reporting/export changes
- licensing semantic changes
- product image functionality
- production readiness

## 12. Status

Status from this connector session: `CANDIDATE_PENDING_SOL_REVERIFY`.

Acceptance requires Maintainer/Sol verification of:

- remote branch truth
- changed files
- protected refs unchanged
- final SHA
- local gates
- temp-DB bootstrap-to-auth check
- manual/API proof that `admin / StrongAdmin123!` authenticates after `/bootstrap`
