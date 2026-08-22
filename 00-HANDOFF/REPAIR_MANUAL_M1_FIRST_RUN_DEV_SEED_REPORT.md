# REPAIR-MANUAL-M1 — First-Run Admin Setup + Dev DB Reset/Seed Stability

## 1. Branch / Base

Branch: `KIRCH-REPAIR-MANUAL-M1-FIRST-RUN-DEV-SEED`

Base branch: `KIRCH-REPAIR-MANUAL-M1-LAN-BIND-FALLBACK`

Base SHA: `5e9a7e79093079954381c35f13e7d40ffa1b6e60`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the Freehand final response or repository branch head after this report commit for the exact final SHA.

## 2. Scope

Repair milestone: `REPAIR-MANUAL-M1 — First-Run Admin Setup + Dev DB Reset/Seed Stability`

This lane addresses:

- `MP-M1-001 Fresh DB has no usable first-run setup/admin route.`
- `MP-M1-005 No safe dev reset + seed workflow exists for Manual Proof M1.`

This lane stacks on the LAN-bind fallback candidate for `MP-M1-002`.

This lane does not resume full manual proof, start beta, start Cloud, implement installer/update behavior, create a desktop dev launcher, promote branches, touch protected refs, change RMO/BIR/eSales semantics, or add product image functionality.

## 3. Files changed

- `src/Salryn.Infrastructure/Data/Initialization/DatabaseInitializationOrchestrator.cs`
- `src/Salryn.Api.Local/LocalBootstrapEndpoints.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.App.Web/src/api/bootstrap.ts`
- `src/Salryn.App.Web/src/pages/BootstrapSetupPage.tsx`
- `src/Salryn.App.Web/src/App.tsx`
- `tools/Salryn.Tools.DevData/Salryn.Tools.DevData.csproj`
- `tools/Salryn.Tools.DevData/Program.cs`
- `tests/Salryn.Tests/ManualM1FirstRunDevSeedRepairChecks.cs`
- `00-HANDOFF/REPAIR_MANUAL_M1_FIRST_RUN_DEV_SEED_REPORT.md`

No installer file, desktop launcher, protected branch, RMO/BIR/eSales semantic path, report/export path, or product-image path was intentionally changed.

## 4. MP-M1-001 root cause

A fresh initialized dev DB could launch the app and render login, but there was no visible setup route to create the first admin/store settings. The infrastructure already had `FirstRunBootstrapper`, but local API/frontend did not expose a first-run setup flow for manual proof.

Observed result: `admin / StrongAdmin123!` failed on a fresh DB and manual proof dead-ended at login.

## 5. First-run fix summary

Backend:

- Added `DatabaseInitializationOrchestrator.CreateFirstAdmin(AdminBootstrapRequest request)` to reuse the existing `FirstRunBootstrapper` without duplicating password hashing or insert logic.
- Added `GET /api/bootstrap/status`.
- Added `POST /api/bootstrap/first-admin`.
- Mapped bootstrap endpoints from `LocalApiApplicationFactory`.
- Kept bootstrap endpoints loopback-local through `LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest`.
- `POST /api/bootstrap/first-admin` still fails closed through `FirstRunBootstrapper` after first-run is completed or users exist.

Frontend:

- Added `src/Salryn.App.Web/src/api/bootstrap.ts`.
- Added `BootstrapSetupPage`.
- Updated `App.tsx` so `firstRunRequired` routes the local console to `/bootstrap` instead of leaving the operator at login.
- Bootstrap page collects store name, optional business/address/contact/TIN, receipt width 58/80, admin username, admin display name, password, and confirm password.

## 6. Dev reset command summary

Added dev-only tool project:

```powershell
dotnet run --project tools\Salryn.Tools.DevData -- reset-db --confirm-dev-reset
```

Reset behavior:

- targets `.salryn-dev/data/salryn.db` through `DbPaths.CreateDevelopment(projectRoot)`
- refuses paths outside `.salryn-dev`
- refuses ProgramData production paths
- requires `--confirm-dev-reset`
- creates `.salryn-dev/data/manual-backups`
- backs up current dev DB to `salryn-dev-reset-YYYYMMDD-HHMMSS.db` before removal
- does not touch licenses, report artifacts, backups outside the dev data path, or ProgramData
- does not create admin and does not seed products

## 7. Dev seed command summary

Added dev-only seed command:

```powershell
dotnet run --project tools\Salryn.Tools.DevData -- seed-general-store --count 1000 --confirm-dev-seed
```

Seed behavior:

- targets `.salryn-dev/data/salryn.db`
- refuses ProgramData production paths
- requires `--confirm-dev-seed`
- requires exactly `--count 1000`
- requires first-run setup to be complete first
- refuses existing products unless `--replace-dev-products` is provided
- refuses replacement if `sale_items` exist
- verifies inserted count and distinct SKU/name/barcode counts after insert

## 8. Seed data profile summary

Seed profile: Philippine general merchandise + hardware store.

The generator is deterministic and produces exactly 1,000 products from 20 categories x 10 item bases x 10 realistic specs.

Category coverage includes:

- Hardware
- Electrical
- Plumbing
- Paint and Supplies
- Tools
- Fasteners
- Construction Materials
- Automotive
- Home Cleaning
- Kitchenware
- School and Office Supplies
- Personal Care
- Beverages
- Snacks
- Canned Goods
- Rice and Staples
- Noodles
- Condiments
- Laundry
- Pet and Garden

Examples represented in source include `GI wire`, `PVC elbow`, `LED bulb`, `Hammer`, `Sardines`, `Corned beef`, `Rice`, `Instant noodles`, `Laundry powder`, and `Garden hose`.

Uniqueness rules implemented:

- 1,000 distinct SKUs using `DEV-GMHW-0001` style identifiers
- 1,000 distinct generated barcodes
- 1,000 distinct product names
- category + product name uniqueness validation
- duplicate generation fails before insert
- post-insert distinct count verification fails if counts do not match 1,000

Product images: not added. No image upload, image path, image URL, or media field was added.

## 9. Safety guards

- Reset requires `--confirm-dev-reset`.
- Seed requires `--confirm-dev-seed`.
- Tool resolves the repository root and uses `.salryn-dev` development DB path by default.
- Tool refuses non-dev paths.
- Tool refuses ProgramData production paths.
- Seed refuses to run before first-run setup is complete.
- Seed refuses count values other than 1,000.
- Seed refuses to overwrite existing inventory unless explicit replace flag is supplied.
- Replace flag refuses to run if sale items exist.
- No production seed path was added.

## 10. Tests added / updated

Added source-boundary check:

- `tests/Salryn.Tests/ManualM1FirstRunDevSeedRepairChecks.cs`

It checks:

- existing bootstrapper is reused
- bootstrap endpoints exist
- bootstrap endpoints are loopback-local
- app routes first-run DBs to `/bootstrap`
- bootstrap page includes required setup fields
- dev reset has confirmation and backup guards
- dev reset/seed target `.salryn-dev`
- ProgramData production paths are refused
- dev seed requires first-run completion
- dev seed targets exactly 1,000 products
- dev seed reports unique SKUs/names/barcodes
- dev seed rejects duplicates and enforces cost below selling price
- realistic product examples exist
- product image/media fields are not added

No focused CoreSmoke runner was registered in this connector session.

## 11. Gate results

No local PowerShell commands were run by this ChatGPT connector session.

Local gates still required:

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
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

Because the dev tool is new, local verification should also run:

```powershell
dotnet run --project tools\Salryn.Tools.DevData -- --help
```

## 12. Manual verification instructions

### Step 1 — reset dev DB

```powershell
cd C:\Users\kirch\Salryn
dotnet run --project tools\Salryn.Tools.DevData -- reset-db --confirm-dev-reset
```

Expected:

- current `.salryn-dev\data\salryn.db` backed up if present
- active dev DB removed/reset
- no production data touched

### Step 2 — start app

```powershell
Remove-Item Env:\SALRYN_LAN_BIND_IP -ErrorAction SilentlyContinue
Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
Remove-Item Env:\DOTNET_URLS -ErrorAction SilentlyContinue

$env:SALRYN_APP_ENV = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run --project src\Salryn.Api.Local\Salryn.Api.Local.csproj --launch-profile "Salryn.Api.Local Development"
```

Expected:

- starts at `http://127.0.0.1:5299`
- does not crash on stale LAN bind
- fresh DB routes to `/bootstrap`

### Step 3 — create admin in UI

Use:

```text
Store name: Salryn Dev General Merchandise
Admin username: admin
Admin display name: DEV Admin
Admin password: StrongAdmin123!
Receipt width: 80
```

Expected:

- first admin created
- first-run completed
- user can sign in

### Step 4 — close app

Stop the local API process.

### Step 5 — seed products

```powershell
dotnet run --project tools\Salryn.Tools.DevData -- seed-general-store --count 1000 --confirm-dev-seed
```

Expected:

- 1,000 realistic products seeded
- 1,000 unique SKUs
- 1,000 unique product names
- 1,000 unique barcodes
- no product images
- categories/brands/stock/prices generated

### Step 6 — restart app and inspect

Expected:

- login works
- Products page shows seeded products
- Inventory page shows stock
- search/filter works where supported
- low-stock/out-of-stock items are present

## 13. Remaining defects

Remaining after this candidate until local proof says otherwise:

- LAN fallback base is still a stacked candidate unless separately accepted.
- Manual reset/setup/seed workflow has not been locally run by this ChatGPT connector session.
- Full Manual Proof M1 is not resumed in this lane.
- Installer/update proof is still later.
- Branch promotion remains not done.

## 14. Required report answers

| Question | Answer |
| --- | --- |
| Did fresh DB still dead-end at login? | Expected no after fix; manual UI proof still required. |
| Is first-run setup available? | Yes at source/API/frontend level. |
| Does first-run setup fail after admin exists? | Yes through existing `FirstRunBootstrapper` conflict guard. |
| Does dev reset backup before reset? | Yes at source level. |
| Does dev reset refuse production paths? | Yes at source level. |
| Does dev seed require first-run setup complete? | Yes at source level. |
| Does dev seed create 1000 realistic products? | Yes by deterministic generator/source design; local run proof still required. |
| Were product images added? | No. |
| Did you modify installer/launcher/promote branches? | No. |

## 15. Non-claims

This lane does not claim:

- GREEN local verification
- manual reset/setup/seed success
- full manual proof completion
- beta readiness
- installer implementation
- dev launcher
- branch promotion
- production seed
- product image feature
- Cloud implementation
- payment/subscription work
- BIR approval
- legal approval
- eSales production transmission

## 16. Status

REPAIR-MANUAL-M1 first-run/dev-seed is a connector-authored candidate only until Sol/Codex/local verification proves:

- remote branch truth
- changed files
- protected refs unchanged
- final SHA
- local gates
- manual reset/setup/seed launch evidence

After acceptance, Manual Proof M1 can restart from this repair branch or a deliberate consolidated manual-proof base.
