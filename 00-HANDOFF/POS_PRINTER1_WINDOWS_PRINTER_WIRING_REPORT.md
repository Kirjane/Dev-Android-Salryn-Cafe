# POS-PRINTER1 Windows Printer Wiring Report

Date: 2026-07-14

Branch: `KIRCH-POS-PRINTER1-WINDOWS-PRINTER-WIRING`

Base SHA: `03495df56541a0d9c3c7a749b29aa07e8435f0f9`

Final SHA: reported in final response after report commit.

Authoring mode: GitHub connector-backed implementation and review. Local build/CoreSmoke/web build/diff/status gates were not executed from this session.

---

## Files changed

- `00-HANDOFF/POS_PRINTER1_WINDOWS_PRINTER_WIRING_REPORT.md`
- `00-HANDOFF/POS_PRINTER1_FREEHAND_QUALITY_REVIEW.md`
- `src/Salryn.Core/Services/PosReceiptPrintService.cs`
- `src/Salryn.Core/Services/ReceiptPrinterAdapterFactory.cs`
- `src/Salryn.Core/Services/WindowsQueueReceiptPrinterAdapter.cs`
- `src/Salryn.Core/ReadModels/InventorySystemReadModels.cs`
- `src/Salryn.Core/Repositories/WriteRepositoryInterfaces.cs`
- `src/Salryn.Infrastructure/Data/Repositories/System/SqliteStoreSettingsReadRepository.cs`
- `src/Salryn.Infrastructure/Data/Repositories/System/SqliteStoreSettingsWriteRepository.cs`
- `src/Salryn.Infrastructure/Data/Migrations/WindowsPrinterNameSchemaMigration.cs`
- `src/Salryn.Infrastructure/Data/Initialization/DefaultDatabaseInitializationFactory.cs`
- `src/Salryn.Api.Local/Contracts/StoreSettingsContracts.cs`
- `src/Salryn.Api.Local/LocalStoreSettingsEndpoints.cs`
- `src/Salryn.Api.Local/Services/HostOperatorApiServices.cs`
- `src/Salryn.App.Web/src/api/storeSettings.ts`
- `src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx`
- `tests/Salryn.Tests/PosPrinter1WindowsPrinterWiringChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

---

## Check-first findings

### current abstraction

`IReceiptPrinterAdapter` already existed with `AdapterName`, `IsEnabled`, and `PrintAsync(jobName, receiptText)`.

`PosReceiptPrintService` already printed from persisted receipt snapshot for sale print/reprint and had a test-print path.

### current disabled adapter

`DisabledReceiptPrinterAdapter` existed and returned `Disabled/Unavailable`, keeping printing disabled safely.

### settings fields

Store Settings already persisted `PrinterMode` with `Disabled` and `WindowsPrinter`, but did not store a Windows printer name.

### composition issue

`HostOperatorApiServices` always constructed `PosReceiptPrintService` with `DisabledReceiptPrinterAdapter`, so runtime ignored `WindowsPrinter` settings.

### feasible implementation path

The safe path was to add a Windows printer name setting, select the adapter from current settings at print time, and submit plain receipt snapshot text through a Windows print-queue boundary while preserving disabled/unavailable behavior.

---

## Implementation

### Windows adapter

Added `WindowsQueueReceiptPrinterAdapter`.

Behavior:

- requires a Windows host
- requires a configured printer name
- writes saved receipt text to a temporary UTF-8 text file
- submits that text through `notepad.exe /pt <file> <printer>`
- returns clear success/failure/unavailable messages
- deletes the temporary file best-effort

The adapter does not implement ESC/POS commands, cutter, cash drawer, or printer discovery.

### adapter selection

Added `ReceiptPrinterAdapterFactory`.

Selection:

- missing settings -> disabled/unavailable
- `Disabled` -> disabled adapter
- `WindowsPrinter` with no printer name -> disabled/unavailable
- `WindowsPrinter` with printer name -> Windows queue adapter
- unsupported mode -> disabled/unavailable

`HostOperatorApiServices` now resolves the current store settings when a print/test-print request runs, so settings changes are not frozen at application startup.

### settings changes

Added `windows_printer_name` migration, read model field, write request field, API contract field, read/write repository mapping, and frontend API type field.

Windows Printer mode now requires a nonblank printer name at settings validation time.

### test print

The existing `/api/receipts/test-print` endpoint still calls `PosReceiptPrintService.TestPrintAsync`, but the service now resolves the configured adapter each call.

Disabled or unconfigured Windows mode returns disabled/unavailable clearly.

### sale print

Sale print still reads the completed sale and persisted receipt snapshot before attempting print. Print failure does not rollback or modify the sale.

### reprint

Reprint still uses the persisted receipt snapshot. The new PRINTER1 smoke proves the adapter receives the snapshot text exactly.

### frontend settings

Store Settings now exposes:

- `Disabled`
- `Windows Printer`
- `Windows printer name`

Copy states that Windows Printer uses the Windows print queue and that paper output remains unproven until manual printer testing.

### copy / claim boundaries

Frontend copy explicitly keeps thermal ESC/POS, cutter, and cash drawer deferred. It does not claim physical printer proof.

---

## Safety invariants

### sale saved before print

Sale print/reprint occurs only after sale lookup and persisted receipt snapshot retrieval. Checkout completion remains sale-first, print-after.

### failure non-destructive

Failure and disabled paths return status/messages only. No sale, payment, stock movement, product stock, or receipt snapshot write is performed by the print service.

### reprint non-mutating

The PRINTER1 smoke captures sale count, payment count, stock movement count, product stock, and receipt snapshot before/after failed print and reprint.

### disabled mode safe

Disabled mode uses a disabled adapter and returns `Disabled/Unavailable`. It does not attempt Windows queue submission.

---

## Quality review pass 1

Reviewed for compile-risk and compatibility.

Finding repaired: the existing MANUAL1/S1 frontend gate still expects the exact copy `Physical printing is not configured`. The printer settings UI now keeps that phrase while making the copy clear that paper output is unproven until Kirch completes manual printer testing.

No other repo-visible compile-risk issue was repaired in this pass.

---

## Quality review pass 2

Reviewed for safety, honesty, and scope control.

Confirmed:

- no ESC/POS implementation
- no cutter support
- no cash drawer support
- no backup UI
- no licensing
- no I1/package work
- no installer work
- no beta/store-ready claim
- no physical printer proven claim
- no official/BIR/eSales receipt claim

---

## Repairs made during review

- Kept legacy `Physical printing is not configured` copy required by existing gates, but changed the content to state that paper output remains unproven until manual Windows printer testing.

---

## Automated proof added

Added CoreSmoke target:

`pos-printer1-windows-printer-wiring`

Proof covers:

- Disabled mode default and disabled test-print behavior
- WindowsPrinter mode rejection when printer name is missing
- WindowsPrinter mode and printer-name round-trip
- adapter factory reaches fake Windows adapter boundary
- test print sends sample text to adapter boundary
- failed print does not mutate sale/payment/stock movement/stock/receipt snapshot
- reprint uses persisted receipt snapshot and does not mutate ledgers
- frontend source exposes Windows printer settings and honest copy
- frontend source avoids false proof/readiness claims

---

## Automated proof results

Not run from this session.

Local gates: `NOT RUN / NEEDS SOL`

---

## Manual proof deferred

Yes. Windows printer path is not `MANUAL_PROVEN` until Kirch performs the physical printer test.

---

## Manual printer test instructions

1. Confirm the printer can print from Windows Notepad.
2. Launch the Salryn dev API.
3. Open Store Settings.
4. Set Printer mode to `Windows Printer`.
5. Enter the exact Windows printer name.
6. Save settings.
7. Create a sale.
8. Click Print Receipt.
9. Open Sales & Receipts.
10. Reprint the same receipt.
11. Set the printer offline or unplug it.
12. Try print/reprint failure paths.
13. Confirm:
    - sale remains saved
    - no duplicate sale
    - no duplicate payment
    - no stock mutation
    - clear failure message
    - receipt content is readable

If successful, classify only the Windows printer path as `MANUAL_PROVEN`.

Still not proven:

- ESC/POS thermal printer
- cash drawer
- receipt cutter
- thermal receipt width profile

---

## Known limitations

- Windows submission uses the host Windows text print handler through `notepad.exe /pt`.
- Printer discovery is not implemented.
- Paper output cannot be proven by automated tests.
- Submitted-to-queue success does not guarantee paper output.
- ESC/POS, cutter, and cash drawer remain deferred.

---

## Gates

build: `NOT RUN / NEEDS SOL`

CoreSmoke: `NOT RUN / NEEDS SOL`

targeted PRINTER1: `NOT RUN / NEEDS SOL`

targeted R3: `NOT RUN / NEEDS SOL`

targeted MANUAL1: `NOT RUN / NEEDS SOL`

targeted T1: `NOT RUN / NEEDS SOL`

targeted B1: `NOT RUN / NEEDS SOL`

targeted S1: `NOT RUN / NEEDS SOL`

targeted R1: `NOT RUN / NEEDS SOL`

targeted SURFACE1: `NOT RUN / NEEDS SOL`

targeted M7: `NOT RUN / NEEDS SOL`

targeted M6: `NOT RUN / NEEDS SOL`

targeted M5: `NOT RUN / NEEDS SOL`

targeted M4: `NOT RUN / NEEDS SOL`

targeted M3: `NOT RUN / NEEDS SOL`

targeted M2: `NOT RUN / NEEDS SOL`

targeted M1: `NOT RUN / NEEDS SOL`

web build: `NOT RUN / NEEDS SOL`

diff check: `NOT RUN / NEEDS SOL`

git status: `NOT RUN / NEEDS SOL`

---

## Protected refs

Expected unchanged:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## Branch pushed

Yes. Changes were pushed to `KIRCH-POS-PRINTER1-WINDOWS-PRINTER-WIRING`.

---

## Next recommended action

Run SOL/local verification gates. If gates pass, run Kirch's non-thermal Windows printer manual test. Do not start Backup UI, licensing, LAN entitlement finalization, I1, installer, beta, or branch promotion from this branch.

---

## Status

`READY_FOR_SOL_VERIFICATION`
