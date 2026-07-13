# POS-M5 Product Write Management Report

Date: 2026-07-13  
Branch: `KIRCH-POS-M5-PRODUCT-WRITE-MANAGEMENT`  
Base branch: `KIRCH-POS-M4-FAST-ADD-UNKNOWN-BARCODE-INVENTORY`  
Base SHA: `b4982b81ccd5606797998d595002d83be05650c5`

Authoring mode: GitHub connector-backed implementation. Local shell gates, browser/manual proof, and physical scanner/printer proof were not executable from this session.

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

- POS-M5 smoke file exists.
- Program.cs entry-point wiring was not safely updated before this report due the large existing CoreSmoke file needing a full replacement through the connector.
- Targeted `--only pos-m5-product-write-management` remains `BLOCKED` until Program.cs is patched and locally verified.

---

## 11. Gates

Not executed from this session:

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

Gate status: `BLOCKED` until run on Kirch's local workstation.

---

## 12. Manual proof

Not executable from this session. Required manual proof remains pending.

---

## 13. Protected refs

Not touched by this branch work:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 14. Status

`BLOCKED`

Reason: implementation is repo-backed, but local build/CoreSmoke/web build/diff/status and manual proof were not executable from this session. Program.cs targeted CoreSmoke wiring remains blocked pending a safe full-file patch.

No `GREEN`, `ACCEPTED`, `STORE_READY`, `BETA_READY`, `PRODUCTION_READY`, or `BIR_READY` claim is made.
