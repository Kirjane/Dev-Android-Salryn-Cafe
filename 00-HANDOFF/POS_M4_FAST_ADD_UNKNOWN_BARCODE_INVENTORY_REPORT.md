# POS-M4 Fast Add Unknown Barcode Inventory Report

Date: 2026-07-13  
Branch: `KIRCH-POS-M4-FAST-ADD-UNKNOWN-BARCODE-INVENTORY`  
Base branch: `KIRCH-POS-M3-BARCODE-SCANNER-CASHIER-FLOW`  
Base SHA: `04d307a2fd31ea424259f897beacdb6cc49efdf2`  
Head before this report update: `669d84dacc13a7e583a55faa3cac128137140b52`

Authoring mode: GitHub connector-backed implementation. Local shell gates, browser/manual proof, and real hardware proof were not executable from this session.

---

## 1. Scope

POS-M4 adds controlled Fast Add for unknown barcode cashier flow.

In scope:

- unknown barcode from Checkout opens Fast Add confirmation flow
- barcode is preserved from the scan/search input
- cashier must explicitly confirm product creation
- required fields: product name, barcode, unit, selling price, initial stock quantity
- client and server validation for required fields, selling price, and initial stock quantity
- duplicate active barcode rejection
- product creation through existing product write repository
- optional initial stock through existing inventory stock movement repository
- newly created product returned to Checkout and added to the current cart
- checkout remains the only sale stock-reducing operation
- POS-M1 receipt and POS-M2 print/reprint UI paths remain wired
- POS-M3 exact barcode add path remains wired

Out of scope and not implemented:

- full product management CRUD
- bulk import
- product images
- variants
- supplier management
- purchase orders
- stock adjustment screen
- void/refund
- backup/restore
- installer/package
- RMO/BIR/eSales/legal/accreditation
- official receipt or BIR language
- real printer or physical scanner proof

---

## 2. Files changed

- `src/Salryn.Composition.Local/SalrynLocalComposition.cs`
- `src/Salryn.Api.Local/Contracts/FastAddProductContracts.cs`
- `src/Salryn.Api.Local/Services/FastAddProductApiService.cs`
- `src/Salryn.Api.Local/Services/OperatorReadService.cs`
- `src/Salryn.Api.Local/Services/ReadModelMapper.cs`
- `src/Salryn.Api.Local/LocalFastAddProductEndpoints.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`
- `src/Salryn.App.Web/src/api/products.ts`
- `src/Salryn.App.Web/src/api/fastAddProduct.ts`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `tests/Salryn.Tests/PosM3BarcodeScannerCashierFlowChecks.cs`
- `tests/Salryn.Tests/PosM4FastAddUnknownBarcodeInventoryChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/POS_M4_FAST_ADD_UNKNOWN_BARCODE_INVENTORY_REPORT.md`

No receipt snapshot/file writer, backend printer service, sales checkout math, installer, protected ref, RMO/BIR/eSales file was intentionally changed.

---

## 3. Fast Add trigger

Unknown barcode no longer ends at a parked message. The Checkout scan/search input still performs exact barcode lookup first. If no active exact barcode match is found, Checkout opens the Fast Add confirmation flow.

The flow is explicit: no product is created until the cashier confirms `Create Product and Add to Cart`.

---

## 4. Barcode preservation

The unknown barcode is copied into the Fast Add form and shown as a read-only field. This preserves the scan/search input value and avoids accidental barcode drift during product creation.

---

## 5. Validation

Client-side validation blocks:

- missing product name
- missing barcode
- missing unit
- selling price less than or equal to zero
- initial stock quantity below zero

Server-side validation repeats the same rules through `FastAddProductApiService` before repository writes.

---

## 6. Duplicate barcode handling

Before create, the service searches active catalog products and rejects an exact active barcode duplicate with `Products.DuplicateBarcode`.

The endpoint maps conflict errors to HTTP 409 and returns `{ code, error }` for clear UI display.

---

## 7. Product creation

Product creation uses the existing `IProductWriteRepository` implementation. POS-M4 does not perform raw SQL product inserts.

The created product is minimal and active:

- barcode from unknown scan/search value
- product name from cashier input
- selected unit
- selling price
- stock tracking enabled
- zero stock at insert time

---

## 8. Initial stock creation

Initial inventory uses the existing `IInventoryWriteRepository` implementation. POS-M4 creates a `StockIn` stock movement only when the initial quantity is positive.

The product is inserted with zero stock first, then the stock movement applies the initial stock quantity. This avoids double-counting initial stock.

If initial stock is zero, no stock movement is created because the existing inventory write repository requires positive stock movement quantity.

---

## 9. Add-to-cart after Fast Add

After successful backend creation and product readback, Checkout receives a `ProductListItemResponse` and calls the existing `addProduct(...)` cart path.

This preserves the same in-memory cart handling used by normal product cards and POS-M3 exact barcode matches.

---

## 10. Stock mutation boundary

Fast Add can create a product and can record initial inventory. It does not perform sale stock reduction.

Sale stock reduction still occurs only through normal checkout commit via `submitCheckout(...)`.

---

## 11. Regression wiring

Preserved:

- POS-M1 Sale Completed receipt file status
- POS-M2 Print Receipt behavior and disabled/unavailable visibility
- POS-M2 Sales History reprint path was not changed
- POS-M3 exact barcode add path
- product-name search from the same scan/search input

---

## 12. Automated coverage added

Added:

```text
tests/Salryn.Tests/PosM4FastAddUnknownBarcodeInventoryChecks.cs
```

Coverage intent:

- Fast Add endpoint and service registration
- permission boundary
- required contracts
- client/server validation strings
- duplicate barcode conflict
- product write repository use
- inventory write repository use
- StockIn initial inventory path
- zero-stock insert before stock movement
- isolated web POST client
- Checkout Fast Add modal and barcode preservation
- add-to-cart after Fast Add
- receipt and printer regression wiring
- POS-M3 exact barcode add regression

Updated CoreSmoke:

```text
PosM4FastAddUnknownBarcodeInventoryChecks.Run();
--only pos-m4-fast-add-unknown-barcode-inventory
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
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status: `BLOCKED` until run on Kirch's local workstation.

---

## 14. Manual proof

Not executable from this session. Required manual proof remains pending:

- Checkout opens
- Shift gate still works
- Existing barcode scan still adds existing product
- Unknown barcode opens Fast Add flow
- Unknown barcode is preserved in Fast Add
- Required fields validate
- Duplicate barcode is rejected
- Valid Fast Add creates product
- Valid Fast Add creates initial stock entry
- Newly Fast Added product is added to cart
- Stock is not reduced by scan/Fast Add alone
- Checkout completes with Fast Added product
- Stock reduces only after checkout
- Receipt `.txt` still created
- Print Receipt disabled/unavailable behavior still clear
- Sales History reprint still works
- No duplicate sale/payment/stock movement from Fast Add/reprint
- No browser-console or backend errors

---

## 15. Protected refs

Not touched by this branch work:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 16. Status

`BLOCKED`

Reason: implementation is repo-backed, but local build/CoreSmoke/web build/diff/status and manual proof were not executable from this session.

No `GREEN`, `ACCEPTED`, `STORE_READY`, `BETA_READY`, `PRODUCTION_READY`, or `BIR_READY` claim is made.
