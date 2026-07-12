# POS-M2 Printer Adapter + Reprint + Failure Handling Report

Date: 2026-07-13  
Branch: `KIRCH-POS-M2-PRINTER-ADAPTER-REPRINT-FAILURE`  
Base branch: `KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY`  
Base SHA: `0dc46de5c875d6fad073b3d57aead9abe1a7cf9a`  
Head before this report update: `94e35714b1339102533525b2460a34a27c21fb0c`

Authoring mode: GitHub connector-backed implementation. Local shell gates, real printer proof, and browser/manual proof were not executable from this session.

---

## 1. Scope

POS-M2 adds printer/reprint/failure-handling foundation only.

In scope:

- printer abstraction and disabled/default adapter boundary
- print service using persisted POS-M1 receipt snapshot text
- receipt print API endpoints
- Sale Completed modal `Print Receipt` action
- Sales History detail `Reprint Receipt` action
- failure/disabled status visibility
- no-op/fake adapter smoke coverage
- report file

Out of scope and not implemented:

- POS-M3 or any next lane
- barcode scanner
- Fast Add Product
- product writes
- stock-in/adjustment writes
- void/refund
- backup/restore
- installer/package
- RMO/BIR/eSales/legal/accreditation
- Crystal Reports
- cloud upload
- non-cash payments
- production printer activation

---

## 2. Files changed

- `src/Salryn.Core/Services/PosReceiptPrintService.cs`
- `src/Salryn.Api.Local/Contracts/ReceiptPrintContracts.cs`
- `src/Salryn.Api.Local/LocalReceiptPrinterEndpoints.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/Services/HostOperatorApiServices.cs`
- `src/Salryn.App.Web/src/api/sales.ts`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx`
- `tests/Salryn.Tests/PosM2PrinterReprintFailureChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/POS_M2_PRINTER_ADAPTER_REPRINT_FAILURE_REPORT.md`

No schema/migration, checkout money calculation, stock movement write, receipt snapshot renderer, receipt file writer, installer, RMO, BIR, or eSales files were intentionally changed.

---

## 3. Printer abstraction design

`PosReceiptPrintService` is the narrow application boundary for POS receipt printing.

It depends on:

- `ISaleReadRepository`
- `IReceiptPrinterAdapter`

`IReceiptPrinterAdapter` exposes only:

- `AdapterName`
- `IsEnabled`
- `PrintAsync(jobName, receiptText, cancellationToken)`

Included adapters:

- `DisabledReceiptPrinterAdapter`
  - default app composition
  - returns `Disabled/Unavailable`
  - no physical printer execution
- `NoOpReceiptPrinterAdapter`
  - test/smoke adapter
  - records the last job/text
  - succeeds without a real printer

Future ESC/POS or Windows printer work must implement `IReceiptPrinterAdapter` instead of adding printer-specific code into checkout logic.

---

## 4. Print/reprint source of truth

Print source is persisted receipt data only.

Current source:

```text
SaleDetailReadModel.ReceiptSnapshot
```

This is the POS-M1 persisted receipt snapshot already written with the sale. POS-M2 does not regenerate receipt text from live product data and does not read current product names/prices.

If no persisted receipt snapshot is available, the service returns:

```text
NotAttempted
```

The POS-M1 receipt `.txt` file remains available as a store artifact. This lane does not alter the POS-M1 file writer or receipt renderer.

---

## 5. Print after sale completion

Checkout UI behavior:

- Sale still commits through the existing checkout path.
- Sale Completed modal now exposes:

```text
[Print Receipt] [New Sale]
```

- `Print Receipt` calls the print endpoint for the already committed sale id.
- `Print Receipt` does not start a new sale.
- `New Sale` remains separate.
- If the app adapter is disabled/unavailable, the modal shows printer status and the sale remains committed.

Hard rule preserved:

```text
Sale commit happens before any print attempt.
```

---

## 6. Reprint foundation

Sales History detail dialog behavior:

- `Reprint Receipt` is exposed when the sale detail exposes a persisted receipt snapshot.
- Reprint calls the same print service using the existing sale id.
- Reprint uses persisted receipt snapshot text only.
- Reprint does not create a sale.
- Reprint does not create payments.
- Reprint does not create stock movements.
- Reprint does not change receipt identity.

The detail dialog remains read-only for sale/payment/stock state.

---

## 7. Failure behavior

Print status values:

- `NotAttempted`
- `Succeeded`
- `Failed`
- `Disabled/Unavailable`

Failure behavior:

- Printer failure is returned as a visible status.
- Error text is preserved in `ErrorMessage` when available.
- Checkout sale remains committed.
- Reprint failure does not mutate sale/payment/stock state.
- Disabled/no-printer state is explicit and safe.

The API returns a diagnostic envelope with:

- code
- message
- print.succeeded
- print.status
- print.saleId
- print.receiptNumber
- print.source
- print.adapterName
- print.errorMessage
- print.printedAtUtc

---

## 8. No-printer behavior

Default composition uses:

```text
DisabledReceiptPrinterAdapter
```

Therefore stores without printers can still:

- complete checkout
- keep POS-M1 receipt snapshots
- keep POS-M1 receipt `.txt` files
- see clear disabled/unavailable print status

Printing is optional and does not block sale completion.

---

## 9. Test print contract

Added endpoint:

```text
POST /api/receipts/test-print
```

It prints harmless sample text through the configured adapter. In default app composition this reports `Disabled/Unavailable`; in smoke tests, the no-op adapter returns `Succeeded` without requiring a physical printer.

Sample text explicitly says it is not an official receipt and creates no sale, payment, or stock movement.

---

## 10. Automated coverage added

Added:

```text
tests/Salryn.Tests/PosM2PrinterReprintFailureChecks.cs
```

Coverage intent:

- print service succeeds with fake/no-op printer
- print failure returns diagnosable `Failed` status
- disabled/no-printer path returns `Disabled/Unavailable`
- missing persisted snapshot returns `NotAttempted`
- print/reprint uses persisted receipt snapshot text exactly
- test print succeeds with fake/no-op adapter
- service depends only on read repository + printer adapter, not write repositories

Targeted CoreSmoke target:

```powershell
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
```

---

## 11. Gates

Required commands:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-M2-PRINTER-ADAPTER-REPRINT-FAILURE
git reset --hard origin/KIRCH-POS-M2-PRINTER-ADAPTER-REPRINT-FAILURE

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
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
| `npm --prefix src\Salryn.App.Web run build` | `BLOCKED` — local shell unavailable. |
| `git diff --check` | `BLOCKED` — local shell unavailable. |
| `git status` | `BLOCKED` — local worktree unavailable. |

---

## 12. Manual proof checklist

Run after local build/web build passes:

- [ ] Complete a sale.
- [ ] Sale Completed modal shows `Print Receipt` and `New Sale`.
- [ ] `Print Receipt` does not start a new sale.
- [ ] `New Sale` starts only when clicked.
- [ ] If printer is disabled/unavailable, UI shows clear printer status.
- [ ] Sale remains committed when printing is disabled/unavailable.
- [ ] Receipt `.txt` still exists after checkout.
- [ ] Sales History detail opens.
- [ ] Sales History detail exposes `Reprint Receipt` when persisted receipt snapshot exists.
- [ ] Reprint does not duplicate sale.
- [ ] Reprint does not duplicate stock movement.
- [ ] Reprint does not change receipt number or sale totals.
- [ ] No console errors.
- [ ] No backend exceptions.

---

## 13. Explicit non-claims

This branch does not claim:

- GREEN
- ACCEPTED
- STORE_READY
- BETA_READY
- PRODUCTION_READY
- BIR_READY
- physical printer readiness
- ESC/POS readiness
- Windows printer readiness
- LAN printer readiness
- official receipt/legal compliance
- barcode scanner support
- Fast Add support
- product write support
- stock-in/adjustment support
- void/refund support
- installer readiness
- RMO/BIR/eSales readiness

---

## 14. Status

Status: `BLOCKED`

Reason: implementation was authored through the GitHub connector, but required local build, smoke, web build, git checks, real-printer/no-printer behavior proof, and browser/manual proof could not be executed from this session.
