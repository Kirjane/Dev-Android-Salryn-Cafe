# Salryn QA Handoff - Stable V1, No Multi-Tenant Yet

Status: QA-ready desktop POS build.

Branch:

stable/v1-no-multi-tenant-yet

Tag:

stable-v1-no-multi-tenant-yet

## Scope

This QA build is the local/offline Windows desktop POS MVP.

Included:

- first-run manual admin setup
- login
- store settings created during first-run admin setup
- QA product seed after manual admin setup
- product management
- seeded catalog foundation through database initialization
- cashier shift gate
- POS checkout
- barcode/product search
- draft cart
- cash payment
- completion dry run
- submit guard
- complete sale
- sale completion modal
- stock reduction after sale
- product list stock verification
- 1366x768 checkout action visibility repair

Not included yet:

- multi-tenant
- cloud sync
- LAN/multi-machine production runtime
- official BIR/accreditation finalization
- installer packaging
- role/user management UI polish
- production receipt compliance final pass

## QA Rule: Manual Admin, Seeded Products

Do not use shared login credentials for QA.

The QA tester should create the first admin manually, then run the product-only QA seed.

The product-only QA seed does not create users, passwords, or admin accounts.

## Clean QA Setup

PowerShell:

cd "C:\Users\<YOUR_USER>\Salryn"
git checkout stable/v1-no-multi-tenant-yet
Remove-Item ".\.salryn-dev" -Recurse -Force -ErrorAction SilentlyContinue

Build:

dotnet build ".\Salryn.sln" /warnaserror:CS8602

Launch first-run setup:

dotnet run --project ".\src\Salryn.App.WinForms\Salryn.App.WinForms.csproj" -- --dev

Expected first screen:

first-run admin setup

Create your own admin username/password.

Close the app after first-run setup/login succeeds.

Then seed QA products only:

dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build qa-products-seed

Launch again:

dotnet run --project ".\src\Salryn.App.WinForms\Salryn.App.WinForms.csproj" -- --dev

Login with your own admin credentials.

## QA Product Barcodes

- 941000000001 | QA Coffee Sachet | PHP 15.00
- 941000000002 | QA Bottled Water 500ml | PHP 20.00
- 941000000003 | QA Rice 1kg | PHP 58.00

## QA Build/Smoke Commands

PowerShell:

dotnet build ".\Salryn.sln" /warnaserror:CS8602
dotnet test ".\Salryn.sln" --no-build
dotnet run --project ".\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj" --no-build
dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build auth-first-run
dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build mvp-business-flow

Developer-only full dev seed exists:

dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build dev-prelaunch-seed

Do not use dev-prelaunch-seed for manual QA first-run testing because it creates a shared admin.

Use qa-products-seed after manual admin setup.

## Manual QA Flow

1. Reset `.salryn-dev`.
2. Launch app with `--dev`.
3. Create first admin manually.
4. Close app.
5. Run `qa-products-seed`.
6. Launch app with `--dev` again.
7. Login with your own admin.
8. Open Products and verify QA products.
9. Open POS Checkout.
10. Open cashier shift.
11. Search product by barcode/name.
12. Add product to draft cart.
13. Enter cash amount.
14. Click Add Cash Payment.
15. Click Validate Completion Dry Run.
16. Click Prepare Submit Guard.
17. Click Complete Sale.
18. Confirm completion modal.
19. Verify sale completed modal.
20. Click Start New Sale.
21. Open Products and verify stock reduction.

## Known QA Notes

- This is stable V1 local/offline POS.
- This is not multi-tenant yet.
- Use development mode for QA.
- Production database should not be touched during QA.
- `.salryn-dev` is ignored by git and stays local to each tester.