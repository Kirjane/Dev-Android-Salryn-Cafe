# Salryn QA Handoff - Stable V1, No Multi-Tenant Yet

Status: QA-ready desktop POS build.

Branch:

stable/v1-no-multi-tenant-yet

Tag:

stable-v1-no-multi-tenant-yet

Base commit:

8c2f0ec Repair checkout 768p action visibility

## Scope

This QA build is the local/offline Windows desktop POS MVP.

Included:

- first-run manual admin setup
- login
- store settings created during first-run admin setup
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

## QA Rule: Empty Login Setup

Do not use seeded login credentials for QA.

The QA tester should create the first admin manually.

Before QA launch, reset the local development database:

PowerShell:

cd "C:\Users\<YOUR_USER>\Salryn"
Remove-Item ".\.salryn-dev" -Recurse -Force -ErrorAction SilentlyContinue

Then launch the app in development mode:

dotnet run --project ".\src\Salryn.App.WinForms\Salryn.App.WinForms.csproj" -- --dev

Expected first screen:

first-run admin setup

The tester should create their own admin username/password.

## QA Build/Smoke Commands

PowerShell:

dotnet build ".\Salryn.sln" /warnaserror:CS8602
dotnet test ".\Salryn.sln" --no-build
dotnet run --project ".\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj" --no-build
dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build auth-first-run
dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build mvp-business-flow

Optional local dev seed command exists for developer convenience only:

dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build dev-prelaunch-seed

Do not use the dev seed command for manual QA first-run testing.

## Manual QA Flow

1. Reset `.salryn-dev`.
2. Launch app with `--dev`.
3. Create first admin manually.
4. Login.
5. Open Products.
6. Add or verify products.
7. Open POS Checkout.
8. Open cashier shift.
9. Search product by barcode/name.
10. Add product to draft cart.
11. Enter cash amount.
12. Click Add Cash Payment.
13. Click Validate Completion Dry Run.
14. Click Prepare Submit Guard.
15. Click Complete Sale.
16. Confirm completion modal.
17. Verify sale completed modal.
18. Click Start New Sale.
19. Open Products and verify stock reduction.

## Known QA Notes

- This is stable V1 local/offline POS.
- This is not multi-tenant yet.
- Use development mode for QA.
- Production database should not be touched during QA.
- `.salryn-dev` is ignored by git and stays local to each tester.