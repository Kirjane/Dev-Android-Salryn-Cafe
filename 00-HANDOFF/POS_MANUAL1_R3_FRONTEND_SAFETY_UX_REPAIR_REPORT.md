# POS-MANUAL1-R3 Frontend Safety UX Repair Report

Date: 2026-07-14  
Branch: `KIRCH-POS-MANUAL1-R3-FRONTEND-SAFETY-UX-REPAIR`  
Base branch: `KIRCH-POS-MANUAL1-FRONTEND-REPAIR`  
Base SHA: `ba0676f10ab94580f44b06650f90c3ce45b724c9`  
Final SHA: connector branch head verified in final response.

Authoring mode: GitHub connector-backed implementation only. Local build/CoreSmoke/web build/diff/status gates were not executed from this session.

---

## Checkout completion change

The Sale Completed dialog no longer offers the route-away `View Sales & Receipts` button. This avoids losing the visible completion state when returning to Checkout.

The dialog still keeps the sale location discoverable with `Saved under Sales & Receipts` copy, and keeps local actions for `Print Receipt` and `New Sale (F5)`.

No broad checkout state persistence patch was added.

---

## Receipt / reprint duplicate proof

Sales & Receipts now labels the persisted receipt text area as `Receipt Snapshot Preview` and renders the saved snapshot in a bounded receipt-like text block.

The new `pos-manual1-r3-frontend-safety-ux-repair` smoke creates a real temporary database sale, records sale/payment/stock movement counts and stock quantity, calls the receipt print service with a no-op adapter, then proves sale count, payment count, stock movement count, and stock quantity remain unchanged after reprint.

No physical printer adapter implementation was added.

---

## Product deactivation guard

Product Catalog deactivation now opens a confirmation dialog before calling the deactivate endpoint.

The dialog explains:

- the product becomes inactive and hidden from the active sale flow
- historical sales remain preserved
- past records and saved receipt snapshots are not deleted or rewritten

A reason is still required before the confirmation action can proceed.

---

## Inventory / scanner / Fast Add copy

Checkout now states barcode scanner keyboard-wedge input is supported through the scan/search box.

Checkout and Stock Movements now explain Fast Add currently starts from Checkout when an unknown barcode is scanned or entered.

Product Catalog states product barcode fields accept manual barcode entry.

Stock Movements states Inventory Fast Add and scanner-driven receiving from Stock-In/Adjustment are deferred unless implemented later, and that inventory scans do not silently create products.

---

## Stock Adjustment layout fix

Stock Movements now keeps movement filters in a fixed panel and keeps product lookup plus quantity in a fixed area inside the stock write dialog.

Copy explicitly says toggling Stock-In / Stock Adjustment or changing filters should not move the ledger search layout awkwardly.

---

## Dashboard / Owner Reports integration

Dashboard now reads the existing Owner Reports summary when available and uses it for:

- Today Sales
- Transactions
- Low Stock
- Voided Today

Dashboard also includes an `Owner report summary` section with today, week, month, and low-stock summary cards while keeping the detailed Owner Reports page available.

---

## Advanced / deferred backup clarification

The Advanced navigation section now includes purpose copy: host setup, reporting utilities, and LAN controls.

It states `Backup & Restore UI planned; backend foundation exists` and explicitly says no restore/create/list controls are exposed there.

No Backup UI implementation, path exposure, create/list/restore controls, or backup workflow was added.

---

## Engineering copy cleanup

The R3 smoke scans operator-facing frontend source for milestone and engineering terms:

- POS-M1 / POS-M2 / POS-M3 / POS-M4 / POS-M5 / POS-M6 / POS-M7
- POS-R1 / POS-S1 / POS-T1
- runtime proof
- CoreSmoke
- milestone
- candidate branch

These terms are allowed in tests and handoff docs, but the smoke blocks them from the selected operator-facing frontend source set.

---

## Forbidden-claim scan

The R3 smoke scans selected operator-facing frontend source for forbidden claims including:

- BIR approved
- official receipt
- official invoice
- production eSales
- legal compliance guaranteed
- physical printer proven
- ESC/POS proven
- scanner hardware proven
- backup UI complete
- licensing final
- installer ready
- beta ready
- store ready

No such claim is intentionally introduced.

---

## Deferred items

Still deferred:

- physical printer adapter implementation
- ESC/POS proof
- Backup UI implementation
- licensing
- LAN terminal entitlement implementation
- installer/package proof
- Store logo upload
- Auditor role
- Inventory Fast Add / scanner receiving
- I1

---

## Manual proof checklist update

Manual re-check should verify:

- Checkout completed-sale dialog stays on Checkout and no longer routes away
- Sales & Receipts remains discoverable from nav/dashboard/completion copy
- Receipt Snapshot Preview reads like a receipt-form view
- Reprint does not create duplicate sales/payments/stock movement in runtime use
- Product deactivate requires confirmation and clear reason
- Product Catalog / Stock Movements scanner and Fast Add copy is understandable
- Stock-In / Adjustment dialog layout does not jump awkwardly
- Dashboard owner-report summary makes reports feel connected
- Advanced section copy explains deferred backup without fake controls
- No visible engineering milestone terms or forbidden commercial/legal claims appear in operator-facing pages

---

## Gates run

Not executed from this session:

```powershell
dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-r3-frontend-safety-ux-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-frontend-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-t1-full-store-runtime-torture
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-b1-backup-restore-safety
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-s1-store-settings-identity-printer
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-r1-owner-reports
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-surface1-role-page-lock-store-nav
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m7-void-sale-stock-reversal
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m6-stock-in-stock-adjustment
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m5-product-write-management
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m4-fast-add-unknown-barcode-inventory
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status in this session: `BLOCKED` until Kirch runs local gates.

---

## Next recommendation

Run the full required gate block locally. If it passes, proceed to manual R3 re-check only. Do not start PRINTER1, BACKUP-UI1, licensing, installer, or I1 from this branch.

---

## Status

`BLOCKED`

Reason: code and automated proof were added through GitHub, but local automated gates were not executed from this session.
