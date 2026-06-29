# POS Phase-B Stability Readiness Seal

## 1. Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Verified stable SHA: `30c1a1a55e6bfd7056bc3112e0b8212338d21268`
- Date/context: POS Phase-B stability seal after B1/B2/B3 regression harness verification, recorded on 2026-06-29.

## 2. Stability Scope

This seal covers POS/operator backend and application stability evidence only.

Completed POS foundation included in this stability scope:

- B1 basic store-day operations
- B2 backup UI + audit/error viewer
- B3 product edit/deactivate + store settings edit + production DB preflight
- B4 users admin MVP
- B5 backup recovery guardrails
- B6 audit operations filters
- B7 presentation/operator UX polish
- B8 sales history filters/paging
- POS-PHASE-B1 automated store-day regression harness
- POS-PHASE-B2 administrative operations regression harness
- POS-PHASE-B3 consolidated POS regression gate

## 3. Verified Regression Commands

The Phase-B POS regression evidence records these commands:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-store-day-regression
```

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-admin-operations-regression
```

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-phase-b-regression
```

Codex verification for B3 ran:

```powershell
git diff --check
dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build
dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-store-day-regression
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-admin-operations-regression
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-phase-b-regression
```

## 4. B1 Coverage Summary

POS-PHASE-B1 proves:

- no GUI launch
- product creation through repository path
- stock-in through inventory write repository
- cashier session open through `CashierSessionService`
- sale completion through `PosCheckoutApplicationService`
- sale lookup/history through read repositories
- stock decrease after sale
- void through `ISaleWriteRepository.MarkVoidedAsync`
- stock restoration after void
- cashier session close through `CashierSessionService`
- no open session remains

## 5. B2 Coverage Summary

POS-PHASE-B2 proves:

- product create/update/readback/deactivate
- store settings update/readback/restore
- users create/update/password/status/self-guard
- backup create/list/verify/live-intact
- audit/error filtering and paging
- sales history receipt/status/date/page filters

## 6. B3 Coverage Summary

POS-PHASE-B3 adds the consolidated command:

```text
pos-phase-b-regression
```

It runs:

1. `PosStoreDayRegressionSmokeChecks.Run(projectRoot);`
2. `PosAdminOperationsRegressionSmokeChecks.Run(projectRoot);`

This is the single Phase-B POS regression gate.

## 7. Known Warning

`SQLitePCLRaw.lib.e_sqlite3` NU1903 may appear and was intentionally left untouched.

## 8. Explicit Boundaries

- This seal does not claim BIR certification.
- This seal does not claim full RMO compliance.
- This seal does not certify production deployment.
- This seal does not cover printer/scanner/hardware behavior.
- This seal does not cover PDF, Crystal Reports, ESC/POS, or official output behavior.
- This seal does not cover LAN/mobile behavior.
- This seal does not authorize B9 feature work.
- This seal is limited to the verified POS Phase-B stability evidence listed above.

## 9. Next-State Recommendation

Recommended next step is to hold Phase B as the current stable POS baseline unless the Human Director explicitly authorizes a new scoped task.
