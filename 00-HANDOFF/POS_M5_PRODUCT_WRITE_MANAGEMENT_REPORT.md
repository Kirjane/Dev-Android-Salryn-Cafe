# POS-M5 Product Write Management Report

Date: 2026-07-13  
Branch: `KIRCH-POS-M5-PRODUCT-WRITE-MANAGEMENT`  
Base branch: `KIRCH-POS-M4-FAST-ADD-UNKNOWN-BARCODE-INVENTORY`  
Base SHA: `b4982b81ccd5606797998d595002d83be05650c5`

Verification mode: local Codex self-check plus in-app browser manual proof. This is candidate evidence, not independent acceptance.

---

## 1. Scope

POS-M5 adds controlled Product Write Management from Product Catalog.

In scope:

- Product Catalog create flow
- Product Catalog edit flow for active products
- Product Catalog deactivate flow
- required product fields: product name, barcode, unit, selling price
- optional safe fields: category, brand, cost price, reorder level
- local validation and backend validation
- duplicate active barcode rejection
- product list/detail refresh after write
- inactive products excluded from Checkout barcode lookup because Checkout still reads `isActive: true`
- POS-M4 Fast Add duplicate barcode behavior retained
- POS-M1 receipt, POS-M2 print/reprint, POS-M3 exact barcode scan paths retained

Out of scope and not implemented:

- stock-in / stock adjustment screen
- normal Product Catalog create initial inventory
- purchase orders
- supplier management
- bulk import/export
- product image upload
- variants
- void/refund
- backup/restore
- installer/package
- RMO/BIR/eSales/legal/accreditation
- official receipt or BIR language
- real printer/scanner hardware proof

---

## 2. Files changed

- `00-HANDOFF/POS_M5_PRODUCT_WRITE_MANAGEMENT_REPORT.md`
- `src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs`
- `src/Salryn.Api.Local/Contracts/ProductManagementContracts.cs`
- `src/Salryn.Api.Local/Contracts/ReadOnlyContracts.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/LocalProductManagementEndpoints.cs`
- `src/Salryn.Api.Local/Services/ProductManagementApiService.cs`
- `src/Salryn.Api.Local/Services/ReadModelMapper.cs`
- `src/Salryn.App.Web/src/api/productManagement.ts`
- `src/Salryn.App.Web/src/api/products.ts`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `tests/Salryn.Tests/PosM5ProductWriteManagementChecks.cs`
- `tests/Salryn.Tests/WebB0ReadOnlyOperatorSurfacesChecks.cs`

No receipt snapshot/file writer, backend printer service, checkout sale math, stock adjustment screen, installer, protected ref, RMO/BIR/eSales file was intentionally changed.

---

## 3. Product create

Product create is exposed through:

```text
POST /api/products/manage
```

The endpoint requires local loopback/session permission checks and `Products.Manage` + `Products.View` permission boundaries.

The service creates a minimal active product through the existing `ProductWriteRepository`. Normal Product Catalog create starts with `StockOnHand = Quantity.Zero` and does not call inventory write repositories.

---

## 4. Validation

Client and server validation cover:

- product name required
- barcode required
- unit required
- selling price greater than zero
- cost price not negative when supplied
- reorder level not negative

---

## 5. Duplicate barcode handling

Before create/edit, the service searches active products and rejects exact active duplicate barcode values with:

```text
Products.DuplicateBarcode
```

The endpoint maps conflict errors to HTTP 409 and the Product Catalog UI displays a clear duplicate barcode message.

---

## 6. Product edit

Product edit is exposed through:

```text
PUT /api/products/{id}/manage
```

Edit preserves existing stock quantity, stock tracking flag, supplier, wholesale price, tax classification, created timestamp, and notes. It updates only the controlled Product Catalog fields and does not rewrite sales or receipt snapshots.

Inactive products are not editable in POS-M5.

---

## 7. Deactivate / reactivate

Deactivate is exposed through:

```text
POST /api/products/{id}/deactivate
```

It uses the existing `ProductWriteRepository.DeactivateAsync(...)` path and requires a reason.

Reactivate is parked because the repository contract exposes `AddAsync`, `UpdateAsync` for active rows, and `DeactivateAsync`, but no clean reactivate method. The UI reports this explicitly instead of inventing a direct SQL path.

---

## 8. Inactive checkout boundary

Checkout exact barcode lookup remains:

```text
fetchProducts({ search: barcode, isActive: true, page: 1, pageSize: 50 })
```

That keeps inactive products out of barcode scan add flow.

---

## 9. Regression wiring

Preserved:

- POS-M1 receipt file status in Sale Completed modal
- POS-M2 Print Receipt behavior and disabled/unavailable messaging
- POS-M2 Sales History reprint path unchanged
- POS-M3 exact barcode scan path
- POS-M4 Fast Add client and duplicate barcode behavior
- historical sale and receipt snapshots unchanged by product edits

---

## 10. Automated coverage added

Added:

```text
tests/Salryn.Tests/PosM5ProductWriteManagementChecks.cs
```

Coverage intent:

- product write contracts
- local endpoint routes
- `Products.Manage` permission boundary
- service registration and endpoint mapping
- create/edit/deactivate service methods
- duplicate barcode conflict
- no inventory writes from normal Product Catalog create/edit
- Product Catalog create/edit/deactivate UI strings
- inactive checkout barcode boundary
- Fast Add and POS-M3 barcode regression strings

CoreSmoke entry-point status:

- POS-M5 is registered as `--only pos-m5-product-write-management`.
- The focused POS-M5 gate executes `PosM5ProductWriteManagementChecks.Run()`.
- Full CoreSmoke initially failed because the older WEB-B0 read-only aggregate still scanned `ProductsPage.tsx` for forbidden mutation labels, even though POS-M5 deliberately adds permission-gated Product Catalog writes.
- The repair removes only `ProductsPage.tsx` from that stale read-only aggregate. Product API, Inventory, Sales History, and Audit remain covered there, while the dedicated POS-M5 gate continues to require create/edit/deactivate UI markers and `Products.Manage` guards.

---

## 11. Gates

Executed locally after the narrow WEB-B0 scope repair:

```powershell
dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m4-fast-add-unknown-barcode-inventory
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m5-product-write-management
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Results:

- `dotnet build Salryn.sln`: PASS, 0 errors (nullable warnings remain in existing code).
- full CoreSmoke: PASS.
- targeted POS-M1 CoreSmoke: PASS.
- targeted POS-M2 CoreSmoke: PASS.
- targeted POS-M3 CoreSmoke: PASS.
- targeted POS-M4 CoreSmoke: PASS.
- targeted POS-M5 CoreSmoke: PASS.
- web build: PASS.
- `git diff --check`: PASS.
- final clean status is recorded after the verification commit.

---

## 12. Manual proof

Completed locally against the development database and host at `http://127.0.0.1:5299`:

- Product Catalog, create, edit, and deactivate flows opened and completed.
- Required product name validation and selling-price-greater-than-zero validation were shown.
- Duplicate active barcode was rejected with a clear conflict message.
- A valid product was created, refreshed into the list, and found by name and barcode.
- The created zero-stock tracked product was found by Checkout barcode lookup but was correctly refused for cart add.
- A stocked POS-M4 product was scanned, sold, and reduced from stock 4 to 3 only after checkout.
- The resulting receipt text file exists at the controlled development receipt path.
- Print Receipt and Sales History Reprint Receipt both showed the disabled adapter clearly and did not create another sale or stock movement.
- After editing the sold product's name, barcode, unit, and price, Sales History still displayed the persisted old name and PHP 25.00 receipt line, proving the historical snapshot was not rewritten.
- Deactivation succeeded and the Product Catalog marked the product inactive. Reactivation remains explicitly parked because no approved repository method exists.
- Scanning the inactive barcode did not add it to the cart; it entered the separate explicit Fast Add confirmation and was cancelled without a write.
- POS-M4 Fast Add regression proof created a separate unknown barcode product with initial stock 1 and added it to the cart; the cart was cleared without checkout.
- Browser warning/error log was empty, backend stderr was empty, the cashier shift was closed, and the host stopped cleanly.

No physical barcode scanner or physical printer was used; keyboard-wedge behavior and the default disabled printer adapter were exercised through the browser UI.

---

## 13. Protected refs

Not touched by this branch work:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 14. Status

`CANDIDATE_SELF_CHECKED_AND_MANUAL_PROVEN`

Reason: required automated gates and the scoped POS-M5 browser workflow passed locally after the narrow WEB-B0 scope correction. Independent maintainer acceptance is still separate.

No `GREEN`, `ACCEPTED`, `STORE_READY`, `BETA_READY`, `PRODUCTION_READY`, or `BIR_READY` claim is made.
