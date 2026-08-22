# POS MANUAL1 Frontend Repair Report

## Branch

- Branch: `KIRCH-POS-MANUAL1-FRONTEND-REPAIR`
- Base branch: `KIRCH-POS-CANDIDATE-T1`
- Base SHA: `d7ade5b359a690a1d1d51a4d38aaef500d4d859d`
- Final SHA: recorded from the committed branch tip in the final handoff output
- Status target: `CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`

## Files changed

- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx`
- `src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx`
- `src/Salryn.App.Web/src/pages/UsersPage.tsx`
- `tests/Salryn.Tests/PosManual1FrontendRepairChecks.cs`
- `tests/Salryn.Tests/LanM16HostClientIdentityUxChecks.cs`
- `tests/Salryn.Tests/PosM5ProductWriteManagementChecks.cs`
- `tests/Salryn.Tests/PosM6StockInStockAdjustmentChecks.cs`
- `tests/Salryn.Tests/WebB0ReadOnlyOperatorSurfacesChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/POS_MANUAL1_FRONTEND_REPAIR_REPORT.md`

No backend endpoint, database, checkout math, receipt snapshot, printer adapter, backup, licensing, LAN transport, installer, RMO, BIR, or eSales implementation was changed.

## Repair summary

This lane repairs operator discovery and commercial clarity for already implemented POS behavior. It does not widen business capability.

### Sales and receipts

- Renamed the main navigation label from `Sales History` to `Sales & Receipts` while preserving route key `sales` and `Sales.ViewHistory` permission behavior.
- Added `View Sales & Receipts` to the completed-checkout dialog.
- Kept Print Receipt and New Sale as separate actions. Opening Sales & Receipts does not clear or start a sale.
- Renamed generic sale `View` action to `Sale & Receipt Details`.
- Replaced milestone terminology with operator copy explaining that reprint reads the saved receipt snapshot and does not create another sale, payment, or stock movement.
- Kept receipt file path secondary in checkout status; no raw path browser or caller-controlled path was introduced.

### Void discoverability

- Retained the existing `Sales.Void` permission and completed-sale eligibility rule.
- Kept Void inside Sale & Receipt Details where the operator can inspect the sale first.
- Added a required acknowledgement checkbox in addition to the required reason.
- Changed the action to `Confirm Void Sale` and stated that the action cannot be repeated.
- Explained that the original sale, payment, and saved receipt remain historical while stock is restored through reversal movements.
- Reset reason, confirmation, and result state when changing selected sales.

No void service, reversal math, payment behavior, receipt immutability, or double-void backend behavior changed.

### Store Settings

- Rebuilt the flat form into clear commercial sections:
  - Store profile
  - Regional / Currency
  - Receipt appearance
  - Printer
  - Planned administration surfaces
- Constrained currency selection to `PHP` or `₱` for new valid saves. An unsupported existing value is displayed and must be corrected before save; it is not silently changed.
- Added clear field guidance for store identity, TIN, receipt header/footer, and receipt width.
- Made historical receipt immutability explicit.
- Added an honest printer warning: Disabled mode is safe; Windows printer support is not active and requires the separate printer-adapter lane and physical proof.
- Disabled selecting Windows printer when it was not already persisted. Existing persisted WindowsPrinter state remains visible so the page does not silently rewrite it.
- Added deferred logo copy only. No upload field, file path, media storage, or placeholder image was added.
- Added clear loading, validation, success, and failure presentation.

### Product and inventory wording

- Changed `Unit` to `Unit of measure` in Product Catalog.
- Changed stock labels to `Stock on hand`.
- Added guidance that unit of measure describes how an item is sold/counted, while stock quantity changes only through Stock-In or Stock Adjustment.
- Kept Edit Product stock-neutral.
- Kept Stock-In and Stock Adjustment entry points in Product Catalog and Inventory.
- Replaced implementation/milestone copy with operator descriptions of recorded movement behavior.
- Kept Inventory Fast Add deferred; checkout Fast Add was not repurposed as a receiving workflow.

### Dashboard and reports

- Moved the large LAN explanation out of the top dashboard hierarchy.
- Added a Store operations action row for Sales & Receipts, Stock Movements, and Owner Reports.
- Retained the four real dashboard metrics: Today Sales, Transactions, Products, and Low Stock.
- Kept LAN as a smaller host-configuration card after operational content.
- Added bounded internal scrolling to Top Products, Cashier Totals, Category Totals, and Low Stock report tables.
- Did not invent void, backup, printer, license, revenue, or compliance metrics.

### Roles and navigation

- Added a Users-page role guide:
  - Admin manages store configuration and users.
  - Manager handles catalog, inventory, sales, voids, and owner reports.
  - Cashier is limited to Checkout and Sales & Receipts.
  - Auditor is not available in this build.
- Manager and Cashier remain creatable through the existing Users flow.
- Cashier page restrictions and all existing permission checks remain unchanged.
- The Manager `StoreTerminal.View` versus Host LAN role-allowlist mismatch was documented but not widened here. Host LAN Control includes management actions; exposing the entire page to a view-only Manager without action-level frontend capability modeling would be misleading. A later LAN role-policy review should decide whether to add a truly read-only Manager surface or remove the unused permission.

### Engineering copy cleanup

Removed operator-visible occurrences of:

- `POS-M2`
- `POS-M4`
- `POS-M5`
- `POS-M6`
- `runtime proof`

The repair does not remove milestone names from tests, handoff reports, or internal engineering documentation.

## Automated proof added

Added focused CoreSmoke target:

```text
pos-manual1-frontend-repair
```

`PosManual1FrontendRepairChecks` verifies source-level UX boundaries appropriate to this lane:

- Sales & Receipts navigation label.
- Completed-checkout navigation action.
- Print and New Sale remain separate.
- Sale & Receipt Details/reprint copy.
- Explicit void reason/confirmation/discoverability.
- Configure Store sections and honest printer/logo status.
- Unit of measure and stock-on-hand guidance.
- Dashboard store-operation priority and bounded report scrolling.
- Role guide and explicit absence of Auditor.
- Engineering milestone terms absent from scoped operator pages.
- No BIR/eSales approval copy introduced.

Business correctness remains covered by the existing M1-T1 behavioral gates. The new gate does not pretend copy assertions prove sale, stock, payment, receipt, void, or backup behavior.

## Manual proof checklist

Manual proof remains deferred to Kirch:

1. Login as Admin.
2. Confirm nav shows `Sales & Receipts`.
3. Complete a sale and confirm the result shows three separate actions: View Sales & Receipts, Print Receipt, New Sale.
4. Open Sales & Receipts and locate the completed sale.
5. Open Sale & Receipt Details and verify saved receipt/reprint status is understandable.
6. Confirm disabled printer status remains honest and non-destructive.
7. For a completed sale, enter a void reason; confirm Void remains disabled until acknowledgement is checked.
8. Confirm void, visible Voided status, stock reversal movement, and repeated-void rejection.
9. Open Configure Store; review all sections, save PHP/₱ settings, and confirm new-receipt-only wording.
10. Confirm Windows printer is not presented as working and no logo upload exists.
11. Confirm Product Catalog distinguishes Unit of measure from Stock on hand and stock actions remain reachable.
12. Confirm Stock Movements explains recorded quantity changes.
13. Confirm Dashboard prioritizes operational metrics/actions before LAN configuration.
14. Load Owner Reports with larger data and verify each major table scrolls within its section.
15. From Users, create one Manager and one Cashier with local test credentials.
16. Verify Manager sees expected operational/report pages and Cashier sees Checkout plus Sales & Receipts only.
17. Confirm no engineering milestone terms appear in normal operator surfaces.
18. Confirm no browser-console or backend errors.

## Intentionally deferred

- Windows printer adapter and physical printer proof: PRINTER1.
- ESC/POS, cutter, cash drawer, and thermal profiles.
- Controlled backup frontend: BACKUP-UI1.
- License status/import, public-key verification, durable storage, device binding, and LAN terminal entitlements: LIC-FINAL.
- Auditor role.
- Inventory receiving Fast Add.
- Store logo file/media implementation.
- Host LAN Manager view policy redesign.
- Installer/package proof and I1.
- BIR/RMO/eSales/payment processing/legal approval.

## Known limitations

- Manual UI acceptance has not been performed in this lane.
- Physical printer/scanner behavior remains unproven.
- Host pages still use internal React page state rather than bookmarkable URL routes; direct operator actions now cover the primary receipt-discovery gap.
- Backup and licensing status cannot appear on Dashboard until their frontend/API integration lanes are complete.
- Advanced official/reporting surfaces remain separate candidate boundaries and are not production/legal approval.
- The existing Vite large-chunk advisory remains a non-blocking frontend quality item.

## Gate results

During the milestone-boundary run, full CoreSmoke exposed three stale source-copy assertions after the operator wording changed. The M5 and M6 assertions now check the current Product Catalog boundaries, and the LAN-M16 assertion now checks the retained LAN safety guidance and Host LAN Control action. No behavioral smoke coverage was weakened or removed.

- `dotnet build Salryn.sln`: PASS, 0 warnings, 0 errors.
- Full CoreSmoke: PASS.
- `pos-manual1-frontend-repair`: PASS.
- `pos-t1-full-store-runtime-torture`: PASS.
- `pos-b1-backup-restore-safety`: PASS.
- `pos-s1-store-settings-identity-printer`: PASS.
- `pos-r1-owner-reports`: PASS.
- `pos-surface1-role-page-lock-store-nav`: PASS.
- `pos-m7-void-sale-stock-reversal`: PASS.
- `pos-m6-stock-in-stock-adjustment`: PASS.
- `pos-m5-product-write-management`: PASS.
- `pos-m4-fast-add-unknown-barcode-inventory`: PASS.
- `pos-m3-barcode-scanner-cashier-flow`: PASS.
- `pos-m2-printer-reprint-failure`: PASS.
- `pos-m1-receipt-snapshot-file-receipt`: PASS.
- `npm --prefix src\\Salryn.App.Web run build`: PASS; the existing Vite chunk-size advisory remains non-blocking.
- Manual UI proof: deferred to Kirch.

## Next recommended branch

`KIRCH-POS-PRINTER1-WINDOWS-PRINTER-WIRING`

Final status target: `CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`
