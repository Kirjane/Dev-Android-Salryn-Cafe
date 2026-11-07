# Salryn POS — Persona, Navigation & Information Architecture Blueprint (A0)

- **Document ID**: `SALRYN_FRONTEND_PERSONA_NAVIGATION_INFORMATION_ARCHITECTURE`
- **Phase**: Antigravity A0 — Whole-System Frontend Audit, UX Architecture & Playwright Visual Baseline
- **Target Application**: Base **Salryn POS** Retail Application (Host Console + LAN Terminal Client + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics; **excludes Salryn Café Yza**)
- **Worktree / Branch**: `C:\Salryn-Antigravity` (`antigravity/salryn-premium-pos-ux`)
- **Design Principle**: *"Users should immediately recognize where they are, what information matters, and what action to take. Prioritize operational speed, legibility, consistency, clarity and financial trust."*

---

## 1. Entry, Licensing, Onboarding & Session State Machine

The Salryn POS frontend operates two distinct runtime entry trees determined at boot by [`isRemoteClientBrowserLocation()`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/utils/lanEntry.ts#L12-L27) in [`App.tsx:24-26`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/App.tsx#L24-L26):

```mermaid
stateDiagram-v2
    [*] --> HostnameCheck: Browser Loads App
    HostnameCheck --> HostStartupGates: Loopback (localhost / 127.0.0.1 / ::1)
    HostnameCheck --> TerminalLicenseGate: Non-Loopback (LAN IP / hostname)

    state "Host Console Entry (ConsoleApp)" as HostStartupGates {
        [*] --> BootQueries: Fetch Bootstrap + Session + License
        BootQueries --> HostUnavailable: Any Query Error
        HostUnavailable --> BootQueries: Click "Try again" (Fix: must also refetch Session!)
        BootQueries --> HostLicenseGate: !isHostLicenseReady(license)
        HostLicenseGate --> FirstRunBootstrap: Valid Host License Installed
        BootQueries --> FirstRunBootstrap: License Ready && firstRunRequired === true
        FirstRunBootstrap --> HostLogin: First Admin + Store Created
        BootQueries --> HostLogin: License Ready && !firstRunRequired && !isAuthenticated
        HostLogin --> RoleLandingRedirect: Valid Credentials
        BootQueries --> RoleLandingRedirect: Authenticated Session Restored
        RoleLandingRedirect --> CashierWorkspace: Role == Cashier -> "checkout"
        RoleLandingRedirect --> ManagerOwnerWorkspace: Role == Admin | Manager -> "dashboard"
    }

    state "LAN Terminal Entry (RemoteClientApp)" as TerminalLicenseGate {
        [*] --> CheckLocalStorageLicense: Read salryn.terminal.installedLicense.v1
        CheckLocalStorageLicense --> TerminalActivationPage: Missing / Machine Mismatch
        TerminalActivationPage --> RemoteClientLandingPage: Validated via Host & Saved
        CheckLocalStorageLicense --> RemoteClientLandingPage: Valid Terminal License Present
        RemoteClientLandingPage --> RemotePosClientPage: Click "Open Terminal POS"
        RemoteClientLandingPage --> TerminalActivationPage: Click "Change license"
        RemotePosClientPage --> TerminalPairingFlow: Not Paired / Trust Expired
        TerminalPairingFlow --> TerminalOperatorLogin: Host Approves Pairing Code
        TerminalOperatorLogin --> TerminalPosWorkstation: Operator Authenticated (TerminalShell)
    }
```

### 1.1 Audited Entry & Session State Machine Gaps & Target Architecture Fixes
1. **Startup Error Recovery (`App.tsx:166`)**:
   - **Current Bug**: Clicking `"Try again"` on the `"Salryn POS unavailable"` screen calls `refreshBootstrapStatus()` and `licenseStatusQuery.refetch()`, omitting `sessionQuery.refetch()`.
   - **Target Fix**: Invoke all three (`refreshBootstrapStatus()`, `licenseStatusQuery.refetch()`, `sessionQuery.refetch()`) and display which specific check failed (Bootstrap vs Session vs License) with a local diagnostic code.
2. **URL & History Synchronization (`App.tsx:140-163`)**:
   - **Current Bug**: Sidebar navigation only mutates React state (`setCurrentPage(page)`). Browser Back/Forward does not work for normal navigation, and URL query strings (`?salesFrom=...&salesTo=...` or `?arBucket=...`) linger after leaving `SalesHistoryPage` or `ReceivablesPage`.
   - **Target Fix**: Centralize navigation in a `navigateToPage(page: AppPage, params?: Record<string, string>)` helper inside `ConsoleApp` that consistently calls `window.history.pushState({ page, params }, "", url)` (and clears stale query parameters when navigating away or resetting filters).

---

## 2. Persona, Role & License Entitlement Matrix

Access control in Salryn POS is governed by a three-layer check in [`src/security/permissions.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/security/permissions.ts#L1-L130):
1. **Role Allowlist (`RoleIds`)**:
   - `Admin` (`00000000-0000-0000-0000-000000000001`)
   - `Manager` (`00000000-0000-0000-0000-000000000002`): Restricted to `managerAllowedPages` (`dashboard`, `checkout`, `products`, `inventory`, `sales`, `receivables`, `owner-reports`, `audit`).
   - `Cashier` (`00000000-0000-0000-0000-000000000003`): Restricted to `cashierAllowedPages` (`checkout`, `sales`).
2. **Granular Permission String (`pagePermissions` & Action Guards)**: Checked via `hasPermission(session, permission)`.
3. **Signed License Feature Entitlement (`enabledFeatures`)**: Specifically `ReceivablesUtangLedger` (U1 Accounts Receivable / Utang module).

### 2.1 Complete Page & Action Access Matrix

| Module / Action | Page ID (`AppPage`) | Required Permission | License Entitlement | Cashier (Host) | Manager (Host) | Admin / Owner (Host) | LAN Terminal Cashier |
| :--- | :--- | :--- | :--- | :---: | :---: | :---: | :---: |
| **Dashboard Overview** | `dashboard` | `Dashboard.View` | Base Host | Denied (Auto-redirects to `checkout`) | Allowed | Allowed | Hidden (Uses `TerminalShell`) |
| **Checkout — Cash Sale** | `checkout` | `Sales.Create` | Base Host | Allowed | Allowed | Allowed | Allowed (`RemotePosClientPage`) |
| **Checkout — Utang / Split Credit Sale** | `checkout` | `Sales.Create` + `Receivables.CreateCreditSale` | `ReceivablesUtangLedger` (U1) | Allowed (if U1 + perm) | Allowed (if U1 + perm) | Allowed (if U1) | Not supported on Terminal POS |
| **Checkout — Fast Add Product** | `checkout` | `Products.FastAdd` (or `Products.Manage`) | Base Host | Allowed (if granted) | Allowed | Allowed | Not on Terminal POS |
| **Sales & Receipts — View & Filter** | `sales` | `Sales.View` | Base Host | Allowed | Allowed | Allowed | Not on Terminal POS |
| **Sales & Receipts — Reprint Receipt** | `sales` | `Receipts.Reprint` | Base Host | Allowed | Allowed | Allowed | Immediate post-sale only |
| **Sales & Receipts — Void Sale** | `sales` | `Sales.Void` | Base Host | Denied (Hidden) | Allowed | Allowed | Denied |
| **Utang Ledger — View Customers & Ledger** | `receivables` | `Receivables.View` | `ReceivablesUtangLedger` (U1) | Denied | Allowed (if U1) | Allowed (if U1) | Denied |
| **Utang Ledger — Create Customer** | `receivables` | `Receivables.ManageCustomers` | `ReceivablesUtangLedger` (U1) | Denied (Except Quick Add in Checkout if `CreateCreditSale`) | Allowed (if U1) | Allowed (if U1) | Denied |
| **Utang Ledger — Collect Payment** | `receivables` | `Receivables.CollectPayment` | `ReceivablesUtangLedger` (U1) | Denied | Allowed (if U1) | Allowed (if U1) | Denied |
| **Utang Ledger — Reverse Collection** | `receivables` | `Receivables.VoidCollection` | `ReceivablesUtangLedger` (U1) | Denied | Allowed (if U1) | Allowed (if U1) | Denied |
| **Owner Reports & Financial Analytics** | `owner-reports` | `Reports.View` | Base Host (AR section requires U1 + `Receivables.View`) | Denied | Allowed | Allowed | Denied |
| **Products — View Catalog** | `products` | `Products.View` | Base Host | Denied (Blocked by `cashierAllowedPages`) | Allowed | Allowed | Read-only search in POS |
| **Products — Create / Edit / Deactivate** | `products` | `Products.Manage` | Base Host | Denied | Allowed | Allowed | Denied |
| **Stock Movements — View Low Stock & Ledger** | `inventory` | `Inventory.View` | Base Host | Denied | Allowed | Allowed | Denied |
| **Stock Movements — Record Stock-In** | `inventory` | `Inventory.Manage` | Base Host | Denied | Allowed | Allowed | Denied |
| **Stock Movements — Record Adjustment** | `inventory` | `Inventory.Adjust` | Base Host | Denied | Allowed | Allowed | Denied |
| **Users, Roles & Permissions** | `users` | `Users.Manage` | Base Host | Denied | Denied (Blocked by `managerAllowedPages`) | Allowed | Denied |
| **Audit & Error Logs (+ CSV Export)** | `audit` | `Audit.View` | Base Host | Denied | Allowed | Allowed | Denied |
| **Store Settings & Printer Config** | `settings` | `Settings.Manage` | Base Host | Denied | Denied | Allowed | Denied |
| **Backup & Restore** | `backup` | `Backup.Manage` | Base Host | Denied | Denied | Allowed | Denied |
| **LAN Terminals Control** | `host-lan-control` | `Settings.Manage` | Base Host | Denied | Denied | Allowed | Denied |
| **Host License Management** | `license` | `Settings.Manage` | Base Host | Denied | Denied | Allowed | Uses `TerminalActivationPage` |

---

## 3. Navigation Taxonomy & Wayfinding Architecture

### 3.1 Current Sidebar Taxonomy vs Target Taxonomy

Currently, [`navigation.ts:17-30`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/navigation.ts#L17-L30) groups 12 Host pages into 4 sidebar sections (`Operations`, `Catalog`, `Reporting`, `System`):

| Current Group | Current Items (`navigation.ts`) | Identified Wayfinding Issues | Target Architecture Improvement (Issue #20) |
| :--- | :--- | :--- | :--- |
| **Operations** | `Dashboard`, `Checkout`, `Sales & Receipts`, `Utang Ledger` | Strong grouping for daily counter and receivable operations. However, when U1 is not entitled, `Utang Ledger` silently disappears with no discoverability cue for owners wondering where credit tracking lives. | Keep `Dashboard`, `Checkout`, `Sales & Receipts`, `Utang Ledger` in **Operations**. Add keyboard shortcut badge (`Alt+1`..`Alt+4`) or quick-switch indicator on `Checkout`. |
| **Catalog** | `Products`, `Stock Movements` | Good pairing, but cross-navigation between `Products` (`"View Movements"`) and `Stock Movements` is broken (`ProductsPage.tsx:550`). | Rename group to **Catalog & Inventory**. Pass `selectedProductId` directly from `ProductsPage` -> `InventoryPage` so `"View Movements"` opens `Stock Movements` pre-filtered to that SKU. |
| **Reporting** | `Owner Reports` | Single-item group in standard builds (`Report Artifacts` only appears when `VITE_SALRYN_INTERNAL_SURFACES === "true"`). Meanwhile `TraditionalReportingPage` (`traditional-reporting`) is orphaned with no nav item even in internal builds! | Rename item to **Financial & Owner Reports**. In internal builds (`VITE_SALRYN_INTERNAL_SURFACES === "true"`), include `Traditional Reporting` and `Official Setup` under an explicit **Internal Engineering** section so no `implementedPages` are orphaned. |
| **System** | `Users & Roles`, `Audit Logs`, `Store Settings`, `Backup & Restore`, `LAN Terminals · Future Feature`, `License` | 6 items in `System`, with `LAN Terminals · Future Feature` wrapping onto two lines and displaying the wrong fallback icon (`FileText` instead of `Network` in `AppShell.tsx:39`). | Fix `navIcons` in `AppShell.tsx` to key off `item.page` (`Record<AppPage, LucideIcon>`) instead of fragile display strings (`item.label`)! Shorten label to `LAN Terminals` with a compact `Soon` pill badge. |

### 3.2 Wayfinding Contract: Active Location, Page Header & Context Bar

Every authenticated screen must immediately answer three questions within 500ms of glance:
1. **Where am I?**
   - Left sidebar active button (`aria-current="page"`, high-contrast blue indicator bar + `bg-blue-600 text-white`).
   - Top `AppShell` context bar (`AppShell.tsx:83-124`) showing Store Logo/Name, active workstation mode (`Host PC` vs `Terminal POS`), Shift state pill (`Shift Open · ₱3,000 float` vs `Shift Closed`), and signed-in Operator + Role badge.
   - Standardized Page Header (`CommandBar` / page title block) with consistent breadcrumb or parent section label, `h1` title, 1-line operational purpose subtitle, and right-aligned primary/secondary action buttons.
2. **What information matters right now?**
   - KPI summary strip at the top of analytical/ledger pages (`Dashboard`, `Utang Ledger`, `Owner Reports`) using tabular numerals (`font-variant-numeric: tabular-nums`) and clear scope labels (`Today`, `Selected Range`, `As of Now`).
   - Active filter pills showing when a table is filtered by a cross-page drilldown (e.g. `Filtered by Date: 2026-04-19 ×` on `Sales & Receipts`, or `Filtered by Product: Sinandomeng Rice 5kg ×` on `Stock Movements`).
3. **What action should I take next?**
   - Exactly **one** primary (`variant="primary"`) CTA per screen region or modal (e.g., `Review Checkout (F9)` in Checkout, `Create Product` in Products, `Record Stock-In` in Inventory, `Save Store Settings` in Settings).

---

## 4. Core Operational Loops (`Content -> Decision -> Action -> Feedback -> Record`)

### Loop 1: Cashier Checkout & Tender Loop (`CheckoutPage.tsx`)
- **Content**: Shift status bar + Product search/barcode input + Cart line table + Unified Payment & Tender panel (`Cash` / `Utang` / `Cash + Utang`).
- **Decision**: Verify items, quantities, subtotal, payment mode, customer credit availability (if Utang), and cash tendered / change due (or credit balance to charge).
- **Action**: Press `F9` or click `Review Checkout` -> press `Enter` or click `Complete Sale` inside confirmation modal.
- **Feedback**: `Sale Completed` modal with accurate payment badge (`Cash sale committed`, `Utang credit sale committed`, or `Split Cash + Utang sale committed`), large tabular Receipt Number, Change Due (for cash) or Charged to Utang amount, and immediate `Print Receipt` / `Start New Sale` action.
- **Record**: Sale appears immediately in `Sales & Receipts`, decrements `Products` / `Stock Movements`, and (if Utang) posts a `CreditSale` debit entry in `Utang Ledger` and updates `Owner Reports`.

### Loop 2: Utang Origination, Aging & Collection Loop (`CheckoutPage` -> `OwnerReportsPage` -> `ReceivablesPage`)
- **Origination (`CheckoutPage`)**: Cashier selects `Utang` or `Cash + Utang`, picks an active customer (or creates one inline), sets optional due date/note, and completes the sale.
- **Monitoring (`OwnerReportsPage`)**: Owner reviews `AR & collections aging` buckets (`Current`, `1–30`, `31–60`, `61–90`, `90+ Days Overdue`). Clicking an aging bucket navigates to `Utang Ledger` (`ReceivablesPage`) with that bucket filter pre-applied (`?arBucket=1_30`).
- **Collection (`ReceivablesPage`)**: Owner/Manager selects the customer row, inspects their chronological `Customer Ledger` (`CreditSale` debits vs `Collection` credits), enters `Amount (PHP)`, `Method`, and `Note` in `Record Collection`, and clicks `Collect Payment`.
- **Feedback & Record**: Toast notification + printable `Collection Acknowledgment` receipt card (`Previous Balance -> Amount Paid -> New Balance`) + new `Collection` credit row in the customer ledger. If a collection was recorded in error, clicking `Reverse Collection` opens a confirmation dialog before posting a `CollectionReversal` entry.

### Loop 3: Catalog Maintenance & Stock Replenishment Loop (`DashboardPage` / `OwnerReportsPage` -> `ProductsPage` <-> `InventoryPage`)
- **Detection**: Low-stock count surfaced on `DashboardPage` (`MetricCard`), `OwnerReportsPage` (`Low stock` table), and `ProductsPage` (`Low Stock` / `Out of Stock` status pills).
- **Decision & Action**: Manager opens `ProductsPage` or `InventoryPage`, clicks `Record Stock-In` (for supplier deliveries) or `Record Adjustment` (for shrinkage/spoilage/count corrections).
- **Cross-Module Verification**: Clicking `View Movements` on any product in `ProductsPage` navigates directly to `InventoryPage` (`Stock Movements`) with `productId` pre-selected in the Movement Ledger filter bar, showing the complete audit trail of `StockIn`, `Sale`, `VoidReversal`, and `Adjustment` rows for that SKU.

### Loop 4: Owner Financial Review & Transaction Audit Loop (`OwnerReportsPage` -> `SalesHistoryPage` -> `AuditPage`)
- **Content**: Owner selects a date range (`Today`, `7D`, `30D`, `MTD`, or custom `From`/`To`) on `OwnerReportsPage` and reviews Net Sales, Cash on Sales, Credit Originated, Estimated Gross Margin, Sales Trend bars, and Payment Mix.
- **Drilldown Action**: Clicking any daily bar in `Sales trend` navigates to `Sales & Receipts` (`SalesHistoryPage`) pre-filtered to that store date (`salesFrom` / `salesTo`), with a visible filter chip and a one-click `Reset` that also cleans the URL query string.
- **Inspection & Governance**: Owner clicks `View Receipt` to inspect the receipt snapshot or void details, and cross-checks high-sensitivity actions (`Sale.Voided`, `Receivable.PaymentCollected`, `Inventory.Adjusted`) in `Audit Logs` (`AuditPage`) with full CSV export.
