# POS-M1 Receipt Snapshot + File Receipt Report

Date: 2026-07-12  
Branch: `KIRCH-POS-M1-RECEIPT-SNAPSHOT-FILE-RECEIPT`  
Base branch: `KIRCH-POS-M0-STORE-READY-GAP-AUDIT`  
Base SHA: `40115ee6c233fb1d6983268e6bb82c98c695be5e`  
Product baseline being extended: `KIRCH-FRONTEND-WIRING-UX-M2` / `b315554cdc7f5ce6a879c877a357610eca20c2ec`

Authoring mode: GitHub connector-backed implementation. Local shell gates were not executable from this session.

---

## 1. Scope

POS-M1 implements receipt snapshot + readable file receipt foundation only.

In scope:

- receipt text renderer
- checkout-time receipt snapshot generation
- persisted `sales.receipt_snapshot` use
- controlled local file receipt writer
- post-commit file receipt integration
- smoke/compile proof class for snapshot rendering and file writer success/failure behavior

Out of scope and not implemented:

- ESC/POS printer
- Windows printer
- LAN printer
- test print button
- reprint UI
- barcode scanner flow
- Fast Add Product
- product write UI
- stock-in/adjustment UI
- void sale
- backup/restore
- installer/package
- BIR/eSales/legal
- Crystal runtime
- cloud upload
- UI polish

---

## 2. Files changed

- `src/Salryn.Core/Services/PosReceiptTextRenderer.cs`
- `src/Salryn.Core/Services/TrustedPosCheckoutSaleBuilder.cs`
- `src/Salryn.Api.Local/Contracts/HostOperatorCheckoutContracts.cs`
- `src/Salryn.Api.Local/Services/FileReceiptWriter.cs`
- `src/Salryn.Api.Local/Services/HostOperatorApiServices.cs`
- `src/Salryn.Api.Local/Services/HostOperatorCheckoutApiService.cs`
- `tests/Salryn.Tests/PosM1ReceiptSnapshotFileReceiptChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/POS_M1_RECEIPT_SNAPSHOT_FILE_RECEIPT_REPORT.md`

---

## 3. Schema changes

None.

The existing schema already contains:

- `sales.receipt_snapshot`
- `sale_items.product_name_snapshot`
- `sale_items.barcode_snapshot`
- `sale_items.unit_name_snapshot`
- `sale_items.unit_price_cents`
- `payments`
- `print_jobs`

POS-M1 did not add or modify migrations.

---

## 4. Receipt snapshot source of truth

Source of truth for POS-M1 receipt text:

- completed sale header values from checkout calculation
- `SaleItem.ProductNameSnapshot`
- `SaleItem.BarcodeSnapshot`
- `SaleItem.UnitNameSnapshot`
- `SaleItem.Quantity`
- `SaleItem.UnitPrice`
- `SaleItem.Discount`
- `SaleItem.LineTotal`
- persisted payment rows

`TrustedPosCheckoutSaleBuilder` now generates a receipt snapshot when the caller does not provide one. The snapshot is rendered from the same sale item/payment snapshot data that is passed into `AddCompletedSaleAsync`.

This keeps old receipts independent from later product edits because rendered text uses item snapshot fields, not live product names or current prices.

---

## 5. Receipt renderer

`PosReceiptTextRenderer` creates readable plain text with:

- receipt number
- UTC sale date
- cashier user id
- status
- line item names, barcodes, quantities, unit prices, discounts, and line totals
- subtotal, discount, tax, total, paid, and change
- payment method/amount/time
- explicit non-printer marker: `This is not a physical printer output.`

The renderer is not an ESC/POS renderer and does not target physical printers.

---

## 6. File receipt path

`FileReceiptWriter` writes receipt snapshots under the existing controlled receipt directory:

```text
DbPaths.ReceiptPath
```

Resolved by existing path policy:

- development: `<repo>/.salryn-dev/receipts`
- production: `%ProgramData%/Salryn/receipts`

File naming:

```text
<safe-receipt-no>-<sale-id-n>.txt
```

The writer creates the receipt directory if needed and writes UTF-8 plain text.

---

## 7. Sale commit failure behavior

Checkout order is intentionally:

1. Build trusted sale, sale item snapshots, payment rows, stock movements.
2. Generate receipt snapshot text.
3. Commit sale through `AddCompletedSaleAsync`.
4. Reload committed sale detail by receipt number.
5. Write file receipt from persisted `ReceiptSnapshot`.
6. Return diagnostic file receipt status.

File receipt writing happens after sale commit. If file writing fails, the sale remains committed. The checkout response now includes:

- `receiptSnapshotSaved`
- `fileReceiptStatus`
- `fileReceiptPath`
- `fileReceiptError`

Failure states are therefore diagnosable without rolling back the sale.

---

## 8. Tests / proof added

Added `PosM1ReceiptSnapshotFileReceiptChecks` and wired it into `tools/Salryn.Tools.CoreSmoke`:

- default full CoreSmoke path calls `PosM1ReceiptSnapshotFileReceiptChecks.Run()`
- targeted path: `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt`

The check covers:

- receipt text generated from sale item snapshot data
- receipt text contains readable PHP money values
- receipt text does not contain a simulated later live product name
- file receipt is created on a writable temp path
- file receipt contains the snapshot text
- blocked file receipt path returns `Failed` without throwing
- failed file receipt write is diagnosable

Limitation: this connector session could not run the tests locally. The branch requires local verification.

---

## 9. Gates

Required commands:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-M1-RECEIPT-SNAPSHOT-FILE-RECEIPT

dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

Connector result:

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools/Salryn.Tools.CoreSmoke` | `BLOCKED` — local shell unavailable. |
| `npm --prefix src/Salryn.App.Web run build` | `BLOCKED` — local shell unavailable. |
| `git diff --check` | `BLOCKED` — local shell unavailable. |
| `git status` | `BLOCKED` — local worktree unavailable. |

---

## 10. Manual proof

No manual app proof was run in this connector session.

Suggested manual proof after local gates:

1. Launch local API/WebView in development mode.
2. Log in as admin/cashier.
3. Open a shift.
4. Complete a cash checkout with a tracked product.
5. Confirm sale remains visible in Sales History.
6. Confirm sale detail has non-empty `receiptSnapshot`.
7. Confirm `.salryn-dev/receipts/<receipt>-<sale>.txt` exists.
8. Confirm file text contains the sold product snapshot name and paid/change values.
9. Simulate receipt directory write failure only in a disposable dev DB/path and confirm checkout response reports file receipt failure without deleting the sale.

---

## 11. Non-claims

This branch does not claim:

- GREEN
- ACCEPTED
- STORE_READY
- BETA_READY
- PRODUCTION_READY
- PRINTER_READY
- ESC/POS support
- Windows printer support
- LAN printer support
- test print readiness
- reprint UI
- barcode scanner reliability
- Fast Add readiness
- stock-in/adjustment readiness
- void sale readiness
- backup/restore readiness
- installer readiness
- BIR/eSales/legal compliance
- Crystal runtime readiness
- cloud upload readiness

---

## 12. Next lane recommendation

Next strict lane:

```text
POS-M2 Printer Adapter + Reprint + Failure Handling
```

Only start POS-M2 after POS-M1 local gates are run and the receipt snapshot/file receipt behavior is verified.

---

## 13. Status

Status: `BLOCKED`

Reason: implementation was pushed through the GitHub connector, but required local gates could not be executed from this session.
