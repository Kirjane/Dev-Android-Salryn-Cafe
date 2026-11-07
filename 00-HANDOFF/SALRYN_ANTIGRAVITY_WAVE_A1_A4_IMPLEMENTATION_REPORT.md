# Salryn POS — Wave A1–A4 Full Frontend UX Implementation Report (`R1`)

- **Document ID**: `SALRYN_ANTIGRAVITY_WAVE_A1_A4_IMPLEMENTATION_REPORT`
- **Mission**: `AG-SALRYN-PREMIUM-POS-FULL-UX-IMPLEMENTATION-R1` ([GitHub Issue #20 comment `6713249546`](https://github.com/Kirjane/Salryn/issues/20#issuecomment-6713249546) & [Issue #20](https://github.com/Kirjane/Salryn/issues/20))
- **Target Application**: Base **Salryn POS** Retail Application (Host Console + LAN Terminal Client + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics; **excludes Salryn Café Yza**)
- **Worktree & Branch**: `C:\Salryn-Antigravity` on `antigravity/salryn-premium-pos-ux` (stacked on `codex/salryn-owner-analytics-w0-w4` @ `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`)
- **Draft PR**: [PR #21](https://github.com/Kirjane/Salryn/pull/21) (maintained as **DRAFT**)

---

## 1. Executive Summary & Verification Matrix

| Gate / Deliverable | Scope | Status | Evidence / Result |
| :--- | :--- | :---: | :--- |
| **Deliverable 0 — Local A0-R1 Playwright Evidence ZIP** | Pre-implementation baseline evidence package (`SALRYN_AG_A0_R1_EVIDENCE_6094016.zip`) | **DELIVERED** | `C:\Salryn-Evidence\SALRYN_AG_A0_R1_EVIDENCE_6094016.zip` (`3,834,597` bytes; SHA-256 `406BBDE8FC79A5CCA27E3183DC1B1C424524970DC47FC284D6350C229EB664A6`) |
| **Wave A1 — Foundation & Wayfinding** | Design tokens (`index.css`), shared accessible `Dialog`, `Select`, `DataTable`, `MetricCard`, `CommandBar`, `Notifications`, 4-domain `AppShell` breadcrumbs, responsive mobile drawer, browser URL/history sync (`?page=<id>`), session retry recovery | **COMPLETE** | All `KNOWN-DEFECT` tests (`UX-004`, `UX-005`, `UX-008`) upgraded to positive pass assertions |
| **Wave A2 — Counter Operations & W4 Compatibility** | `CheckoutPage.tsx` Utang false `"Insufficient cash"` fix (`UX-001`), unified payment/settlement hierarchy (`UX-002`), accessible form labels (`UX-003`), `SalesHistoryPage.tsx` modal & filter polish, read-only W4 compatibility matrix (`PR #22`) | **COMPLETE** | `00-HANDOFF/SALRYN_W4_CHECKOUT_UI_COMPATIBILITY_MATRIX.md` + `UX-001 & UX-002 FIXED` E2E test passing |
| **Wave A3 — Finance, Catalog & Stock** | `OwnerReportsPage.tsx` accessible Period/From/To labels (`UX-003`) & bounded scrolling (`max-h-80 overflow-y-auto`), `ReceivablesPage.tsx` tabular numerals & U1 status pill, `ProductsPage.tsx` → `InventoryPage.tsx` `"View Movements"` navigation (`UX-005`) + shared `Dialog` (`UX-004`) | **COMPLETE** | `UX-004 FIXED` & `UX-005 FIXED` E2E tests passing |
| **Wave A4 — Security, Admin, Recovery & Edge States** | `BackupRestorePage.tsx` removal of 3 redundant post-action OK modal popups (`UX-010`), `AuditPage.tsx` ARIA tablist semantics, `StoreSettingsPage.tsx` live shell logo invalidation | **COMPLETE** | `UX-010 FIXED` E2E test passing |
| **Frontend Production Build (`npm run build`)** | `tsc -b && vite build && node scripts/write-build-stamp.mjs` | **PASS** | Exit code `0` (`dist/assets/index-*.js` & `dist/build-stamp.json`) |
| **C# `CoreSmoke` Frontend Source Guards (15 suites)** | All structural, copy, role-lock, backup, notification, and scanner-first C# source guards | **PASS (`15 / 15`)** | Exit code `0` across all 15 `CoreSmoke --only` suites |
| **`MOCKED_UI` Playwright Suite (`npm run test:e2e`)** | 16 visual baseline specs (42 `toHaveScreenshot` comparisons with `updateSnapshots: "none"`) + 8 behavioral/defect verification specs | **PASS (`24 / 24`)** | `24 passed (41.1s)`, exit code `0` |
| **`INTEGRATED_HOST` Playwright Gate (`npm run test:integrated-host`)** | Unmocked live Host check on `http://127.0.0.1:5299` | **BLOCKED (`BLOCKED_NO_HOST`)** | Exit code `1` (`connect ECONNREFUSED 127.0.0.1:5299` — honestly reported, never faked) |

---

## 2. Wave-by-Wave Implementation Summary

### Wave A1 — Foundation, Design System & Wayfinding
- **Design Tokens & Shared Primitives ([`src/index.css`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/index.css), [`src/components/ui/`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/index.ts))**:
  - Defined semantic CSS variables (`--salryn-surface-*`, `--salryn-navy-*`, `--salryn-border-*`, `--salryn-focus-ring`) and `.tabular-nums` utility rules.
  - Upgraded [`Dialog.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/Dialog.tsx) with `Escape` key dismissal, body scroll lock, initial focus placement, and focus restoration on close (`UX-004`).
  - Added shared [`Select.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/Select.tsx) and enhanced [`DataTable.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/DataTable.tsx) with `align?: "left" | "right" | "center"`, `numeric?: boolean` (`tabular-nums`), and keyboard row activation (`Enter` / `Space`).
  - Repositioned [`Notifications.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/Notifications.tsx) to `top-16` (`64px`) so action toasts never obscure the `60px` top header bar (`Signed in as...` / `Sign out`).
- **Role-Aware Wayfinding, Responsive Drawer & Browser History ([`navigation.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/navigation.ts), [`AppShell.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/AppShell.tsx), [`RoleAwareAppShell.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/RoleAwareAppShell.tsx), [`App.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx))**:
  - Added 4-domain location metadata (`Counter & Sales`, `Finance & Analytics`, `Catalog & Stock`, `Store & System`) displayed in the top header and footer status bar, plus `aria-current="page"` on active navigation buttons.
  - Fixed `navIcons` mapping for `"LAN Terminals · Future Feature"` (`MonitorSmartphone`).
  - Fixed responsive layout on `< 768px` (`390×844` mobile) by hiding the collapsed sidebar (`max-md:hidden`) and providing a header menu button (`Open navigation menu`) that opens a slide-over backdrop drawer (`UX-006` / `UX-09`).
  - Synchronized `currentPage` with `?page=<id>` via `window.history.pushState` and `popstate` listener in [`App.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx) (`UX-008` / `UX-06`), and added `sessionQuery.refetch()` to the startup error `"Try again"` handler (`UX-05`).

### Wave A2 — Counter Operations (`CheckoutPage`, `SalesHistoryPage`) & W4 Compatibility
- **`CheckoutPage.tsx` Counter Workstation Refactor ([`CheckoutPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx))**:
  - **Fixed P0 `UX-001` (Full Utang False `"Insufficient cash"` Warning)**: Selecting `Utang` automatically sets `cashInput` to `"0.00"`, disables the `Cash tendered` input and quick-tender buttons, and transforms the settlement readout card from red `"Insufficient cash"` (`Change due`) into an amber `"Utang credit balance"` card showing `"Select customer"` or `"Credit ready"`.
  - **Fixed P0 `UX-002` & P1 `UX-003` (Unified Settlement Hierarchy & Accessible Labels)**: Associated `<label htmlFor="checkout-payment-mode">`, `<label htmlFor="checkout-customer-select">`, `<label htmlFor="checkout-due-date">`, `<label htmlFor="checkout-credit-note">`, and `<label htmlFor="checkout-cash-tendered">`, and added a live settlement mode badge (`Cash sale`, `Full Utang credit`, `Cash + Utang split`) and contextual Utang customer/due-date summary inside the right-hand `Payment / checkout` panel.
  - **Fixed P1 `UX-04` (`Sale Completed` Status Pill)**: Displays `"Cash sale committed"`, `"Utang sale committed"`, or `"Split sale committed"` based on `completedSale.paymentMode` while preserving the exact C# source-guard order (`"Cash sale committed"` before `"Support details"`).
  - **Authored W4 Compatibility Matrix ([`00-HANDOFF/SALRYN_W4_CHECKOUT_UI_COMPATIBILITY_MATRIX.md`](file:///C:/Salryn-Antigravity/00-HANDOFF/SALRYN_W4_CHECKOUT_UI_COMPATIBILITY_MATRIX.md))**: Inspected read-only PR #22 (`codex/salryn-w4-drawer-reconciliation-r1`) and preserved clean hunk boundaries around shift state, header banner, and bottom dialogs so W4's Open/Close Shift drawer reconciliation modals merge cleanly.
- **`SalesHistoryPage.tsx` Polish ([`SalesHistoryPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx))**:
  - Replaced the custom inline modal with the shared accessible `UiDialog` (`Escape` support), added monospace tabular receipt numbers, and bound `<label htmlFor>` on date/search/void inputs.

### Wave A3 — Finance, Catalog & Stock (`OwnerReportsPage`, `ReceivablesPage`, `ProductsPage`, `InventoryPage`)
- **`OwnerReportsPage.tsx` ([`OwnerReportsPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx))**:
  - Bound `<label htmlFor="owner-reports-period-preset">`, `<label htmlFor="owner-reports-from-date">`, and `<label htmlFor="owner-reports-to-date">` (`UX-003`), applied `tabular-nums` to all KPI and table amounts, and preserved `max-h-80 overflow-y-auto` on tabular chart data.
- **`ReceivablesPage.tsx` ([`ReceivablesPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx))**:
  - Added `<StatusPill tone="info">Signed U1 · Accounts Receivable</StatusPill>`, right-aligned tabular ledger columns (`Amount`, `Balance after`), and added accessible `id`/`aria-label` bindings to customer creation, payment collection, and adjustment inputs.
- **`ProductsPage.tsx` & `InventoryPage.tsx` ([`ProductsPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ProductsPage.tsx), [`InventoryPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/InventoryPage.tsx))**:
  - **Fixed P1 `UX-004` (`Escape` Key on Modals)**: Replaced custom non-Escape `Dialog` definitions in both `ProductsPage.tsx` and `InventoryPage.tsx` with the shared `UiDialog`.
  - **Fixed P1 `UX-005` (`"View Movements"` Cross-Module Navigation)**: Clicking `"View Movements"` on a selected product in `ProductsPage` now navigates directly to `InventoryPage` (`/?page=inventory&productId=<id>`) with an active product filter pill (`Filtered: <productName> (<sku>)`) and `"Clear Product Filter"` action.

### Wave A4 — Security, Admin, Recovery & Edge States (`BackupRestorePage`, `AuditPage`, `StoreSettingsPage`)
- **`BackupRestorePage.tsx` ([`BackupRestorePage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/BackupRestorePage.tsx))**:
  - **Fixed P2 `UX-010` (Triple-Confirmation Modal Redundancy)**: Removed the 3 redundant post-action modal OK popups (`createSuccessId`, `verifySuccessId`, `restoreSuccessAt`) so creating or verifying a backup immediately updates the package status pill (`Verified`), inline `<Alert tone="success">`, and action toast without blocking the operator behind an extra modal click before `"Restore Backup"`. Retained the mandatory destructive `"Confirm restore"` dialog requiring typed `RESTORE SALRYN BACKUP`.
- **`AuditPage.tsx` & `StoreSettingsPage.tsx` ([`AuditPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/AuditPage.tsx), [`StoreSettingsPage.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx))**:
  - Added `role="tablist"` / `role="tab"` / `aria-selected` to `Audit logs` and `Error logs` toggle buttons (`UX-18`).
  - Added `queryClient.invalidateQueries({ queryKey: ["app-shell-store-branding"] })` on store settings save, logo upload, and logo removal so the `AppShell` header branding updates immediately (`UX-16`).

---

## 3. Before / After Defect Resolution Register (`UX-001` – `UX-010`)

| Defect ID | Severity | Defect Summary | Before (A0 Baseline) | After (Wave A1–A4 Implementation) | Playwright Verification Spec |
| :--- | :---: | :--- | :--- | :--- | :--- |
| **`UX-001`** | **P0** | Full `Utang` mode leaves `Cash tendered` enabled and shows red `"Insufficient cash"` | `11-checkout-utang-mode.png`: Red `"Insufficient cash"` inside `Change due` box when `Utang` is selected | `11-checkout-utang-mode.png`: `Cash tendered` disabled (`0.00`); amber `"Utang credit balance"` card with zero `"Insufficient cash"` warning | `UX-001 & UX-002 FIXED` (`PASS`) |
| **`UX-002`** | **P0** | Payment mode & settlement feedback split across disconnected regions | Top payment bar disconnected from right-hand `Cash sale` card | Right-hand `Payment / checkout` panel shows live mode badge (`Cash sale` / `Full Utang credit` / `Cash + Utang split`) and inline customer/credit summary | `UX-001 & UX-002 FIXED` (`PASS`) |
| **`UX-003`** | **P1** | Form `<label>` elements lack `htmlFor` / `id` bindings | `getByLabel(...)` failed on Checkout and Owner Reports filter controls | Explicit `htmlFor` + `id` bindings across `CheckoutPage`, `OwnerReportsPage`, `SalesHistoryPage`, and `ReceivablesPage` | `UX-001 & UX-002 FIXED` (`PASS`) |
| **`UX-004`** | **P1** | Custom `Dialog` in `ProductsPage` & `InventoryPage` ignores `Escape` key | Pressing `Escape` left `Create Product` and `Stock-In` modals open | Unified onto shared `UiDialog`; pressing `Escape` cleanly closes modals and restores focus | `UX-004 FIXED` (`PASS`) |
| **`UX-005`** | **P1** | `ProductsPage` `"View Movements"` button is an in-page dead-end | Clicking `"View Movements"` stayed on `ProductsPage` and showed an info alert | Navigates directly to `Stock Movements` (`/?page=inventory&productId=...`) filtered by the selected product | `UX-005 FIXED` (`PASS`) |
| **`UX-006`** | **P2** | Fixed `w-60` sidebar crushes `< 768px` mobile viewports (`390×844`) | `35-responsive-390x844-mobile-owner-reports.png`: `240px` sidebar left only `150px` for content | Collapsed sidebar hides on `< 768px` (`max-md:hidden`), giving `<main>` full `390px` width with a header slide-over drawer toggle | `35-responsive-390x844-mobile-owner-reports.png` (`PASS`) |
| **`UX-008`** | **P2** | Sidebar navigation does not update URL or browser history | Clicking sidebar links left URL at `/` and did not support browser `Back` | Sidebar navigation syncs `?page=<id>` via `pushState` and restores page state on `popstate` (`page.goBack()`) | `UX-008 FIXED` (`PASS`) |
| **`UX-010`** | **P2** | `BackupRestorePage` fires toast + inline alert + blocking modal OK dialog | `26-backup-restore-verified.png`: Operator had to dismiss `"Backup verified"` modal before clicking `"Restore Backup"` | `26-backup-restore-verified.png`: Inline banner + toast + `"Verified"` pill enable `"Restore Backup"` immediately without a blocking OK dialog | `UX-010 FIXED` (`PASS`) |
