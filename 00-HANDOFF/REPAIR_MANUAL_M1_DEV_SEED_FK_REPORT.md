# REPAIR-MANUAL-M1 — Dev Seed FK Repair

## 1. Branch / base

Branch: `KIRCH-REPAIR-MANUAL-M1-FIRST-RUN-DEV-SEED`

Starting known good auth SHA for this repair: `f3d4575fe942a6315f9c1dac821ca1a03982d114`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the final response or repository branch head after this report commit for exact final SHA.

## 2. Scope

Bug ID: `MP-M1-007 — Dev product seed fails FK constraint.`

This is a narrow repair for `tools/Salryn.Tools.DevData seed-general-store --count 1000 --confirm-dev-seed` failing with SQLite foreign-key error after normal database initialization, first-run bootstrap, admin login proof, and app shutdown.

This lane does not launch the app, reset DB with confirmation, seed the live dev DB with confirmation, create a live admin, resume full manual proof, touch installer/launcher/RMO/reporting/licensing/product images, promote branches, or modify protected refs.

## 3. Root cause

`seed-general-store` inserted units, categories, and brands using deterministic stable dev IDs with `INSERT OR IGNORE`.

Normal database initialization already seeds default units/categories/brands. If a dev dimension insert collided with an existing unique name, `INSERT OR IGNORE` no-oped and did not create the deterministic stable dev ID.

Product inserts still referenced:

- `StableGuid("unit:" + p.Unit)`
- `StableGuid("category:" + p.Category)`
- `StableGuid("brand:" + p.Brand)`

When the deterministic ID did not exist because the insert was ignored, the product row referenced a non-existent `unit_id`, `category_id`, or `brand_id`, causing SQLite foreign-key failure.

## 4. Exact mismatch found

Dimension path before repair:

```csharp
INSERT OR IGNORE INTO units/categories/brands (... id ...)
VALUES (StableGuid(...), ...);
```

Product path before repair:

```csharp
["$categoryId"] = StableGuid("category:" + p.Category),
["$brandId"] = StableGuid("brand:" + p.Brand),
["$unitId"] = StableGuid("unit:" + p.Unit),
```

The bug was ID assumption after an ignored insert. It was not a product-count generator issue and not a product-image issue.

## 5. Fix summary

`seed-general-store` now resolves actual dimension IDs after attempting to insert each required unit/category/brand.

For every dimension used by generated products:

1. insert the row if missing
2. query the actual row ID by unique natural key
   - unit: unit name from abbreviation mapping
   - category: name
   - brand: name
3. reactivate the row if needed
4. store the actual ID in a dictionary
5. insert products using the resolved actual IDs instead of assuming deterministic stable IDs exist

Product rows now use:

```csharp
["$categoryId"] = categoryIds[p.Category],
["$brandId"] = brandIds[p.Brand],
["$unitId"] = unitIds[p.Unit],
```

The seed still requires exactly `1000` products and preserves existing verification:

- `Seeded products: 1000`
- `Unique SKUs: 1000`
- `Unique names: 1000`
- `Unique barcodes: 1000`
- no product images, image paths, image URLs, or media fields

## 6. Executable test/gate coverage

Added:

- `tests/Salryn.Tests/ManualM1DevSeedFkRepairChecks.cs`

The check performs this temp-DB flow:

1. create a temporary `.salryn-dev` SQLite path
2. initialize database through normal local composition
3. verify first-run is required and no users/store settings exist
4. create first admin with `admin / StrongAdmin123!`
5. verify first-run is completed
6. run the DevData seed helper against the temp DB with count `1000`
7. verify seeded product count is `1000`
8. verify unique SKUs/names/barcodes are each `1000`
9. verify no seeded products have missing category, brand, or unit FK joins
10. verify seeded products keep the no-product-images note
11. clear SQLite pools and delete the temp path

The existing focused CoreSmoke gate `manual-m1-bootstrap-auth-repair` now executes the bootstrap-auth proof and then invokes `ManualM1DevSeedFkRepairChecks.Run()` so the required temp-DB seed FK proof is executable through:

```powershell
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only manual-m1-bootstrap-auth-repair
```

## 7. Gates

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

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only manual-m1-bootstrap-auth-repair
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-lan-bind-truth
dotnet run --project tools/Salryn.Tools.CoreSmoke

dotnet run --project tools\Salryn.Tools.DevData -- --help
dotnet run --project tools\Salryn.Tools.DevData -- reset-db
dotnet run --project tools\Salryn.Tools.DevData -- seed-general-store --count 1000

npm --prefix src/Salryn.App.Web run build

git diff --check
git status
```

Do not run confirmed seed until the manual sequence reaches the seed step again.

## 8. Required manual proof after fix

Manual proof still requires Kirch/Sol local execution:

1. reset dev DB
2. launch app
3. complete bootstrap
4. login admin
5. stop app
6. run:

```powershell
dotnet run --project tools\Salryn.Tools.DevData -- seed-general-store --count 1000 --confirm-dev-seed
```

Expected output includes:

- `Seeded products: 1000`
- `Unique SKUs: 1000`
- `Unique names: 1000`
- `Unique barcodes: 1000`
- `No product images, image paths, image URLs, or media fields were seeded.`

## 9. Non-claims

This connector session does not claim:

- local restore result
- local build result
- local focused CoreSmoke result
- local full CoreSmoke result
- local DevData guard result
- local web build result
- confirmed live dev seed success
- browser/manual proof completion
- beta readiness
- production readiness

## 10. Status

Status from this connector session: `CANDIDATE_PENDING_SOL_REVERIFY`.

Acceptance requires Sol/local verification of:

- branch head
- changed files
- protected refs unchanged
- build
- focused CoreSmoke `manual-m1-bootstrap-auth-repair`
- full CoreSmoke
- DevData unconfirmed guards
- web build
- confirmed manual seed output after app/bootstrap/login proof
