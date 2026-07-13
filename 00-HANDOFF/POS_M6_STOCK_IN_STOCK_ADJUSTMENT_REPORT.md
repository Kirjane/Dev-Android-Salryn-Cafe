# POS-M6 Stock-In / Stock Adjustment Report

Date: 2026-07-14  
Branch: `KIRCH-POS-M6-STOCK-IN-STOCK-ADJUSTMENT`  
Base branch: `KIRCH-POS-M5-PRODUCT-WRITE-MANAGEMENT`  
Base SHA: `e2e36a0dd34738af6dbf750283c0f2406957cf31`

Authoring mode: GitHub connector-backed implementation. Local shell gates, browser/manual proof, and physical scanner/printer proof were not executable from this session.

---

## 1. Scope

POS-M6 adds controlled Stock-In and Stock Adjustment from the Stock Movements surface, then POS-M6-R1 repairs store-operator workflow by making Product Catalog the practical owner screen for product metadata plus stock actions.

In scope:

- Stock-In flow for active, stock-tracked existing products
- Stock Adjustment flow for active, stock-tracked existing products
- product lookup by existing product search API
- required product, quantity, and reason validation
- Stock-In quantity greater than zero
- delta-based adjustment using existing `AdjustmentAdd` and `AdjustmentRemove` movement types
- movement ledger entry for every stock write
- product stock readback after successful write
- inventory/product views refresh after write
- clear validation / permission / backend failure messages
- Product Catalog selected-product actions for Stock-In, Adjust Stock, and View Movements path
- Product Create copy explaining one product record and stock quantity added through Stock-In after saving
- visible optional Category / Brand copy with No category / No brand accepted
- checkout repeated same product/barcode quantity path preserved
- POS-M6 targeted CoreSmoke registration

Out of scope and not implemented:

- purchase orders
- supplier receiving workflow
- batch/expiry tracking
- bulk import/export
- variants
- void/refund
- backup/restore
- installer/package
- RMO/BIR/eSales/legal/accreditation
- official receipt or BIR language
- physical printer/scanner proof
- product reactivation
- Store Settings / category-brand-unit management / printer selection implementation

---

## 2. Files changed

- `00-HANDOFF/POS_M6_STOCK_IN_STOCK_ADJUSTMENT_REPORT.md`
- `src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs`
- `src/Salryn.Api.Local/Contracts/InventoryManagementContracts.cs`
- `src/Salryn.Api.Local/LocalFastAddProductEndpoints.cs`
- `src/Salryn.Api.Local/LocalInventoryManagementEndpoints.cs`
- `src/Salryn.Api.Local/Services/InventoryManagementApiService.cs`
- `src/Salryn.App.Web/src/api/inventory.ts`
- `src/Salryn.App.Web/src/api/inventoryManagement.ts`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `tests/Salryn.Tests/PosM6StockInStockAdjustmentChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

No receipt snapshot/file writer, backend printer service, checkout sale math, product edit service, installer, protected ref, RMO/BIR/eSales file was intentionally changed.

---

## 3. Stock-In flow

Stock-In is exposed through:

```text
POST /api/inventory/stock-in
```

The endpoint requires local/session permission checks and `Inventory.Adjust`, `Inventory.View`, and `Products.View` permission boundaries.

The service creates a `StockMovement` with `StockMovementType.StockIn` and writes it through `IInventoryWriteRepository`.

POS-M6-R1 exposes Product Catalog action copy as `Stock-In this product` and opens a product-preselected stock dialog using the existing write client module.

---

## 4. Stock-In validation

Client and server validation cover:

- product required
- quantity greater than zero
- reason required
- active product required
- stock-tracked product required

Inactive products are rejected by service validation and by the existing inventory write repository.

---

## 5. Stock-In ledger

The existing `SqliteInventoryWriteRepository` applies stock changes and inserts a stock movement row in the same transaction. POS-M6 uses that repository path rather than raw SQL.

---

## 6. Stock Adjustment flow

Stock Adjustment is exposed through:

```text
POST /api/inventory/adjustment
```

Adjustment uses the existing safe delta model:

- `AdjustmentAdd` for positive correction
- `AdjustmentRemove` for negative correction

Set-to-count adjustment is not implemented in POS-M6 because the existing repository supports delta movement types cleanly.

POS-M6-R1 exposes Product Catalog action copy as `Adjust Stock` and opens the same product-preselected dialog with AdjustmentAdd / AdjustmentRemove direction selection.

---

## 7. Stock Adjustment validation

Client and server validation cover:

- product required
- quantity greater than zero
- reason required
- adjustment direction must be `add` or `remove`
- remove adjustment cannot make stock negative
- active, stock-tracked product required

---

## 8. Stock Adjustment ledger

Adjustment writes create `AdjustmentAdd` or `AdjustmentRemove` stock movement rows through `IInventoryWriteRepository`.

The ledger filter exposes `StockIn`, `AdjustmentAdd`, and `AdjustmentRemove` explicitly.

Product Catalog now exposes a `View Movements` path from the selected product panel. It does not add a new route or backend endpoint.

---

## 9. Inventory quantity update

Product stock on hand is updated by the existing inventory repository transaction. After a successful write, the API reads back the product and returns the updated `ProductListItemResponse`.

The web page refreshes low-stock data, movement ledger data, product lookup data, and Product Catalog detail/list data after write.

---

## 10. Side-effect boundary

Stock-In and Stock Adjustment do not call sale write repositories, payment paths, checkout paths, receipt snapshot writers, or printer/reprint paths.

The service response and UI state that no sale, payment, or receipt is created by stock writes.

Product Catalog create/edit still does not mutate stock quantity. Product Create now states: `Create one product record. Add quantity through Stock-In after saving.`

---

## 11. Regression wiring

Preserved:

- POS-M1 receipt file status in checkout completion flow
- POS-M2 Print Receipt behavior and Sales History reprint path
- POS-M3 exact barcode checkout path
- POS-M4 Fast Add path
- POS-M5 Product Catalog product write path and stock-neutral product edit boundary
- checkout remains the sale stock-reduction path
- repeated same barcode/product add increments the existing cart quantity path rather than requiring duplicate product records
- WEB-B0 read-only inventory API boundary: `inventory.ts` stays read-only and stock POST clients live in `inventoryManagement.ts`

---

## 12. Automated coverage added

Added / updated:

```text
tests/Salryn.Tests/PosM6StockInStockAdjustmentChecks.cs
```

Coverage intent:

- inventory write contracts
- local endpoint routes
- `Inventory.Adjust` permission boundary
- Stock-In service path
- AdjustmentAdd and AdjustmentRemove service path
- product active/stock-tracked validation
- required reason validation
- negative-stock prevention
- inventory ledger repository use
- no sale/payment/receipt side effects
- Stock Movements UI write flows
- Product Catalog selected-product Stock-In / Adjust Stock / View Movements workflow
- Product Create stock guidance and optional category/brand copy
- checkout repeated add / quantity control strings
- POS-M1/POS-M2/POS-M3/POS-M4/POS-M5 regression strings

Updated CoreSmoke:

```text
PosM6StockInStockAdjustmentChecks.Run();
--only pos-m6-stock-in-stock-adjustment
```

---

## 13. Gates

Not executed from this session:

```powershell
dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m4-fast-add-unknown-barcode-inventory
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m5-product-write-management
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m6-stock-in-stock-adjustment
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status: `BLOCKED` until run on Kirch's local workstation.

---

## 14. Manual proof

Not executable from this session. Required POS-M6-R1 strict manual proof remains pending.

---

## 15. Protected refs

Not touched by this branch work:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 16. Status

`BLOCKED`

Reason: implementation is repo-backed, but local build/CoreSmoke/web build/diff/status and strict manual proof were not executable from this session.

No `GREEN`, `ACCEPTED`, `STORE_READY`, `BETA_READY`, `PRODUCTION_READY`, `BIR_READY`, `PHYSICAL_SCANNER_PROVEN`, or `PHYSICAL_PRINTER_PROVEN` claim is made.
