# POS-M3 Barcode Scanner Cashier Flow Report

Date: 2026-07-13  
Branch: `KIRCH-POS-M3-BARCODE-SCANNER-CASHIER-FLOW`  
Base branch: `KIRCH-POS-M2-PRINTER-ADAPTER-REPRINT-FAILURE`  
Base SHA: `d22bede19496633b67826971a2cb03316893a266`  
Head before this report update: `88dd068c71f34a28b6b2a282e07ad98ef6e68730`

Authoring mode: GitHub connector-backed implementation. Local shell gates, browser/manual proof, and physical keyboard-wedge proof were not executable from this session.

---

## 1. Scope

POS-M3 adds keyboard-wedge barcode cashier flow to checkout only.

In scope:

- scan/search input on Checkout
- Enter suffix handling from the scan/search input
- exact barcode lookup using existing catalog GET data
- cart add through the existing `addProduct(...)` path
- unknown barcode parked Fast Add status
- targeted POS-M3 CoreSmoke source checks
- POS-M1 receipt file and POS-M2 printer/reprint regression wiring kept visible

Out of scope and not implemented:

- Fast Add product creation
- product writes
- stock-in/adjustment writes
- void/refund
- backup/restore
- installer/package
- RMO/BIR/eSales/legal/accreditation
- real scanner driver work
- stock mutation outside completed checkout

---

## 2. Files changed

- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `tests/Salryn.Tests/PosM3BarcodeScannerCashierFlowChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/POS_M3_BARCODE_SCANNER_CASHIER_FLOW_REPORT.md`

No product API shape, checkout math, receipt snapshot/file writer, backend printer service, stock write, Fast Add, void/refund, installer, RMO/BIR/eSales files were intentionally changed.

---

## 3. Barcode input behavior

Checkout product search is now presented as a scan/search input.

Behavior:

- input remains normal text search for product names/SKU/barcode text
- input is autofocus-enabled for cashier workflow
- Enter from this input is intercepted locally
- Enter calls `handleBarcodeEnter()`
- Enter does not submit checkout or confirm sale unless the checkout review dialog is open

The global checkout keyboard behavior remains:

- F9 = Review Checkout
- Enter = Confirm only when review dialog is open
- F5 = New Sale after completion
- Esc = Close/cancel dialog

---

## 4. Exact barcode match

Exact barcode add is catalog-backed only.

Implementation path:

```text
fetchProducts({ search: barcode, isActive: true, page: 1, pageSize: 50 })
exactBarcodeMatch(lookup.items, barcode)
addProduct(match)
```

The match compares normalized existing `ProductListItem.barcode` against the normalized scan/search input. It does not create products and does not mutate stock.

---

## 5. Enter suffix behavior

Keyboard-wedge Enter suffix behavior:

- cashier scans/types barcode into the scan/search input
- Enter triggers exact barcode lookup
- exact match adds existing product to cart through existing cart code
- no exact match adds nothing
- stock remains unchanged until normal checkout completes

---

## 6. Unknown barcode behavior

Unknown barcode message:

```text
Product not found. Fast Add is parked for POS-M4.
```

Unknown barcode does not:

- create product
- add placeholder cart line
- reduce stock
- call checkout
- call any product write

---

## 7. Product-name search regression

Product-name search remains available while typing. The scan/search input still feeds the existing `/api/products` read path through `fetchProducts(...)` and the result list remains visible.

---

## 8. Checkout regression

Checkout remains the only stock-reducing operation.

POS-M3 only adds products to the in-memory cart. Sale commit still goes through the existing `submitCheckout(...)` path and existing backend validation.

---

## 9. Receipt regression

POS-M1 receipt status remains visible in Sale Completed modal through existing receipt file status fields.

Receipt snapshot and file writer were not changed.

---

## 10. Printer/reprint regression

POS-M2 print behavior remains available from Sale Completed modal through `printReceiptForSale(...)` imported from the isolated `receiptPrint` API module.

Sales History reprint path was not changed in this lane.

---

## 11. Automated coverage added

Added:

```text
tests/Salryn.Tests/PosM3BarcodeScannerCashierFlowChecks.cs
```

Coverage intent:

- product list exposes barcode field
- checkout normalizes barcode input
- exact barcode match is isolated
- Enter suffix triggers barcode add flow
- lookup uses existing catalog read path
- exact match reuses `addProduct(match)`
- unknown barcode shows POS-M4 parked Fast Add message
- checkout still uses `submitCheckout(...)`
- POS-M1 receipt file status remains wired
- POS-M2 print client remains wired
- product writes/stock writes/void/official behavior are not introduced

Targeted CoreSmoke target:

```powershell
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
```

---

## 12. Gates

Required commands:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-M3-BARCODE-SCANNER-CASHIER-FLOW
git reset --hard origin/KIRCH-POS-M3-BARCODE-SCANNER-CASHIER-FLOW

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Connector result:

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow` | `BLOCKED` — local shell unavailable. |
| `npm --prefix src\Salryn.App.Web run build` | `BLOCKED` — local shell unavailable. |
| `git diff --check` | `BLOCKED` — local shell unavailable. |
| `git status` | `BLOCKED` — local worktree unavailable. |

---

## 13. Manual proof checklist

Run after local build/web build passes:

- [ ] Checkout opens.
- [ ] Shift gate still works.
- [ ] Scan/search input is focused and usable.
- [ ] Typing/scanning barcode then Enter adds exact product.
- [ ] Product-name search still works.
- [ ] Unknown barcode does not add product.
- [ ] Unknown barcode clearly says Fast Add parked for POS-M4.
- [ ] Checkout completes after barcode-added item.
- [ ] Receipt `.txt` still created.
- [ ] Print Receipt disabled/unavailable behavior still clear.
- [ ] Sales History reprint still works.
- [ ] Stock reduces only after checkout, not after scan.
- [ ] No console errors.
- [ ] No backend exceptions.

---

## 14. Explicit non-claims

This branch does not claim:

- GREEN
- ACCEPTED
- STORE_READY
- BETA_READY
- PRODUCTION_READY
- BIR_READY
- Fast Add readiness
- product-write readiness
- stock-in/adjustment readiness
- void/refund readiness
- installer readiness
- RMO/BIR/eSales readiness
- real scanner hardware certification

---

## 15. Status

Status: `BLOCKED`

Reason: implementation was authored through the GitHub connector, but required local build, full/targeted CoreSmoke, web build, git checks, browser proof, and keyboard-wedge/manual proof could not be executed from this session.
