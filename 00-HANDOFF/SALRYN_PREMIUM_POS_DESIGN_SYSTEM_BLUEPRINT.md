# Salryn POS — Premium Retail POS Design System & Component Blueprint (A0 -> Issue #20)

- **Document ID**: `SALRYN_PREMIUM_POS_DESIGN_SYSTEM_BLUEPRINT`
- **Phase**: Antigravity A0 — Whole-System Frontend Audit, UX Architecture & Playwright Visual Baseline
- **Target Application**: Base **Salryn POS** Retail Application (Host Console + LAN Terminal Client + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics; **excludes Salryn Café Yza**)
- **Worktree / Branch**: `C:\Salryn-Antigravity` (`antigravity/salryn-premium-pos-ux`)
- **Purpose**: Establish the authoritative visual, typographic, ergonomic, and component engineering specification to be executed in [GitHub Issue #20](https://github.com/Kirjane/Salryn/issues/20) after A0 architectural sign-off.

---

## 1. Core Design Philosophy & Aesthetic Direction

Salryn POS is an operational retail counter and financial ledger workstation used under bright fluorescent store lighting on `1366×768` to `1920×1080` displays during fast customer queues.
1. **Operational Speed First**: Zero decorative clutter, zero layout shift during barcode scanning, instant keyboard reachability (`Enter` to scan, `F9` to review checkout, `Escape` to dismiss any modal), and high-contrast touch/click targets (`min-h-[38px]` standard, `min-h-[44px]` primary counter actions).
2. **Financial Trust & Tabular Precision**: Every currency amount (`₱`), quantity, percentage, receipt number, and timestamp MUST use `tabular-nums` (`font-variant-numeric: tabular-nums`) and right-alignment inside tables so decimal points align vertically across rows.
3. **Calm, High-Contrast Light Surfaces with Deep Slate Chrome**:
   - Navigation shell: Deep slate navy (`bg-slate-950` / `#020617`) for zero glare and unmistakable spatial anchoring.
   - Workspace canvas: Warm cool-neutral canvas (`bg-slate-100` / `#f1f5f9`) with crisp white elevated cards (`bg-white`, `border-slate-200/90`, subtle `shadow-xs`).
   - Primary interactive accent: Trust Royal Blue (`blue-600` / `#2563eb`, hover `blue-700` / `#1d4ed8`, focus ring `ring-blue-500/30`).

---

## 2. Tokenized Typography Scale & Numerals (`src/components/ui/tokens.ts` & `index.css`)

### 2.1 Font Stacks
- **UI & Operational Sans (`--font-sans`)**: `"Inter", "Segoe UI", system-ui, -apple-system, sans-serif`
- **Tabular Financial & Receipt Mono (`--font-mono`)**: `"JetBrains Mono", "Cascadia Mono", "Consolas", ui-monospace, monospace`

### 2.2 Type Scale & Hierarchy Contract

| Token Name | Tailwind Utility Contract | Size / Line-Height | Usage Across Salryn POS |
| :--- | :--- | :--- | :--- |
| `display-total` | `text-3xl font-bold tracking-tight tabular-nums text-slate-950` | `30px / 36px` | Checkout Grand Total, Change Due hero amount, Utang Balance Due hero |
| `kpi-metric` | `text-2xl font-bold tracking-tight tabular-nums text-slate-950` | `24px / 32px` | `MetricCard` primary value on Dashboard, Utang Summary, Owner Reports |
| `page-title` (`h1`) | `text-xl font-semibold tracking-tight text-slate-950` | `20px / 28px` | Top-level Page Header title (`Store Operations Overview`, `Cashier Checkout Workstation`) |
| `section-title` (`h2`) | `text-base font-semibold text-slate-900` | `16px / 24px` | `CardTitle`, Modal `Dialog` title |
| `subsection-title` (`h3`) | `text-sm font-semibold text-slate-800` | `14px / 20px` | Sub-panel headers, drawer sections |
| `body-default` | `text-sm font-normal text-slate-700` | `14px / 20px` | Form inputs, table cells, standard descriptions |
| `body-dense` | `text-xs font-normal text-slate-600` | `12px / 18px` | Secondary metadata, SKU/barcode sub-labels, helper hints |
| `table-header` | `text-xs font-semibold uppercase tracking-wider text-slate-500` | `12px / 16px` | `DataTable` `<th>` column headers, `MetricCard` labels |
| `receipt-tape` | `font-mono text-xs leading-relaxed whitespace-pre-wrap text-slate-900` | `12px / 19.5px` | Thermal receipt preview (`SalesHistoryPage`, `ReceivablesPage`, `OfficialSetupPage`) |

---

## 3. Semantic Color & Status Token System

Currently, status colors across `StatusPill.tsx`, `Alert.tsx`, `CheckoutPage.tsx`, and `OwnerReportsPage.tsx` are mostly aligned but have critical misuse bugs (such as `ReceivablesPage.tsx:143` rendering errors as `tone="info"`, and `CheckoutPage.tsx:369` rendering Utang sales as `"Cash sale committed"`).

### 3.1 Semantic Tone Matrix

| Semantic Tone | Surface / Background | Border | Text / Icon | Mandatory Domain Meaning in Salryn POS |
| :--- | :--- | :--- | :--- | :--- |
| **Primary / Brand (`primary`)** | `bg-blue-600` (solid) / `bg-blue-50` (subtle) | `border-blue-600` / `border-blue-200` | `text-white` / `text-blue-800` | Active navigation item, primary CTA button, active filter chip, selected table row (`bg-blue-50/70`) |
| **Positive / Settled (`success`)** | `bg-emerald-50` | `border-emerald-200` | `text-emerald-800` | Completed sale, Open shift, Active product/user, Verified backup, LAN Ready, Paid receivable |
| **Credit / Utang / Attention (`warning`)** | `bg-amber-50` | `border-amber-200` | `text-amber-900` | **Utang / Credit Sale**, Partial settlement, Low-stock warning, Unverified/stale backup, Pending LAN pairing |
| **Destructive / Overdue / Void (`danger`)** | `bg-rose-50` | `border-rose-200` | `text-rose-800` | **Voided sale**, Reversed collection, Overdue Utang / Over credit limit, Out-of-stock (`0`), Failed API/validation error, Destructive confirmation |
| **Informational (`info`)** | `bg-sky-50` | `border-sky-200` | `text-sky-900` | Neutral system guidance, active filter notice, read-only mode explanation |
| **Neutral / Inactive (`neutral`)** | `bg-slate-100` | `border-slate-200` | `text-slate-700` | Closed shift, Inactive/deactivated product or user, Disabled LAN runtime, Historical snapshot badge |

---

## 4. Layout Grid, 8px Spacing Rhythm & Responsive Breakpoints

### 4.1 Spacing Scale
All layout containers, cards, and form groups follow an 8px base rhythm (`4px` micro-gap):
- **Page Canvas Padding**: `p-4 lg:p-6` (`16px` on compact displays, `24px` on `1024px+` displays) with `space-y-5` (`20px`) vertical section rhythm.
- **Card Padding**: `CardHeader` `px-5 py-3.5`, `CardBody` `p-5`, `CardFooter` `px-5 py-3.5 bg-slate-50/70`.
- **Dense Counter Mode (Checkout)**: `p-4` cards so the entire 3-column workstation fits within a `1366×768` viewport without vertical page scrolling.

### 4.2 Responsive Shell & Workstation Breakpoints (Fixing P1 Responsive Defects)
1. **Collapsible Sidebar on `< 1024px` (`AppShell.tsx`)**:
   - **Current Defect**: `<aside className="flex w-60 flex-col ...">` is permanently `240px` wide at all viewport widths (`35-responsive-390x844-mobile-owner-reports.png`).
   - **Target Architecture**:
     - `lg:` (`>= 1024px`): Persistent left sidebar (`w-60`).
     - `< 1024px`: Collapses into a slide-over drawer triggered by a top-left Menu button in the `AppShell` header bar, freeing 100% of horizontal width for `1024×768` (at 125% zoom) and mobile `390×844` screens!
2. **Checkout Workstation Grid (`CheckoutPage.tsx`)**:
   - **Current Defect**: Uses `xl:grid-cols-12` (`>= 1280px`), which collapses all 3 columns into a single vertical stack on `1024×768` monitors or on `1366×768` laptops set to 125% Windows display scaling (`32-responsive-1024x768-checkout.png`, `36-scale-125pct-checkout.png`).
   - **Target Architecture**:
     - Consolidate Payment Mode (`Cash` / `Utang` / `Cash + Utang`) + Customer selector **directly inside the right-hand Tender & Checkout column** (eliminating the separate top Utang banner card!).
     - Use a 2-zone responsive layout at `lg:` (`>= 1024px` -> `lg:grid-cols-12`): Left/Center Product Search + Cart (`lg:col-span-7 xl:col-span-8`) and Right Sticky Tender & Checkout Panel (`lg:col-span-5 xl:col-span-4`).

---

## 5. UI Primitive Refactoring Specification (`src/components/ui/*`)

To eliminate duplicate inline components and visual drift across the 22 pages, the following upgrades are specified for `src/components/ui/`:

### 5.1 Unified Accessible `Dialog.tsx` (Eliminating 6 Duplicate Local Implementations)
- **Current State**:
  - [`src/components/ui/Dialog.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/Dialog.tsx#L1-L72) is used only by `BackupRestorePage.tsx` and `HostLanControlPage.tsx`.
  - `CheckoutPage.tsx:71`, `SalesHistoryPage.tsx:9`, `ProductsPage.tsx:135`, `InventoryPage.tsx:102`, `LicensePage.tsx:98`, and `TerminalActivationPage.tsx:31` each define their own local modal component!
  - Worse, `Dialog.tsx`, `ProductsPage.tsx`, `InventoryPage.tsx`, `LicensePage.tsx`, and `TerminalActivationPage.tsx` **do not listen for `Escape`**!
- **Target Contract for `src/components/ui/Dialog.tsx`**:
  - Support `size?: "sm" | "md" | "lg" | "xl" | "2xl" | "4xl"` (`max-w-md` through `max-w-4xl`) so `SalesHistoryPage` (`max-w-4xl`) and `ProductsPage` (`max-w-xl`) can use the shared primitive.
  - Built-in `Escape` key dismissal (unless `busy === true`).
  - Built-in focus management (auto-focus first focusable control or primary input on open; restore focus to trigger element on close).
  - Optional `headerBadge?: ReactNode` and `footer?: ReactNode` slots.
  - Delete all 6 local `Dialog`/`WizardShell` duplicates across `src/pages/*` and route every modal through `src/components/ui/Dialog.tsx`.

### 5.2 Standardized `Select.tsx` Primitive
- **Current State**: There is no `Select.tsx` in `src/components/ui/`. Every page styles native `<select>` elements manually with slightly different border, height, focus ring, and padding classes (`CheckoutPage.tsx:343`, `SalesHistoryPage.tsx:196`, `ProductsPage.tsx:287`, `InventoryPage.tsx:257`, `BootstrapSetupPage.tsx:207`).
- **Target Contract**: Create `src/components/ui/Select.tsx` matching `TextInput.tsx` height (`h-9` / `36px`), border (`border-slate-300`), focus ring (`focus:border-blue-600 focus:ring-2 focus:ring-blue-500/20`), and disabled styling.

### 5.3 Enhanced `DataTable.tsx` & Financial Table Conventions
- **Current State**: [`DataTable.tsx`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/src/components/ui/DataTable.tsx#L1-L76) provides a basic wrapper, while `ReceivablesPage.tsx:169` and `OwnerReportsPage.tsx:28,284,300,317` use raw unstyled `<table>` elements (`text-xs` with no cell padding, borders, or hover states).
- **Target Contract**:
  - Replace all raw `<table>` elements in `ReceivablesPage.tsx` and `OwnerReportsPage.tsx` with `DataTable` (`DataTableHead`, `DataTableBody`, `DataTableRow`, `DataTableHeaderCell`, `DataTableCell`).
  - Add `align?: "left" | "right" | "center"` and `numeric?: boolean` props to `DataTableHeaderCell` and `DataTableCell` so currency/quantity columns automatically apply `text-right tabular-nums font-medium`.
  - Add `selected?: boolean` prop to `DataTableRow` for consistent active row highlighting (`bg-blue-50/80 hover:bg-blue-50`).

### 5.4 Unified Toast & Inline Feedback Contract (`Notifications.tsx` + `Alert.tsx`)
- **Current State**: `CheckoutPage`, `SalesHistoryPage`, `ProductsPage`, `InventoryPage`, `BackupRestorePage`, and `HostLanControlPage` use `useNotifications()`, whereas `ReceivablesPage`, `UsersPage`, and `StoreSettingsPage` use ad-hoc local `message` states (with `ReceivablesPage` rendering errors as `tone="info"`!).
- **Target Contract**:
  - Every mutation across all pages MUST emit a semantic toast via `useNotifications()` (`notify.success`, `notify.error`, `notify.warning`) in addition to any persistent inline form error `Alert` (`tone="danger"`).

---

## 6. Phased Implementation Roadmap for Issue #20 (Post-Review Execution)

To ensure zero regressions to financial logic, licensing, or LAN boundaries, [GitHub Issue #20](https://github.com/Kirjane/Salryn/issues/20) should be executed in 4 atomic, verifiable waves:

| Wave | Scope | Files Touched | Verification Gate |
| :--- | :--- | :--- | :--- |
| **Wave 1 — Core Shell, Wayfinding & UI Primitives** | 1. Fix `App.tsx:166` retry bug (`sessionQuery.refetch()`) and browser history URL sync.<br>2. Fix `AppShell.tsx` icon mapping (`Record<AppPage, LucideIcon>`) and add responsive `< 1024px` drawer sidebar.<br>3. Upgrade `Dialog.tsx` (`Escape` key, sizes, focus management) and add `Select.tsx` + numeric `DataTable` props. | `App.tsx`, `AppShell.tsx`, `navigation.ts`, `src/components/ui/*` | `npm run build` + Playwright navigation, `Escape` modal, and `390×844` / `1024×768` responsive tests pass. |
| **Wave 2 — Counter & Cash/Utang Workstation (`CheckoutPage` & `SalesHistoryPage`)** | 1. Consolidate Payment Mode (`Cash`/`Utang`/`Cash + Utang`) & Customer selector into the right-hand Tender column.<br>2. Auto-zero and disable `Cash tendered` when `paymentMode === "Utang"`; show accurate `Charged to Utang` vs `Change due` without false `"Insufficient cash"` errors.<br>3. Fix `Sale Completed` modal badge to reflect actual payment method (`Cash`, `Utang`, `Cash + Utang`).<br>4. Add Close Shift confirmation dialog.<br>5. Add Payment Method pill column to `SalesHistoryPage` and clear stale URL params on `Reset`. | `CheckoutPage.tsx`, `SalesHistoryPage.tsx` | Playwright Cash, Utang, and Split checkout flows + `F9` / `Enter` barcode tests pass. |
| **Wave 3 — Financial Trust Surfaces (`ReceivablesPage` & `OwnerReportsPage`)** | 1. Replace `ReceivablesPage` `tone="info"` error state with `tone="danger"` + `useNotifications()` toasts.<br>2. Migrate `ReceivablesPage` customer ledger and `OwnerReportsPage` summary tables from raw `<table>` to `DataTable` with `EmptyState`s.<br>3. Add confirmation dialog before `Reverse Collection` on `ReceivablesPage`.<br>4. Wire `OwnerReportsPage` AR aging bucket drilldown (`?arBucket=...`) into `ReceivablesPage` filter state. | `ReceivablesPage.tsx`, `OwnerReportsPage.tsx` | Playwright Utang collection, reversal confirmation, and Owner Reports -> AR / Sales drilldown tests pass. |
| **Wave 4 — Catalog, Inventory & Governance Polish (`ProductsPage`, `InventoryPage`, `UsersPage`, `StoreSettingsPage`, `BackupRestorePage`)** | 1. Wire `ProductsPage` `"View Movements"` button to navigate directly to `InventoryPage` pre-filtered by `productId`, and add Product filter to `InventoryPage`.<br>2. Replace local `Dialog`s in `ProductsPage`, `InventoryPage`, `LicensePage`, `TerminalActivationPage` with shared `ui/Dialog.tsx`.<br>3. Invalidate `["app-shell-store-branding"]` on logo upload/remove in `StoreSettingsPage`.<br>4. Move `UsersPage` creation form into a clean modal or collapsible drawer so User Directory is primary. | `ProductsPage.tsx`, `InventoryPage.tsx`, `UsersPage.tsx`, `StoreSettingsPage.tsx`, `LicensePage.tsx`, `TerminalActivationPage.tsx` | Full Playwright visual regression refresh + `npm run build` + `dotnet test` clean. |
