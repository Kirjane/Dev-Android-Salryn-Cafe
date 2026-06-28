# POS-STAB-BATCH-1 Basic Store-Day Seal

## Scope

POS-STAB-BATCH-1 added the first visible basic store-day operation coverage after POS startup/login hardening. This is a local POS stabilization milestone, not an RMO certification milestone.

Included commits:

- `ed766e216730ab962d56c284779eaa56b2771e5b` Add cashier shift close workflow
- `1f256df417431d24f06e818b5c597ef710654449` Add controlled sales void workflow
- `7e23c46328f4e529d0bef7fa9f2de6e8944a67d4` Add inventory adjustment workflow
- `c8f26bdcd6c15db714244665cfc0473c0a563156` Add normal receipt printing support

## Features Verified

- Cashier shift close uses the cashier session service and reports opening cash, expected cash, counted cash, and variance.
- Sales void is launched from Sales History, requires a reason, confirms the operation, marks the sale voided, and records stock reversal through existing backend paths.
- Inventory adjustment is launched from Inventory, requires a selected stock-tracked product, quantity delta, and reason, then records an adjustment movement through existing backend paths.
- Receipt preview opens from Sales History and uses Windows printer queue printing through the normal receipt preview surface.
- Existing checkout still opens through the shift gate and can complete a sale.

## Manual Smoke Result

Manual dev-app smoke was run from `C:\Users\kirch\Salryn` using the development app path and the `admin` dev account.

Result:

- App launched in dev mode.
- Login succeeded.
- Existing open cashier shift was detected.
- POS Checkout opened from the cashier shift gate.
- A stock-tracked product was found by barcode and added to the cart.
- Cash payment was added and balance reached zero.
- Completion dry run passed, submit guard prepared, and sale completed.
- Sales History showed the completed sale:
  - Receipt: `POS-20260628-044919119-50C35B99-E263D8`
  - Sale id: `6f321504-201b-49b9-8326-0b85db8e176b`
- Receipt Preview opened and the Windows print dialog path was exercised with cancel behavior.
- Sales void required reason `Manual smoke void reason`, confirmed the operation, and showed the sale as `Voided`.
- Read-only verification found the original sale stock-out movement and the matching `VoidSale` reversal movement.
- Inventory adjustment added quantity `1` with reason `Manual smoke inventory adjustment` and showed an `AdjustmentAdd` stock movement.
- Cashier shift close accepted counted cash PHP 100.00 and reported variance PHP 0.00.
- App remained stable through the smoke.

## Official Output Regression Folder

The existing official output text-file smoke folder still contained controlled text-file output under:

`C:\Users\kirch\Desktop\SalrynOfficialOutputSmoke\official-output\ae5e30300a6fdfc81d5eb86a056c2b3e167c80b50860945b7b561355724de7a6`

Files observed:

- `manifest.txt`
- `package.txt`
- `ledger.txt`
- `dispatch-plan.txt`
- `execution-receipt.txt`
- `payload-001-receiptinvoice-1eed7cd390aa.txt`

## Verification

Before this seal, the batch verification passed:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- official-output-text-file`

## Boundaries

This milestone does not add or claim:

- RMO/BIR certification
- PDF output
- Crystal Reports output
- `.rpt` templates
- ESC/POS support
- raw USB printer support
- schema or migration changes
- RMO official output mutation

Receipt printing in this milestone is normal local POS receipt printing through an installed Windows printer queue. It is not official certified/RMO output printing.

## Known Warning

`SQLitePCLRaw.lib.e_sqlite3` NU1903 remains intentionally untouched.

## Remaining Gaps

- Users UI
- Backup UI
- Audit UI
- Product edit/deactivate
- Settings edit
- Scanner UX polish
- Printer configuration and manual hardware coverage
- Production database writability/deployment diagnostics still need real deployment validation
- Login normal-mode accessibility/static label polish should be rechecked in a future UI pass
