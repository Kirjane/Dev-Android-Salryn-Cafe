# POS-STAB-BATCH-3 Catalog and Settings Readiness Seal

## Scope

POS-STAB-BATCH-3 moved the local POS catalog/settings surfaces closer to operator readiness after the Basic Store-Day and Operational Safety seals.

Included promoted candidate work:

- Product edit form and Product Management edit/deactivate actions.
- Product edit keeps stock on hand read-only and preserves stock through existing inventory workflows.
- Product deactivate requires an authenticated session and an operator reason.
- Store Settings edit/save workflow.
- Store settings write boundary and SQLite implementation.
- Production database writability preflight.
- Freehand candidate verification repair and promotion to `KIRCH-SANDBOX`.
- Production database preflight hardening for existing read-only `salryn.db` files.

Key commits:

- `8ba5e49e61453517092636105fa280fce223540c` Repair freehand POS stabilization verification
- `04a207fc187fa123d5a5a10c85e84326fbc1a03e` Harden production database writability preflight

## Features Verified

- Products shell route opens the real Product Management form.
- Product Edit opens for an active product.
- Product Edit shows `Stock On Hand (read-only)` and does not use the stock field for product edit persistence.
- Product field edits persist after save and reload.
- Product deactivate blocks a blank reason with `Products.DeactivateReasonRequired`.
- Product deactivate accepts an explicit reason and reports `Product deactivated.`
- Store Settings opens from the shell.
- Store Settings enters edit mode, saves a harmless store field change, and reports `Store settings saved.`
- App remained stable and was closed normally after the smoke.
- Production database preflight now checks both folder writability and existing database file read/write access before SQLite initialization.

## Manual Smoke Result

Manual dev-app smoke was run from `C:\Users\kirch\Salryn`.

Result:

- App launched in development mode.
- Login succeeded with the dev admin account.
- Products module opened.
- Active product was selected from Product Management.
- Edit Product opened.
- Stock On Hand was visible as read-only.
- Product name was changed to include `TEST PERSISTENCE`, saved, searched again, and confirmed persisted.
- Deactivate with blank reason was blocked.
- Deactivate with reason `TEST REASON` completed and showed `Product deactivated.`
- Settings module opened.
- Business Name was changed to `Local POS TEST PERSISTENCE`.
- Store Settings saved successfully.
- App did not crash and was closed by the operator.

## Verification

Before this seal, the automated verification passed:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj`
- `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- official-output-text-file`

## Boundaries

This milestone did not add or touch:

- RMO official output behavior
- BIR/RMO certification claims
- PDF output
- Crystal Reports output
- `.rpt` templates
- ESC/POS
- raw USB printer support
- scanner UX
- schema migrations
- Users admin UI

## Known Warning

`SQLitePCLRaw.lib.e_sqlite3` NU1903 remains intentionally untouched.

## Remaining Gaps

- Users admin UI remains placeholder-routed.
- Scanner UX polish remains deferred.
- Printer hardware/configuration remains deferred.
- Product deactivate/manual edit should receive broader manual regression coverage before production use.
- Store Settings edit should receive broader manual regression coverage before production use.
- Production database writability behavior should be rechecked on an actual production install target.
- Normal-login label polish should be revisited because stale first-run labels can still appear visually in the login form.
