# CoreSmoke Dev-DB Isolation Repair Report

Date: 2026-07-15

Branch: `KIRCH-REPAIR-CORESMOKE-DEVDB-ISOLATION`

Base branch: `KIRCH-POS-SURFACE4-HARDWARE-SCOPE-BAN`

Base SHA: `ab6b58bd64c50988efdc9f0ef8e6c12c24babc61`

USER-M1 repair integrated SHA: `240665a823556f306e52dc415a71ecc2c4a22157`

Final SHA: supplied by final assistant response after this report commit.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Mission

Repair the broader smoke-isolation defect where CoreSmoke could mutate the active development database under `.salryn-dev`.

This branch does not clean existing polluted records. Existing `m1-cashier-*`, `m1-manager-*`, `USER_CREATED`, product, sale, payment, stock, or audit pollution remains untouched and must be handled only by a separate explicitly approved dev-only cleanup tool.

---

## Files changed

- `src/Salryn.Composition.Local/SalrynLocalComposition.cs`
- `tests/Salryn.Tests/CoreSmokeDevelopmentDatabaseIsolation.cs`
- `tests/Salryn.Tests/UserM1HostOnlyUserCreationChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/CORESMOKE_DEVDB_ISOLATION_REPAIR_REPORT.md`

No Surface4 UI/product behavior was changed.

No PRINTER1 runtime behavior was changed.

No Backup UI, LIC-FINAL, I1, RMO/BIR, eSales, official receipt/invoice, hardware support, or production activation work was done.

---

## PASS 1 findings

### Active dev DB mutation root cause

The active dev DB mutation root cause was not a single application endpoint. It was smoke infrastructure using development launch/composition paths against the repository root.

Risk patterns found:

1. `UserM1HostOnlyUserCreationChecks.cs` used `SalrynLocalComposition.CreateDevelopment(projectRoot)` before the proven USER-M1 repair. That writes user/audit data into the active `.salryn-dev` database when `projectRoot` is the repository root.
2. Several API runtime checks construct or indirectly construct dev-mode local API composition with `--dev`. If the test does not pass an isolated composition, `LocalApiApplicationFactory` resolves development composition through `SalrynLocalLaunchModeResolver`, which resolves the repository root and therefore active `.salryn-dev`.
3. Some runtime checks explicitly call `SalrynLocalComposition.CreateDevelopment(projectRoot)` before creating a `LocalApiApplicationFactory` test app. These are write-capable checks and were the likely source of full-CoreSmoke hash/timestamp mutation.

### Confirmed risky checks / patterns

- `UserM1HostOnlyUserCreationChecks.cs`: user creation runtime smoke. Writes users and audit rows.
- `RoleM3HttpApiPermissionRuntimeChecks.cs`: API runtime smoke using dev launch without explicit composition. Mostly auth/read boundary, but initialization can touch dev DB.
- `RoleM4PermissionBoundaryRegressionMatrixChecks.cs`: API runtime matrix using dev launch without explicit composition. Mostly auth/read boundary, but initialization can touch dev DB.
- `V2HostOperatorShiftApiChecks.cs`: explicit `CreateDevelopment(projectRoot)` and shift open/close runtime writes.
- `V2HostOperatorCheckoutApiChecks.cs`: explicit `CreateDevelopment(projectRoot)` plus product, shift, checkout, sale/payment/stock writes.
- `CleanupA1OperationalHardeningChecks.cs`: dev-mode Local API factory call without isolated composition when frontend dist exists.

### Already-safe checks

Many later POS/runtime checks were already using unique temp roots directly, for example PRINTER1 and MANUAL1/R3 runtime tests. Those tests create roots under `Path.GetTempPath()`, initialize their own SQLite database, and clear pools/delete temp roots in finalizers.

Read-only source guard checks are also safe because they read repository files only and do not create local composition or initialize SQLite.

### Exact full-CoreSmoke defect

Full CoreSmoke runs a mix of source guards, runtime API checks, and write-path smoke checks. Any legacy check that calls `CreateDevelopment(projectRoot)` or constructs `LocalApiApplicationFactory` in `--dev` mode without an isolated composition can mutate `.salryn-dev` during full CoreSmoke.

---

## Implementation

### USER-M1 proven repair integrated

Cherry-pick equivalent of `240665a823556f306e52dc415a71ecc2c4a22157` was integrated:

- `UserM1HostOnlyUserCreationChecks.cs` now creates `salryn-user-m1-*` under the OS temp directory.
- USER-M1 creates its runtime composition from that isolated temp root.
- USER-M1 compares its isolated DB path against the active development DB path and throws if they match.
- USER-M1 clears SQLite pools and deletes its temp root on cleanup.
- CoreSmoke `RunOnly` now exposes `user-m1-host-only-user-creation`.

### CoreSmoke-wide isolation hook

Added `tests/Salryn.Tests/CoreSmokeDevelopmentDatabaseIsolation.cs`.

Behavior:

- Detects the `Salryn.Tools.CoreSmoke` process through a module initializer.
- Creates a unique temp root: `salryn-coresmoke-devdb-*`.
- Sets `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT` for the CoreSmoke process only.
- Clears SQLite pools and best-effort deletes the temp root on process exit.

### Composition-level guard

Updated `SalrynLocalComposition.CreateDevelopment(projectRoot)` to honor `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT` only when the supplied root is an actual repository root containing `Salryn.sln`.

That matters because:

- legacy smoke checks passing the repository root are redirected away from `.salryn-dev`;
- tests that already pass explicit temp roots are not overridden;
- normal production/dev runtime behavior is unchanged when the environment variable is absent;
- the redirect checks that the isolated DB path does not equal the active dev DB path.

---

## PASS 2 findings

### Remaining `CreateDevelopment(projectRoot)` risk

Legacy calls can still exist in source, but when executed under `Salryn.Tools.CoreSmoke`, repository-root `CreateDevelopment(projectRoot)` calls are redirected to the CoreSmoke temp root.

Explicit temp-root calls remain unchanged. This preserves tests that already use isolated temp roots.

### Local API dev launch risk

`LocalApiApplicationFactory.CreateAsync(new[] { "--dev" }, ...)` without explicit composition is also covered because dev launch eventually calls `SalrynLocalComposition.CreateDevelopment(repositoryRoot)`, which is redirected inside CoreSmoke.

### Cleanup risk

The isolation hook clears SQLite pools before deleting the CoreSmoke temp root. Cleanup is best-effort and ignores IO/permission failures to avoid masking the actual smoke failure. Temp-root cleanup must still be confirmed by SOL/local gate output.

### USER-M1 repair still present

USER-M1 retains its own isolated temp root and cleanup behavior. The CoreSmoke-wide redirect is limited to repository roots, so it does not override USER-M1's explicit temp root.

### Surface4 preserved

Surface4 hardware-scope ban files were not modified by this branch.

### No production runtime behavior changed

When `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT` is not set, `SalrynLocalComposition.CreateDevelopment(projectRoot)` keeps the original behavior.

### Existing polluted records

No delete/cleanup logic for existing polluted active dev DB records was added.

---

## Active dev DB verification

Local execution is not available from this chat.

The following values must be recorded by SOL/local verification:

- before hash: `NOT RUN / NEEDS SOL`
- after focused hash: `NOT RUN / NEEDS SOL`
- after full CoreSmoke hash: `NOT RUN / NEEDS SOL`
- timestamp before/after: `NOT RUN / NEEDS SOL`
- pollution check: `NOT RUN / NEEDS SOL`

Expected SOL/local result:

- active `.salryn-dev\data\salryn.db` hash unchanged;
- active `.salryn-dev\data\salryn.db` `LastWriteTimeUtc` unchanged;
- no new `m1-cashier-*` or `m1-manager-*` rows in active dev DB;
- no new `USER_CREATED` audit rows in active dev DB from CoreSmoke;
- no other smoke-created product/sale/payment/stock/audit pollution in active dev DB.

---

## Existing polluted data

Existing polluted data was not deleted.

Cleanup is deferred to a separate dev-only cleanup tool if Kirch approves it.

---

## Not touched

- Surface4 behavior
- PRINTER1 runtime
- Backup UI
- LIC-FINAL
- I1 installer
- RMO/BIR implementation
- official receipt/invoice implementation
- eSales implementation/transmission
- hardware support
- production activation
- protected refs

---

## Required gates

```powershell
cd C:\Users\kirch\Salryn

git fetch origin
git checkout KIRCH-REPAIR-CORESMOKE-DEVDB-ISOLATION
git reset --hard origin/KIRCH-REPAIR-CORESMOKE-DEVDB-ISOLATION

git rev-parse HEAD
git status

$DevDb = "C:\Users\kirch\Salryn\.salryn-dev\data\salryn.db"
if (Test-Path $DevDb) {
  Get-FileHash $DevDb -Algorithm SHA256
  Get-Item $DevDb | Select-Object FullName, Length, LastWriteTimeUtc
} else {
  "NO ACTIVE DEV DB FOUND"
}

dotnet build Salryn.sln

dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only user-m1-host-only-user-creation
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-printer1-windows-printer-wiring
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-r3-frontend-safety-ux-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-r1-owner-reports
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-surface1-role-page-lock-store-nav

if (Test-Path $DevDb) {
  Get-FileHash $DevDb -Algorithm SHA256
  Get-Item $DevDb | Select-Object FullName, Length, LastWriteTimeUtc
} else {
  "NO ACTIVE DEV DB FOUND"
}

dotnet run --project tools\Salryn.Tools.CoreSmoke

if (Test-Path $DevDb) {
  Get-FileHash $DevDb -Algorithm SHA256
  Get-Item $DevDb | Select-Object FullName, Length, LastWriteTimeUtc
} else {
  "NO ACTIVE DEV DB FOUND"
}

npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status:

- build: `NOT RUN / NEEDS SOL`
- USER-M1: `NOT RUN / NEEDS SOL`
- PRINTER1: `NOT RUN / NEEDS SOL`
- R3: `NOT RUN / NEEDS SOL`
- R1: `NOT RUN / NEEDS SOL`
- SURFACE1: `NOT RUN / NEEDS SOL`
- full CoreSmoke: `NOT RUN / NEEDS SOL`
- web build: `NOT RUN / NEEDS SOL`
- diff check: `NOT RUN / NEEDS SOL`
- git status: `NOT RUN / NEEDS SOL`

No `CANDIDATE_AUTOMATED_PROVEN` claim is made from this chat.

---

## Protected refs

No write operation was issued to:

- `KIRCH-SANDBOX`
- `KIRCH-INSTALLER-M1`

---

## Final status

`READY_FOR_SOL_VERIFICATION`
