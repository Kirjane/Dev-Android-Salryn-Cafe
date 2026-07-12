# POS-M0 Store-Ready Gap Audit

Date: 2026-07-12  
Authoring mode: GitHub connector-backed audit only  
Branch: `KIRCH-POS-M0-STORE-READY-GAP-AUDIT`  
Base branch intended: `KIRCH-FRONTEND-WIRING-UX-M2`  
Base SHA: `b315554cdc7f5ce6a879c877a357610eca20c2ec`

Status rules used in this document:

- Feature status: `DONE`, `PARTIAL`, `MISSING`, `PARKED`, `STALE-DOC`
- Candidate/gate status: `CANDIDATE_PENDING_VERIFICATION`, `CANDIDATE_SELF_CHECKED_ONLY`, `FAILED`, `BLOCKED`

This audit is repo-backed from fetched repository files and connector code search. It is not a local shell audit. Local `rg`, `dotnet`, `npm`, `git diff --check`, and `git status` gates were not executable from this connector session and remain blocked until Kirch/Sol runs them locally at `C:\Users\kirch\Salryn`.

---

## 1. Baseline truth

| Item | Value |
| --- | --- |
| Audit branch | `KIRCH-POS-M0-STORE-READY-GAP-AUDIT` |
| Product baseline branch | `KIRCH-FRONTEND-WIRING-UX-M2` |
| Product baseline SHA | `b315554cdc7f5ce6a879c877a357610eca20c2ec` |
| Baseline commit title | `Avoid frontend wording smoke false positives` |
| Protected ref — stable lane | `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| Protected ref — installer lane | `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865` |
| Files intentionally changed | `00-HANDOFF/POS_M0_STORE_READY_GAP_AUDIT.md` only |

### Why the beta installer branch is excluded

`KIRCH-BETA-M1-INSTALLER-PACKAGE-STARTUP` is excluded from the product baseline because this task is a POS product readiness audit, not installer/report packaging work. Installer work is already separated under `KIRCH-INSTALLER-M1`, and the fetched installer ref documents internal installer pre-release workflow, unsigned MSI packaging, and real-machine installer testing boundaries rather than product feature completion.

Therefore:

- Product baseline remains `KIRCH-FRONTEND-WIRING-UX-M2` at `b315554cdc7f5ce6a879c877a357610eca20c2ec`.
- Installer branches remain non-product-baseline inputs until POS runtime gaps are closed and install proof is explicitly reopened.
- No installer packaging, MSI, release, shortcut, ProgramData, or LocalAppData work is performed in POS-M0.

---

## 2. Required sources inspected

| Source | Result |
| --- | --- |
| `01-FEATURE-LOCK/FEATURE_LOCK.md` | Inspected. Defines v1 feature intent and old final build direction. |
| `00-HANDOFF/V1_BUILD_ORDER.md` | Inspected. Defines old phased build order and transaction-first rules. |
| `00-HANDOFF/FRONTEND_WIRING_UX_M2_REPORT.md` | Inspected. Defines current frontend wiring scope and known parked/disabled features. |
| `Salryn.sln` | Inspected. Confirms mixed current solution: core, infrastructure, WinForms, API, WebView, web-related host projects. |
| `src/Salryn.Api.Local/Program.cs` | Inspected. Confirms API smoke surface and local composition startup. |
| `src/Salryn.Api.Local/LocalApiApplicationFactory.cs` | Inspected. Confirms current local API, static web dist hosting, auth/session/dashboard/product/checkout/user/audit/official/report endpoint registration. |
| `src/Salryn.Api.Local/Services/OperatorReadService.cs` | Inspected. Confirms read-side products, inventory, sale history, users, audit/error log projections. |
| `src/Salryn.App.Web/src/api/products.ts` | Inspected. Confirms read-only product GET wrappers. |
| `src/Salryn.App.Web/src/api/inventory.ts` | Inspected. Confirms low-stock and stock movement GET wrappers. |
| `src/Salryn.App.Web/src/api/sales.ts` | Inspected. Confirms sales history/detail GET wrappers. |
| `src/Salryn.App.Web/src/api/checkout.ts` | Inspected. Confirms shift and cash checkout POST wrappers. |
| `src/Salryn.App.Web/src/api/dashboard.ts` | Inspected. Confirms today dashboard summary wrapper only. |
| `src/Salryn.App.Web/src/api/users.ts` | Inspected. Confirms users/roles/detail/create wrappers only. |
| `src/Salryn.App.Web/src/api/audit.ts` | Inspected. Confirms audit/error log wrappers. |
| `src/Salryn.App.Web/src/pages/ProductsPage.tsx` | Inspected. Confirms read-only catalog UI and absent product writes. |
| `src/Salryn.App.Web/src/pages/InventoryPage.tsx` | Inspected. Confirms read-only stock attention/movement UI and absent adjustment UI. |
| `src/Salryn.App.Web/src/pages/CheckoutPage.tsx` | Inspected. Confirms current search/cart/cash checkout UI and no reliable print/reprint flow. |

---

## 3. Required searches

The user-requested local `rg` commands could not be executed in this connector-only session. The equivalent GitHub connector searches and direct file fetches produced the following audit notes. These are evidence limits, not a substitute for local `rg` output.

| Requested search | Connector/direct-inspection result |
| --- | --- |
| `rg "Receipt|receipt|PrintJob|print_job|Printer|ESC|EscPos|reprint|snapshot" src tests tools 00-HANDOFF 01-FEATURE-LOCK` | Connector search surfaced planning docs. Direct code inspection shows checkout result displays `receiptNumber`, but no React file printer, ESC/POS adapter, test print, or reprint wrapper was found in inspected API/page layer. |
| `rg "Barcode|barcode|scanner|FastAdd|Fast Add|unknown barcode|scan box" src tests tools 00-HANDOFF 01-FEATURE-LOCK` | Connector search returned no code result. Direct inspection shows barcode fields and search placeholders, but no scanner-focused always-on scan box, Enter suffix handling, unknown barcode detection, or Fast Add flow in inspected React layer. |
| `rg "CreateProduct|UpdateProduct|DeactivateProduct|FindByBarcode|ProductService|ProductRepository" src tests tools 00-HANDOFF` | Connector search surfaced planning docs. Direct frontend API inspection shows product read wrappers only; no create/update/deactivate wrapper in inspected React API. |
| `rg "StockIn|AdjustStock|InventoryService|InventoryRepository|stock_movements|movement" src tests tools 00-HANDOFF` | Connector search surfaced planning docs. Direct inspection shows stock movement read wrappers and read service projections, but no React stock-in or adjustment mutation wrapper. |
| `rg "Void|void sale|VOID_SALE|return stock|reversal" src tests tools 00-HANDOFF` | Connector search surfaced feature/build/database docs. Current local API registers official document lifecycle endpoints, but those are RMO/traditional-reporting boundary surfaces and are not treated as POS void-sale completion. No inspected React sales wrapper exposes void. |
| `rg "Daily|Weekly|Monthly|summary|top products|cashier|category|inventory value|CSV|PDF" src tests tools 00-HANDOFF` | Connector search returned no direct code result. Direct inspection shows `/api/dashboard/summary` with today totals, transaction count, product count, low-stock count only; no week/month/top-products/cashier/category/inventory-value POS reporting wrapper found. |
| `rg "Backup|Restore|backup_logs|backup before restore" src tests tools 00-HANDOFF` | Connector search surfaced planning docs only. FRONTEND_WIRING_UX_M2 report says Backup nav is disabled because no approved backup API contract is wired. |
| `rg "Settings|StoreSettings|ReceiptSettings|PrinterSettings|receipt width|footer|TIN" src tests tools 00-HANDOFF` | Connector search surfaced planning docs only. FRONTEND_WIRING_UX_M2 report says Settings nav is disabled because no approved settings API contract is wired. |
| `rg "installer|package|publish|ProgramData|LocalAppData|shortcut" src tests tools 00-HANDOFF` | Connector search did not produce product-baseline code evidence. Installer ref exists separately and is excluded from POS product baseline. |

Local follow-up required:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-M0-STORE-READY-GAP-AUDIT
# Re-run all requested rg commands exactly from the user prompt.
```

---

## 4. Architecture drift

### Old Feature Lock direction

`01-FEATURE-LOCK/FEATURE_LOCK.md` defines Salryn as a native offline-first Windows POS and states the old final build direction as:

- C#
- WinForms
- SQLite
- ESC/POS receipt printing
- keyboard-wedge barcode scanner

It also explicitly marks React/web-app architecture as avoid-for-now in the old planning document.

### Current product architecture

The current baseline is no longer only the original WinForms direction. The solution now includes:

- `Salryn.Core`
- `Salryn.Infrastructure`
- `Salryn.App.WinForms`
- `Salryn.Api.Local`
- `Salryn.Composition.Local`
- `Salryn.Desktop.WebView`
- `src/Salryn.App.Web` React frontend

`FRONTEND_WIRING_UX_M2_REPORT.md` describes the current shell as frontend wiring over real local API contracts. It also states that product, inventory, audit, dashboard, checkout, sales, users, official setup, traditional reporting, and production reporting are surfaced through the React shell at varying completeness levels.

### Drift decision

Old docs say WinForms. Current product uses WebView/React frontend over a local API. Therefore:

- Old docs are feature intent, not exact UI technology law.
- The old WinForms wording is stale as a UI implementation constraint.
- Behavioral requirements still stand unless Kirch explicitly parks them.
- The audit uses the old documents as requirements for POS behavior, not as a mandate to revert the current WebView/React direction.

---

## 5. Feature matrix

Legend:

- Store blocker `YES`: must be closed or explicitly accepted before relying on Salryn as a real store POS.
- Batchable `YES`: can be grouped with adjacent read-only/config/UI-only/reporting work.
- Dedicated lane: should not be mixed casually because it affects money, stock, receipt proof, printer failure, audit trail, data loss, security, or install/update safety.

| Feature | Source requirement | Current code evidence | Status | Store blocker | Batchable | Recommended lane |
| --- | --- | --- | --- | --- | --- | --- |
| Login / users / roles | Feature Lock requires admin/cashier login, accounts, permissions, active/inactive users. Build Order requires login, roles, permission checks, inactive user blocking, password hashing. | Local API maps auth/session and user admin endpoints. React `users.ts` supports list, roles, detail, and create. FRONTEND M2 says user edit/deactivate/reactivate/reset password are absent. | PARTIAL | YES | NO | POS-T1 |
| Store profile / settings | Feature Lock requires store name, business name, address, contact, optional TIN, receipt header/footer, logo path, width, currency, unit, low-stock toggle. Build Order requires Store/Receipt/Printer settings. | FRONTEND M2 says Settings nav disabled because no approved React settings API contract is wired. No inspected React settings wrapper. | MISSING | YES | YES | POS-S1 |
| Product create/edit/deactivate | Feature Lock requires add/edit/deactivate and no hard delete. Build Order requires create/update/deactivate product methods. | `products.ts` exposes read wrappers only. `ProductsPage.tsx` explicitly says catalog write actions are unavailable and catalog maintenance/deactivation remain parked. | MISSING | YES | NO | POS-M5 |
| Barcode lookup | Feature Lock requires scan barcode, exact barcode lookup first, product search fallback. | Product and checkout API models include barcode fields. Checkout page search placeholder supports barcode/search text via `/api/products`. No inspected code proves exact-first barcode path, scanner suffix handling, or focus discipline. | PARTIAL | YES | NO | POS-M3 |
| Fast Add Product | Feature Lock marks Fast Add as core selling feature; unknown barcode should open Fast Add with barcode prefilled and save product/stock. | FRONTEND M2 says product create/import/write actions are not wired. No inspected Fast Add React/API wrapper. | MISSING | YES | NO | POS-M4 |
| Inventory stock-in | Feature Lock and Build Order require stock-in and stock movement creation. | Inventory React API/page is read-only: low stock and stock movements only. | MISSING | YES | NO | POS-M6 |
| Inventory adjustment | Feature Lock and Build Order require adjustment with reason. | Inventory page explicitly states it does not perform stock adjustments because no approved React adjustment contract is wired. | MISSING | YES | NO | POS-M6 |
| Stock movement ledger | Feature Lock requires every stock change to create movement records. | `inventory.ts`, `InventoryPage.tsx`, and `OperatorReadService` expose stock movement reads. Full mutation coverage is not proven from inspected files. | PARTIAL | YES | NO | POS-M6 |
| Cart | Feature Lock requires add/search, merge same product, quantity change, remove, subtotal/discount/total, stock validation before checkout. | `CheckoutPage.tsx` keeps in-memory cart lines, merges same product by product id, allows quantity updates/removal, computes subtotal and local validation. Discount is not implemented in inspected UI. | PARTIAL | YES | NO | POS-M3 |
| Checkout | Feature Lock requires cash payment, save sale/items/payment, reduce stock, receipt snapshot, print job, print after commit. | `checkout.ts` posts `/api/checkout`; `CheckoutPage.tsx` builds cash-only checkout request and shows receipt number/sale id. Receipt snapshot/print job/print failure proof not confirmed. | PARTIAL | YES | NO | POS-M1 |
| Receipt snapshot | Feature Lock requires saved receipt snapshot and sales history view. Build Order requires reprint from snapshot. | Sales detail models include item snapshots; checkout displays receipt number only. No inspected file exposes full saved receipt text snapshot or reprint-from-snapshot behavior. | PARTIAL | YES | NO | POS-M1 |
| File receipt printer | Build Order requires `FileReceiptPrinter` before physical printer. | No inspected React/API wrapper or concrete current file receipt output proof. | MISSING | YES | NO | POS-M1 |
| ESC/POS printer | Feature Lock requires ESC/POS support. Build Order requires ESC/POS printer after backup/restore. | No inspected adapter/wrapper/proof. | MISSING | YES | NO | POS-M2 |
| Test print | Feature Lock and Build Order require test print. | No inspected printer settings/test-print API or UI. | MISSING | YES | NO | POS-M2 |
| Reprint receipt | Feature Lock requires reprint receipt and reprint last receipt. | Sales history wrapper exposes reads only; checkout page has no reprint button. Official lifecycle endpoints exist under RMO/traditional boundary and are not counted as POS reliable reprint. | MISSING | YES | NO | POS-M2 |
| Sales history | Feature Lock requires receipt/date/cashier search, details, snapshot, reprint, void. | `sales.ts` supports history and detail. `OperatorReadService` supports search by date/status/receipt, but cashier filter is passed null. Reprint/void absent in inspected React API. | PARTIAL | YES | YES | POS-M7 |
| Void sale | Feature Lock requires reason, authorized user, mark voided, return stock, reversal movement, audit log. | No inspected POS void sale React/API wrapper. Official document lifecycle endpoints are RMO/traditional boundary and not counted here. | MISSING | YES | NO | POS-M7 |
| Daily summary | Feature Lock requires daily sales summary. | `dashboard.ts` exposes today sales total and today transaction count. It is dashboard-level, not a full daily report/export. | PARTIAL | NO | YES | POS-R1 |
| Weekly summary | User-required store-readiness item. | No inspected wrapper/page supports weekly POS summary. | MISSING | NO | YES | POS-R1 |
| Monthly summary | Feature Lock requires monthly summary and monthly PDF. | Traditional reporting endpoints exist but are RMO/draft boundary. No inspected POS owner monthly summary wrapper/page. | MISSING | NO | YES | POS-R1 |
| Top products | Feature Lock requires top-selling products. | No inspected POS reporting wrapper/page. | MISSING | NO | YES | POS-R1 |
| Sales by cashier | Feature Lock requires sales by cashier/cashier breakdown. | Sales history read service has `CashierUserId: null` in inspected search path; no cashier summary wrapper. | MISSING | NO | YES | POS-R1 |
| Sales by category | Feature Lock requires sales by category. | No inspected POS reporting wrapper/page. | MISSING | NO | YES | POS-R1 |
| Inventory value | Feature Lock and Build Order require inventory value estimate. | No inspected POS reporting wrapper/page. | MISSING | NO | YES | POS-R1 |
| Backup | Feature Lock requires manual backup, timestamp, verification, logs. | FRONTEND M2 says Backup nav disabled because no approved backup API contract is wired. | MISSING | YES | NO | POS-B1 |
| Restore | Feature Lock requires restore warning, admin, audit log, backup before restore. | FRONTEND M2 says Backup nav disabled; no inspected restore contract. | MISSING | YES | NO | POS-B1 |
| Audit logs | Feature Lock requires login/failure/product/stock/sale/receipt/settings/backup/user audit events. | `audit.ts`, `AuditPage` in M2 report, and `OperatorReadService` expose audit/error reads. Event coverage for all required mutating actions is not proven, and many mutating actions are missing. | PARTIAL | YES | NO | POS-T1 |
| Installer/package | Build Order deployment prep requires production path, first-run setup, shortcut later, backup folder, handoff checklist. | Installer ref exists separately and is excluded from this product audit. No installer work is performed here. | PARKED | YES | NO | POS-I1 |
| License behavior | Feature Lock lists licensing as good later and says avoid until core POS is stable. FRONTEND M2 says no licensing changes. | License endpoints are registered, but license behavior is intentionally outside store-ready POS core until transaction/receipt/backup lanes are stable. | PARKED | NO | NO | Parked |
| RMO/BIR/eSales boundary | User explicitly excludes RMO/BIR/eSales work. FRONTEND M2 preserves traditional reporting/official boundaries without legal activation. | Local API registers official and reporting endpoints, but audit treats them as parked boundary surfaces. | PARKED | NO | NO | Parked |

---

## 6. Dedicated lanes

Classified as dedicated because they affect money, stock, receipt proof, printer failure, audit trail, data loss, security/permissions, or install/update safety.

| Lane | Dedicated reason |
| --- | --- |
| POS-M1 Receipt Snapshot + File Receipt | Receipt proof; money trail; sale must survive print failure. |
| POS-M2 Printer Adapter + Reprint + Failure Handling | Printer failure and proof duplication risks. |
| POS-M3 Barcode Scanner Cashier Flow | Cashier speed; wrong product risk; money/stock impact. |
| POS-M4 Fast Add Product / Unknown Barcode Inventory Add | Creates product and stock under cashier pressure. |
| POS-M5 Product Write Management | Selling price/cost/active-state changes affect money and inventory truth. |
| POS-M6 Stock-In / Stock Adjustment | Direct stock mutation and movement ledger truth. |
| POS-M7 Void Sale + Reversal | Sale reversal, stock return, and audit trail. |
| POS-B1 Backup/Restore Safety | Data loss and restore integrity. |
| POS-T1 Full Store Runtime Torture Proof | Cross-feature proof, security/permissions, runtime regression. |
| POS-I1 Portable Package / Install Proof | Install/update safety and client data path safety. |

---

## 7. Batchable lanes

Classified as batchable because they are read-only reporting, UI-only boundary text, grouped settings contracts, dashboard/summary projections, or non-mutating filters.

| Batchable area | Recommended handling |
| --- | --- |
| POS-R1 Owner summaries | Batch daily/weekly/monthly/top products/cashier/category/inventory value projections after sales and stock mutation lanes are stable. |
| POS-S1 Store/Receipt/Printer Settings | Batch under one configuration contract, but keep printer test/failure handling in POS-M2. |
| Read-only filters | Batch product/inventory/sales/audit filters if they do not mutate money/stock. |
| Boundary copy | Batch copy updates that clarify parked/non-claims without changing behavior. |
| Dashboard projections | Batch non-mutating cards after source queries are stable. |

---

## 8. Parked lanes

Classified as parked under the user's constraints and current product maturity.

| Parked area | Reason |
| --- | --- |
| BIR/eSales/legal/accreditation | Explicitly excluded. Current official surfaces remain boundary/draft/internal unless Kirch separately reopens them. |
| Crystal runtime | Explicitly outside POS store-ready core and production report renderer remains unavailable. |
| Cloud upload / Vercel licensing cloud | Avoid until local POS core is stable. |
| Multi-branch / cloud-first mode | Old Feature Lock says avoid for v1; current local POS should stabilize first. |
| Non-cash payments | Feature Lock says cash only for v1; GCash/Maya/card/bank/split are later. |
| Advanced supplier / PO / expiry / barcode label printing | Feature Lock good-to-have; not a store-ready blocker for base general merchandise POS. |
| Public production release | Not allowed in this task and not supported by current gaps. |
| License behavior | Feature Lock says avoid until core POS is stable. |
| Installer/package execution | Installer refs remain separate; no installer work in POS-M0. |

---

## 9. Recommended execution order

Strict order for next lanes:

1. **POS-M1 Receipt Snapshot + File Receipt**
   - Goal: make sale proof durable before physical printing.
   - Must prove: saved receipt snapshot, readable text renderer, file receipt output, old receipts unchanged after product edits.
   - Not in lane: ESC/POS, UI polish, RMO/BIR/eSales.

2. **POS-M2 Printer Adapter + Reprint + Failure Handling**
   - Goal: add real printer contract after file proof.
   - Must prove: printer failure does not rollback sale; reprint uses snapshot; test print exists; failed print job can be diagnosed.
   - Not in lane: legal official printing, Crystal, installer.

3. **POS-M3 Barcode Scanner Cashier Flow**
   - Goal: make keyboard-wedge scanner reliable in cashier flow.
   - Must prove: scan box focus, Enter suffix behavior, exact barcode lookup first, repeated scan merge/increase, search fallback.
   - Not in lane: Fast Add write path unless explicitly sequenced into POS-M4.

4. **POS-M4 Fast Add Product / Unknown Barcode Inventory Add**
   - Goal: unknown barcode can become sellable inventory quickly.
   - Must prove: barcode prefilled, minimum fields, initial stock creates movement, save returns to cashier/cart path.
   - Not in lane: full catalog admin polish.

5. **POS-M5 Product Write Management**
   - Goal: controlled product create/edit/deactivate outside cashier panic path.
   - Must prove: duplicate barcode blocked, used products deactivated not deleted, price/cost changes audited.
   - Not in lane: imports/exports unless small and safe after core writes.

6. **POS-M6 Stock-In / Stock Adjustment**
   - Goal: trusted stock mutation and ledger.
   - Must prove: stock-in positive only, adjustment requires reason, stock_on_hand and movement insert share transaction, movement ledger reads back. 
   - Not in lane: supplier PO, expiry, barcode labels.

7. **POS-M7 Void Sale + Reversal**
   - Goal: safe completed-sale reversal.
   - Must prove: permission required, reason required, sale marked voided, stock returned, VOID_SALE movements inserted, audit log saved, totals exclude voided sale.
   - Not in lane: refund complexity unless separately scoped.

8. **POS-R1 Owner Day/Week/Month Summaries**
   - Goal: owner-useful reporting after sales/void/stock truth is stable.
   - Must prove: daily, weekly, monthly, top products, cashier, category, inventory value. CSV/PDF only after values are trusted.
   - Batchable: yes.

9. **POS-S1 Store/Receipt/Printer Settings**
   - Goal: client configuration without code edits.
   - Must prove: store identity, receipt header/footer, TIN optional, receipt width, printer config persistence, settings audit.
   - Batchable: yes, but printer failure behavior remains POS-M2-owned.

10. **POS-B1 Backup/Restore Safety**
    - Goal: protect client data.
    - Must prove: manual backup, verification, backup logs, admin restore, backup-before-restore, restore audit.
    - Dedicated: yes.

11. **POS-T1 Full Store Runtime Torture Proof**
    - Goal: prove the whole local store runtime under realistic cashier/owner workflows.
    - Must prove: permissions, auth expiry, shift state, product writes, scanner path, checkout, receipt, print failure, stock mutation, void, reports, backup/restore.
    - Dedicated: yes.

12. **POS-I1 Portable Package / Install Proof**
    - Goal: reopen packaging only after POS runtime is stable.
    - Must prove: production data path, first-run setup, shortcut/launcher, install/update data preservation, client handoff checklist.
    - Dedicated: yes.

---

## 10. Explicit answers

| Question | Answer |
| --- | --- |
| Is repo stable enough to continue? | Yes, as `CANDIDATE_PENDING_VERIFICATION` for the next focused POS lane. Not enough for real store reliance. |
| Is frontend done? | No. Checkout/read-side pages exist, but scanner flow, Fast Add, product writes, inventory writes, reprint, settings, backup, and reporting are not complete. |
| Is reporting engine done? | No. Current dashboard has today-level summary only; POS owner reports are missing. |
| Is RMO parked? | Yes. RMO/BIR/eSales remain parked for this POS audit. |
| Is day/week/month summary done? | No. Daily is partial via dashboard summary; weekly and monthly POS owner reports are missing. |
| Is receipt printing reliable? | No. Receipt number display exists, but file receipt, ESC/POS, test print, reprint, print-job failure handling, and snapshot proof are not complete. |
| Is barcode scanner reliable? | No. Barcode fields/search exist, but scanner focus, Enter suffix, exact lookup proof, and unknown-barcode flow are not complete. |
| Can inventory be added via barcode? | No. Fast Add / unknown barcode inventory add is missing. |
| Is repo reliable POS use today? | No. It is a candidate baseline for continuing engineering, not a reliable daily store POS. |

---

## 11. Gates

The following gates were required by the prompt but blocked in this connector session because no local shell access to `C:\Users\kirch\Salryn` was available.

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | BLOCKED — not executable from GitHub connector session. |
| `dotnet run --project tools/Salryn.Tools.CoreSmoke` | BLOCKED — not executable from GitHub connector session. |
| `npm --prefix src/Salryn.App.Web run build` | BLOCKED — not executable from GitHub connector session. |
| `git diff --check` | BLOCKED — not executable from GitHub connector session. |
| `git status` | BLOCKED — local worktree status unavailable from connector session. |

Required local verification command block:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-M0-STORE-READY-GAP-AUDIT
git rev-parse HEAD

dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

---

## 12. Audit status

Authoring status: `CANDIDATE_PENDING_VERIFICATION`  
Gate status: `BLOCKED`  
Overall POS readiness answer: not reliable POS use today.
