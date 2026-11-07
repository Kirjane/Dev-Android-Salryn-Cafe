# Salryn POS — Prioritized Frontend UX & Architecture Defect Register (A0)

- **Document ID**: `SALRYN_FRONTEND_UX_FINDINGS_PRIORITIZED`
- **Phase**: Antigravity A0 — Whole-System Frontend Audit, UX Architecture & Playwright Visual Baseline
- **Target Application**: Base **Salryn POS** Retail Application (Host Console + LAN Terminal Client + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics; **excludes Salryn Café Yza**)
- **Worktree / Branch**: `C:\Salryn-Antigravity` (`antigravity/salryn-premium-pos-ux`)
- **Severity Scale**:
  - **P0 — Financial Trust / Counter Blocking**: Causes cashier checkout dead-ends, misleading financial/payment state, or masks operational errors on financial ledgers.
  - **P1 — High-Friction Operational / Navigation / Responsive Defect**: Breaks cross-module workflows, keyboard ergonomics (`Escape`), browser history, or layout usability on target viewports.
  - **P2 — Design System / Consistency / Governance Gap**: Component duplication, stale UI cache after mutation, raw unstyled HTML tables, or missing ARIA semantics.
  - **P3 — Polish / Ergonomic Enhancement**: Minor visual or copy improvements.

---

## 1. Summary Defect Matrix (20 Scored Findings)

| ID | Severity | Module / Screen | Finding Summary | Source Location | Visual / Test Evidence | Cost | Risk | Target Wave (Issue #20) |
| :--- | :---: | :--- | :--- | :--- | :--- | :---: | :---: | :---: |
| **UX-01** | **P0** | `CheckoutPage` | Full Utang mode leaves `Cash tendered` editable and shows false red `"Insufficient cash"` in Change Due box | [`CheckoutPage.tsx:177`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L177), [`CheckoutPage.tsx:450-479`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L450-L479) | `11-checkout-utang-mode.png` | S | Low | Wave 2 |
| **UX-02** | **P0** | `CheckoutPage` | Payment Mode (`Cash`/`Utang`/`Cash + Utang`) & Customer selector are isolated in a top banner card far from the right-hand Tender column | [`CheckoutPage.tsx:337-357`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L337-L357), [`CheckoutPage.tsx:435-498`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L435-L498) | `11-checkout-utang-mode.png` | M | Low | Wave 2 |
| **UX-03** | **P0** | `ReceivablesPage` | All API/validation errors in Utang Ledger are rendered as blue `tone="info"` alerts with no toast feedback | [`ReceivablesPage.tsx:31`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx#L31), [`ReceivablesPage.tsx:81,102,128,143`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx#L143) | `16-receivables-utang-ledger.png` | S | Low | Wave 3 |
| **UX-04** | **P1** | `CheckoutPage` | `Sale Completed` modal hardcodes `<StatusPill tone="success">Cash sale committed</StatusPill>` even for `Utang` and `Cash + Utang` credit sales | [`CheckoutPage.tsx:369`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L369) | `CheckoutPage.tsx#L369` | XS | Low | Wave 2 |
| **UX-05** | **P1** | `App.tsx` (Startup Gate) | `"Salryn POS unavailable"` retry button (`"Try again"`) omits `sessionQuery.refetch()`, leaving transient session errors unrecoverable without `F5` | [`App.tsx:166`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L166) | `01-startup-unavailable.png` | XS | Low | Wave 1 |
| **UX-06** | **P1** | `App.tsx` & `AppShell.tsx` | Sidebar navigation (`setCurrentPage`) never pushes browser history or updates URL; browser Back button breaks or jumps to stale drilldown state | [`App.tsx:140-163`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L140-L163), [`AppShell.tsx:146-152`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/AppShell.tsx#L146-L152) | `mocked-ui-interactions-and-defects.spec.ts` | M | Low | Wave 1 |
| **UX-07** | **P1** | `ProductsPage` & `InventoryPage` | Clicking `"View Movements"` on a product does not navigate to `Stock Movements` (only shows a text banner), and `InventoryPage` lacks a Product filter | [`ProductsPage.tsx:550-554`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ProductsPage.tsx#L550-L554), [`InventoryPage.tsx:251-298`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/InventoryPage.tsx#L251-L298) | `19-products-catalog-detail.png`, `21-inventory-movements.png` | M | Low | Wave 4 |
| **UX-08** | **P1** | `Dialog.tsx` & 5 Pages | 6 duplicate modal implementations; `ui/Dialog.tsx`, `ProductsPage`, `InventoryPage`, `LicensePage`, and `TerminalActivationPage` modals ignore `Escape` key | [`Dialog.tsx:20-71`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/Dialog.tsx#L20-L71), [`ProductsPage.tsx:135-166`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ProductsPage.tsx#L135-L166) | `20-products-create-modal.png`, `22-inventory-stockin-modal.png` | M | Low | Wave 1 & 4 |
| **UX-09** | **P1** | `AppShell.tsx` & `CheckoutPage` | Responsive failure on `< 1280px` / `< 1024px` / `390×844` / `125%` zoom: fixed `w-60` sidebar crushes mobile viewports; Checkout 3-column grid collapses into long vertical stack | [`AppShell.tsx:128`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/AppShell.tsx#L128), [`CheckoutPage.tsx:358`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L358) | `32-responsive-1024x768-checkout.png`, `35-responsive-390x844-mobile-owner-reports.png`, `36-scale-125pct-checkout.png` | M | Low | Wave 1 & 2 |
| **UX-10** | **P1** | `ReceivablesPage` | Destructive `"Reverse Collection"` action executes immediately on button click without a confirmation modal | [`ReceivablesPage.tsx:194-215`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx#L194-L215) | `16-receivables-utang-ledger.png` | S | Low | Wave 3 |
| **UX-11** | **P1** | `CheckoutPage` | `"Close Shift"` button executes immediately on single click with no confirmation modal or shift cash summary | [`CheckoutPage.tsx:332`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L332) | `10-checkout-cash-cart.png` | S | Low | Wave 2 |
| **UX-12** | **P1** | `SalesHistoryPage` | Sales table omits `Payment Method` / Utang indicator column; clicking `"Reset"` fails to clear `?salesFrom=&salesTo=` URL query string | [`SalesHistoryPage.tsx:19-24`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx#L19-L24), [`SalesHistoryPage.tsx:124-134,242-251`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx#L124-L134) | `14-sales-history-list.png` | S | Low | Wave 2 |
| **UX-13** | **P1** | `OwnerReportsPage` & `ReceivablesPage` | Clicking an AR Aging bucket on `OwnerReportsPage` pushes `?arBucket=...` to URL, but `ReceivablesPage` ignores the query parameter | [`App.tsx:191-195`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L191-L195), [`OwnerReportsPage.tsx:242-250`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx#L242-L250) | `17-owner-reports-analytics.png` | S | Low | Wave 3 |
| **UX-14** | **P2** | `ReceivablesPage` & `OwnerReportsPage` | Customer credit ledger and Owner Reports operational tables use raw unstyled `<table className="w-full text-xs">` instead of `DataTable`, and lack `EmptyState`s | [`ReceivablesPage.tsx:169-203`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx#L169-L203), [`OwnerReportsPage.tsx:28,284,300,317`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx#L28) | `16-receivables-utang-ledger.png`, `17-owner-reports-analytics.png` | M | Low | Wave 3 |
| **UX-15** | **P2** | `AppShell.tsx` & `navigation.ts` | `navIcons` keys off display label `"Terminal Management"` while `navigation.ts` uses `"LAN Terminals · Future Feature"`, causing wrong fallback icon (`FileText`) | [`AppShell.tsx:39,141`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/AppShell.tsx#L39), [`navigation.ts:28`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/navigation.ts#L28) | `27-lan-terminals-future-feature.png` | XS | Low | Wave 1 |
| **UX-16** | **P2** | `StoreSettingsPage` | Uploading or removing Store Logo does not invalidate `["app-shell-store-branding"]` query; header logo remains stale until page reload | [`StoreSettingsPage.tsx:69-90`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx#L69-L90), [`AppShell.tsx:52-56`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/AppShell.tsx#L52-L56) | `25-store-settings.png` | XS | Low | Wave 4 |
| **UX-17** | **P2** | `UsersPage` | Permanently expanded 6-field `Create User Account` card pushes the User Directory table below the fold | [`UsersPage.tsx:138-252`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/UsersPage.tsx#L138-L252) | `23-users-roles-permissions.png` | S | Low | Wave 4 |
| **UX-18** | **P2** | `AuditPage` | `Audit Logs` vs `Error Logs` toggle buttons lack `role="tablist"` / `role="tab"` / `aria-selected` accessibility attributes | [`AuditPage.tsx:161-180`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/AuditPage.tsx#L161-L180) | `24-audit-logs.png` | XS | Low | Wave 4 |
| **UX-19** | **P2** | `navigation.ts` & `App.tsx` | `OfficialSetupPage` (`official-setup`) and `TraditionalReportingPage` (`traditional-reporting`) are in `implementedPages` but absent from `internalNavItems` | [`navigation.ts:17-37`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/navigation.ts#L17-L37), [`App.tsx:100-105`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L100-L105) | `38-internal-official-setup.png`, `39-internal-traditional-reporting.png` | XS | Low | Wave 1 |
| **UX-20** | **P3** | `BootstrapSetupPage` | First-admin password inputs use `<TextInput type="password" />` instead of `<PasswordInput />` (missing Show/Hide toggle) | [`BootstrapSetupPage.tsx:249-277`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/BootstrapSetupPage.tsx#L249-L277) | `04-bootstrap-setup.png` | XS | Low | Wave 4 |

---

## 2. Detailed Specifications & Acceptance Criteria for Top P0 / P1 Defects

### UX-01 (P0): Checkout Full Utang Cash-Tendered Trap & False "Insufficient Cash" Display
- **Observed Behavior**:
  - In [`CheckoutPage.tsx:450-479`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L450-L479), when the cashier selects `Payment mode: Utang`, the `Cash tendered (PHP)` input remains enabled and the `Change due` box evaluates `cashCents >= subtotalCents`. Because `cashCents` is `0` (or empty) for a full credit sale, the box displays **"Insufficient cash"** in red (`text-rose-600`). Worse, if the cashier had typed a cash amount before switching to `Utang`, [`validateCheckoutRequest`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L177) blocks submission with `"Full Utang requires cash received to be zero."`
- **Recommended Fix**:
  - When `paymentMode` changes to `"Utang"`, automatically set `cashInput` to `"0.00"` and disable/hide the `Cash tendered` field.
  - Replace the static `Change due` box with a payment-mode-aware summary box:
    - `Cash`: Shows **Change due** (`cashCents - subtotalCents`) or `"Insufficient cash"` if `cashCents < subtotalCents`.
    - `Utang`: Shows **Charged to Utang** (`subtotalCents`) in amber (`text-amber-800 bg-amber-50`).
    - `Cash + Utang`: Shows both **Cash paid** (`cashCents`) and **Remaining to Utang** (`subtotalCents - cashCents`) in amber.
- **Measurable Acceptance Criteria**:
  - Selecting `Utang` in `CheckoutPage` sets cash tendered to `0`, disables the cash input, displays `Charged to Utang: ₱...` with zero red `"Insufficient cash"` text, and enables `Review Checkout (F9)` as soon as a customer is selected.

### UX-02 (P0): Checkout Split Payment Architecture (Top Banner vs Right Tender Column)
- **Observed Behavior**:
  - [`CheckoutPage.tsx:337-357`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L337-L357) renders `Payment mode` (`Cash`, `Utang`, `Cash + Utang`), `Customer`, `Due date`, and `Credit note` in a full-width card above the 3-column grid, while `Cash tendered` and `Review Checkout (F9)` live in the right-hand column (`CheckoutPage.tsx:435-498`). Cashiers must scan their eyes and mouse from the bottom-right total up to the top-left banner and back down to the bottom-right button.
- **Recommended Fix**:
  - Move `Payment mode` (as a 3-button segmented control: `Cash` | `Utang` | `Cash + Utang`), `Customer` selector (+ inline Quick New Customer), `Due date`, and `Credit note` directly into the right-hand `Payment / checkout` card above `Cash tendered`.
- **Measurable Acceptance Criteria**:
  - 100% of payment tender controls (`Payment mode`, `Customer`, `Cash tendered`, `Change / Utang summary`, `Review Checkout (F9)`) reside inside a single contiguous right-hand panel.

### UX-03 (P0): Utang Ledger (`ReceivablesPage`) Masks Errors as Informational Alerts
- **Observed Behavior**:
  - [`ReceivablesPage.tsx:143`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/ReceivablesPage.tsx#L143) renders `<Alert tone="info">{message}</Alert>` for all outcomes, including catch blocks at `lines 81, 102, 128`.
- **Recommended Fix**:
  - Integrate `useNotifications()` (`notify.success`, `notify.error`) for customer creation, payment collection, and collection reversal, and use `<Alert tone="danger">` for inline form errors.
- **Measurable Acceptance Criteria**:
  - Failed API mutations on `ReceivablesPage` trigger `notify.error` (`role="alert"`) and render `tone="danger"` (rose) styling.

### UX-04 (P1): `Sale Completed` Modal Hardcodes `"Cash sale committed"` for Credit Sales
- **Observed Behavior**:
  - [`CheckoutPage.tsx:369`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/pages/CheckoutPage.tsx#L369) always renders `<StatusPill tone="success">Cash sale committed</StatusPill>`.
- **Recommended Fix**:
  - Track the committed sale's payment mode (`lastCompletedPaymentMode`) and render `"Cash sale committed"`, `"Utang credit sale committed"`, or `"Split Cash + Utang sale committed"`, along with the charged Utang amount when applicable.
- **Measurable Acceptance Criteria**:
  - Completing an `Utang` sale displays `"Utang credit sale committed"` and the credit amount charged.

### UX-05 (P1): Startup Unavailable `"Try again"` Button Omits `sessionQuery.refetch()`
- **Observed Behavior**:
  - [`App.tsx:166`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L166) calls `refreshBootstrapStatus()` and `licenseStatusQuery.refetch()`, but not `sessionQuery.refetch()`.
- **Recommended Fix**:
  - Add `void sessionQuery.refetch();` to the `onRetry` callback in `App.tsx:166`.
- **Measurable Acceptance Criteria**:
  - When `/api/session` fails once and then recovers, clicking `"Try again"` transitions out of `"Salryn POS unavailable"` without requiring a page reload.

### UX-06, UX-07, UX-08, UX-09 (P1): Navigation History, Cross-Module Product->Inventory Link, Unified `Escape` Modals & Responsive Shell
- **UX-06 Acceptance Criteria**: Clicking any sidebar button calls `window.history.pushState({ page }, "", url)` so browser `Back` returns to the previous screen.
- **UX-07 Acceptance Criteria**: Clicking `"View Movements"` in `ProductsPage` navigates to `InventoryPage` (`Stock Movements`) with that product pre-selected in a new Product filter dropdown on `InventoryPage`.
- **UX-08 Acceptance Criteria**: Pressing `Escape` closes any open modal across `Dialog.tsx`, `ProductsPage`, `InventoryPage`, `LicensePage`, and `TerminalActivationPage`.
- **UX-09 Acceptance Criteria**: At `< 1024px` (`390×844` mobile and compact tablets), `AppShell` collapses the `w-60` sidebar into a toggleable drawer so `<main>` receives full viewport width.
