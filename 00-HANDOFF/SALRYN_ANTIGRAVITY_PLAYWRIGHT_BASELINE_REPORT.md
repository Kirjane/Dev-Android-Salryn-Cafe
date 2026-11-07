# Salryn POS — Playwright Visual Baseline & QA Verification Report (Phase A0)

**Target Application**: Salryn POS (Base Retail Edition + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics)  
**Branch**: `antigravity/salryn-premium-pos-ux` (stacked on `codex/salryn-owner-analytics-w0-w4` @ `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`)  
**Worktree**: `C:\Salryn-Antigravity`  
**Phase**: `ANTIGRAVITY A0` ([GitHub Issue #19](https://github.com/Kirjane/Salryn/issues/19))  
**Status**: **PASS (23 / 23 tests passing, 42 / 42 visual baseline screenshots captured and reviewed)**

---

## 1. Execution Environment & Command Verification

| Property | Value |
|---|---|
| Operating System | Windows 10/11 x64 (`win32`) |
| Shell | PowerShell (`pwsh`) |
| Node / Frontend Root | `C:\Salryn-Antigravity\src\Salryn.App.Web` |
| Test Runner | `@playwright/test` (`^1.65.0`) |
| Browser Engine | Headless Chromium (`Chrome/158.0.8127.18`) |
| Default Desktop Viewport | `1440 × 900` (`deviceScaleFactor: 1`) |
| Timezone / Locale | `Asia/Manila` / `en-PH` |

### Verified Commands & Exit Codes

| Command | Working Directory | Purpose | Result | Exit Code |
|---|---|---|---|---|
| `npm run build` | `src/Salryn.App.Web` | TypeScript project references (`tsc -b`), Vite production bundle (`vite build`), and build stamp generation (`scripts/write-build-stamp.mjs`) | **PASS** | `0` |
| `npm run test:e2e` | `src/Salryn.App.Web` | Full Playwright E2E suite (`MOCKED_UI` visual baseline + keyboard/interaction/defect specs + `INTEGRATED_HOST` loopback probe) | **23 passed (33.1s)** | `0` |
| `npm run test:visual` | `src/Salryn.App.Web` | Visual baseline subset (`playwright test --grep @visual`) regenerating and verifying all 42 screenshots in `e2e/__screenshots__/` | **16 passed** | `0` |

---

## 2. Two-Tier QA Separation (`MOCKED_UI` vs `INTEGRATED_HOST`)

Per `.agents/skills/salryn-playwright-ux-qa/SKILL.md`, this harness enforces a strict architectural separation between synthetic frontend rendering and live Host integration:

1. **Tier 1 — `MOCKED_UI` (`e2e/mocked-ui-visual-baseline.spec.ts` & `e2e/mocked-ui-interactions-and-defects.spec.ts`)**:
   - Intercepts only `/api/*` HTTP requests (`url.pathname.startsWith("/api/")`) via `e2e/fixtures/mockApi.ts` using deterministic Philippine retail fixtures (`e2e/fixtures/syntheticData.ts`).
   - Exercises all 16 `HostPage` routes, 4 `RemoteClientMode` states, 3 operator personas (`Admin`, `Manager`, `Cashier`), U1 entitlement variations (`ReceivablesUtangLedger` enabled vs absent / HTTP `403`), 6 responsive/zoom viewports, and keyboard-first workflows.
   - **Never** claims backend cryptographic license verification, SQLite persistence, or live LAN pairing.

2. **Tier 2 — `INTEGRATED_HOST` (`e2e/integrated-host.spec.ts`)**:
   - Runs zero network mocks and zero entitlement bypasses.
   - Probes the live loopback Host endpoint (`http://127.0.0.1:5299/health` and `/api/license/status`) and records a structured readiness artifact to `test-results/integrated-host-probe.json`:
     ```json
     {
       "tier": "INTEGRATED_HOST",
       "targetBaseUrl": "http://127.0.0.1:5299",
       "hostReachable": false,
       "healthStatus": null,
       "diagnosticReason": "BLOCKED_NO_LIVE_HOST_ON_5299: apiRequestContext.get: connect ECONNREFUSED 127.0.0.1:5299",
       "licensePayload": null,
       "note": "MOCKED_UI and INTEGRATED_HOST are strictly separated. No license or entitlement bypass is permitted."
     }
     ```
   - When a live licensed `Salryn.App.Host` instance is running on `127.0.0.1:5299`, the same spec automatically validates HTTP `200` on `/health` and records the live `licenseStatus` payload without modifying host state.

---

## 3. Complete Index of Committed Visual Baseline Screenshots (`src/Salryn.App.Web/e2e/__screenshots__/`)

All 42 PNG screenshots are committed under `src/Salryn.App.Web/e2e/__screenshots__/` and were visually inspected during the A0 audit:

| # | Filename | Surface / Component | Persona / Gate State | Viewport | Key Visual & UX Observations |
|---|---|---|---|---|---|
| 01 | `01-startup-unavailable.png` | `ConsoleApp` startup error gate | Unauthenticated (`503` on `/api/bootstrap/status`) | `1440×900` | Full-screen `bg-navy-900` card with `"Salryn POS unavailable"` and `"Try again"` CTA. |
| 02 | `02-license-unlicensed.png` | `LicensePage` (Host pre-auth gate) | `status: "Unlicensed"`, `machineMatched: false` | `1440×900` | Renders outside `AppShell`; shows Machine Code card, copy button, and `"Import license"` CTA. |
| 03 | `03-license-wizard-modal.png` | `LicensePage` Activation Wizard modal | Step 2 (`"Terms & Conditions"`) open via `"Import license"` | `1440×900` | Modal displays scrollable EULA text and acceptance checkbox before file upload. |
| 04 | `04-bootstrap-setup.png` | `BootstrapSetupPage` | `firstRunRequired: true` | `1440×900` | First-run onboarding card for store name + initial Admin account creation. |
| 05 | `05-login-page.png` | `LoginPage` (idle) | Unauthenticated | `1440×900` | Clean centered card on `bg-navy-900` with store branding (`Balite Neighborhood Mart`). |
| 06 | `06-login-error.png` | `LoginPage` (error state) | HTTP `401` invalid credentials | `1440×900` | Displays inline danger alert `"Invalid username or password."` inside login card. |
| 07 | `07-dashboard-healthy.png` | `DashboardPage` (healthy) | Admin + U1 entitled (`status: "ok"`) | `1440×900` | Shows `"Healthy"` green status pill, 6 KPI summary cards, and quick links. |
| 08 | `08-dashboard-warnings.png` | `DashboardPage` (warnings) | Admin + U1 entitled (`status: "warning"`) | `1440×900` | Shows `"Needs attention"` amber pill, backup age warning, and low-stock items list. |
| 09 | `09-checkout-shift-closed.png` | `CheckoutPage` (shift closed) | Admin / Cashier (`currentShift: null`) | `1440×900` | Shift status banner shows closed state; `"Open shift"` input and button active. |
| 10 | `10-checkout-cash-cart.png` | `CheckoutPage` (open shift + Cash cart) | Open shift, 2 cart lines (`₱357.00`), `Cash` tendered `₱500.00` | `1440×900` | Shows 3-column grid + top payment mode bar; `Change due` shows `₱143.00` in emerald. |
| 11 | `11-checkout-utang-mode.png` | `CheckoutPage` (`Utang` mode defect) | Open shift, `Utang` mode selected, customer `Elena Santos` | `1440×900` | **Visual proof of P0 defects `UX-001` & `UX-002`**: Payment mode & customer selector sit detached above the 3-column grid, while the right settlement column still shows `Cash tendered = 0.00` and alarming red `"Insufficient cash"` text inside `Change due`. |
| 12 | `12-checkout-review-modal.png` | `CheckoutPage` Review Checkout modal | `F9` / `"Review checkout"` dialog open | `1440×900` | Pre-post verification modal showing line count, payment breakdown, and `"Confirm and post sale"`. |
| 13 | `13-checkout-fast-add-modal.png` | `CheckoutPage` Fast Add Product modal | Unmatched barcode `4809999888877` | `1440×900` | Inline modal allowing Admin/Manager to create and price an unknown barcode without leaving checkout. |
| 14 | `14-sales-history-list.png` | `SalesHistoryPage` | Admin (`Sales.ViewHistory` + `Sales.Void`) | `1440×900` | Filterable receipt table with status pills (`Completed`, `Voided`) and payment method badges. |
| 15 | `15-sales-receipt-detail-modal.png` | `SalesHistoryPage` Sale Detail modal | Receipt `OR-2026-000142` open | `1440×900` | Shows line items, payment breakdown, monospace 58mm/80mm thermal receipt preview (`<pre>`), and Void controls. |
| 16 | `16-receivables-utang-ledger.png` | `ReceivablesPage` (U1 Utang Ledger) | Admin + `ReceivablesUtangLedger` entitled | `1440×900` | Master-detail layout: top 4 AR KPI cards, left customer list (`Elena Santos`, `Roberto Cruz`), right customer ledger, payment collection form, and balance adjustment form. |
| 17 | `17-owner-reports-analytics.png` | `OwnerReportsPage` (full analytics + U1) | Admin + U1 entitled | `1440×900` | Period presets, 6 executive financial cards (Gross, Voided, Net Sales, Cash Collected, Utang Created, Utang Collected), Cash vs Credit split, Top Products, Void/Adjustment Accountability, and U1 AR Aging buckets. |
| 18 | `18-owner-reports-no-u1.png` | `OwnerReportsPage` (without U1) | Admin without `ReceivablesUtangLedger` (`403` on `/api/reports/owner/receivables`) | `1440×900` | Core sales analytics render normally while the Receivables Aging section degrades gracefully to an informational alert. |
| 19 | `19-products-catalog-detail.png` | `ProductsPage` (master-detail) | Admin, row `Sinandomeng Premium Rice 5kg` selected | `1440×900` | Left catalog table + right product detail inspector with `"Edit Product"`, `"Stock-In"`, `"Adjust Stock"`, and `"View Movements"`. |
| 20 | `20-products-create-modal.png` | `ProductsPage` Create Product modal | `"New Product"` custom `Dialog` open | `1440×900` | Multi-field product creation modal (note: custom `Dialog` lacks `Escape` handler — `UX-004`). |
| 21 | `21-inventory-movements.png` | `InventoryPage` | Admin | `1440×900` | Stock movement audit ledger with `StockIn`, `SaleOut`, and `Adjustment` badges and reason codes. |
| 22 | `22-inventory-stockin-modal.png` | `InventoryPage` Record Stock-In modal | `"Record Stock-In"` custom `Dialog` open | `1440×900` | Supplier delivery entry modal (also uses custom `Dialog` without `Escape` handler — `UX-004`). |
| 23 | `23-users-roles-permissions.png` | `UsersPage` | Admin (`Users.Manage`) | `1440×900` | 3-column layout: Users table, Role Permission Matrix, and Selected User detail / password reset panel. |
| 24 | `24-audit-logs.png` | `AuditPage` | Admin (`Audit.View`) | `1440×900` | Dual-tab Security & Operational Audit events table + Application Error logs. |
| 25 | `25-store-settings.png` | `StoreSettingsPage` | Admin (`Settings.Manage`) | `1440×900` | Store identity, TIN, receipt header/footer, paper width (`58mm`/`80mm`), and logo management. |
| 26 | `26-backup-restore-verified.png` | `BackupRestorePage` (verified state) | Admin (`Backup.Manage`) after clicking `"Verify Backup"` | `1440×900` | **Visual proof of `UX-010` & toast overlap**: Simultaneous toast notification (overlapping top-right header user info), inline green alert, and modal confirmation dialog. |
| 27 | `27-lan-terminals-future-feature.png` | `HostLanControlPage` | Admin (`StoreTerminal.View`) | `1440×900` | Displays `"Future Feature — Preview Only"` alert alongside live LAN toggle, join code, and pairing approval tables (`UX-009`). |
| 28 | `28-license-authenticated.png` | `LicensePage` (inside `AppShell`) | Admin authenticated (`status: "Licensed"`) | `1440×900` | Shows `"Salryn is activated"`, `Valid` badge, license metadata, and enabled feature pills (`ReceivablesUtangLedger`). |
| 29 | `29-persona-cashier-shell.png` | `AppShell` + `CheckoutPage` (Cashier) | Cashier (`roleId: ...0003`) | `1440×900` | Auto-redirects from `dashboard` to `checkout`; sidebar shows strictly 2 items (`POS Checkout`, `Sales & Receipts`). |
| 30 | `30-persona-manager-shell.png` | `AppShell` + `DashboardPage` (Manager) | Manager (`roleId: ...0002`) | `1440×900` | Sidebar shows Cashier + Manager groups (`Utang Ledger`, `Dashboard`, `Reports`, `Product Catalog`, `Stock Movements`, `Audit`); hides Admin config pages. |
| 31 | `31-persona-no-u1-shell.png` | `AppShell` (Admin without U1) | Admin with `enabledFeatures: []` | `1440×900` | `Utang Ledger` is cleanly omitted from the Manager sidebar group when `ReceivablesUtangLedger` is not licensed. |
| 32 | `32-responsive-1024x768-checkout.png` | `CheckoutPage` @ `1024×768` | Legacy POS XGA (`4:3`) | `1024×768` | **Visual proof of `UX-007`**: Below `xl` (`1280px`), `CheckoutPage`'s 3-column grid collapses into a vertical stack, pushing Cart and Settlement below the fold. |
| 33 | `33-responsive-1280x720-dashboard.png` | `DashboardPage` @ `1280×720` | Compact HD counter monitor | `1280×720` | 3-column KPI grid fits cleanly at `1280×720`. |
| 34 | `34-responsive-1920x1080-owner-reports.png` | `OwnerReportsPage` @ `1920×1080` | Full HD back-office monitor | `1920×1080` | Generous whitespace; `max-w-[1600px]` container centers report cards cleanly. |
| 35 | `35-responsive-390x844-mobile-owner-reports.png` | `OwnerReportsPage` @ `390×844` | Mobile owner check (`iPhone 14`) | `390×844` | **Visual proof of `UX-006`**: Fixed `w-60` (`240px`) sidebar never collapses into a drawer on mobile, leaving only `150px` for `<main>`. |
| 36 | `36-scale-125pct-checkout.png` | `CheckoutPage` @ `1366×768` (`125%` zoom) | Windows `125%` DPI scaling simulation | `1366×768` | Effective width drops to `~1092px` (`< 1280px` `xl` breakpoint), triggering single-column stack on standard `1366×768` POS screens. |
| 37 | `37-scale-150pct-checkout.png` | `CheckoutPage` @ `1366×768` (`150%` zoom) | Windows `150%` DPI scaling simulation | `1366×768` | High-magnification stress test showing header pill truncation and vertical stacking. |
| 38 | `38-internal-official-setup.png` | `OfficialSetupPage` (internal) | Admin via synthetic `popstate` (`page: "official-setup"`) | `1440×900` | Renders internal BIR/EIS dry-run setup cards (unreachable from standard sidebar navigation — `UX-011`). |
| 39 | `39-internal-traditional-reporting.png` | `TraditionalReportingPage` (internal) | Admin via synthetic `popstate` (`page: "traditional-reporting"`) | `1440×900` | Renders `"Internal Compliance Reporting Lab"` with warning banner and X/Z/EJ/eSales draft controls (`UX-011`). |
| 40 | `40-terminal-activation-gate.png` | `TerminalActivationPage` | Unlicensed Terminal on `salryn-terminal.localhost` | `1440×900` | Full-screen dark activation gate (`SALRYN Terminal`) with 4-step wizard (`Machine Code`, `Terms`, `Upload`, `Confirm`). |
| 41 | `41-terminal-remote-landing.png` | `RemoteClientLandingPage` | Licensed Terminal on `salryn-terminal.localhost` | `1440×900` | Terminal Mode client entry screen with `"Open Salryn POS terminal"` primary card and device tools. |
| 42 | `42-terminal-remote-pos-pairing.png` | `RemotePosClientPage` (unpaired `S1_UNPAIRED`) | Licensed Terminal requesting Host pairing | `1440×900` | `"Connect terminal to Host"` screen with `"Host join code"` and `"Terminal display name"` inputs. |

---

## 4. Automated Interaction & UX Defect Verification Summary (`e2e/mocked-ui-interactions-and-defects.spec.ts`)

In addition to the 42 visual baseline captures, `e2e/mocked-ui-interactions-and-defects.spec.ts` runs 6 behavioral and defect-verification specs:

1. **Zero Unexpected Runtime Errors Across Core Navigation (`PASS`)**:
   - Navigates sequentially across `Dashboard` → `POS Checkout` → `Sales & Receipts` → `Utang Ledger` → `Reports` → `Product Catalog` → `Stock Movements` → `Users` → `Audit` → `Store Settings` → `Backup & Restore` → `LAN Terminals · Future Feature` → `License`.
   - Asserts `pageErrors.length === 0`.
2. **Checkout Keyboard Barcode Scan (`Enter`) & `F9` Review Shortcut (`PASS`)**:
   - Enters barcode `4800016644801` into `#pos-barcode-search` and presses `Enter`; verifies `Lucky Me! Pancit Canton Calamansi 60g` is immediately added to the cart.
   - Enters `100` in cash tendered and dispatches `F9`; verifies the `"Review checkout"` modal opens immediately.
3. **Defect Reproduction — `UX-004` (`ProductsPage` Custom Dialog Ignores `Escape` Key) (`PASS`)**:
   - Opens `"New Product"` modal in `ProductsPage`, presses `Escape`, and confirms the custom local `Dialog` remains open (unlike `ui/Modal.tsx`).
4. **Defect Reproduction — `UX-005` (`ProductsPage` `"View Movements"` Dead End) (`PASS`)**:
   - Selects a product in `ProductsPage`, clicks `"View Movements"`, and verifies that the user remains on `Product Catalog` with an inline text message (`"Viewing movements for ... Open the Inventory page for the full ledger."`) instead of navigating to `Stock Movements`.
5. **Defect Reproduction — `UX-008` (Sidebar Navigation Does Not Push Browser History) (`PASS`)**:
   - Navigates via sidebar `Dashboard` → `Product Catalog` → `Stock Movements` and verifies `window.location.pathname` and `window.history.state` never record sidebar page transitions.
6. **Cross-Page Drilldown Verification — `OwnerReportsPage` → `SalesHistoryPage` (`PASS`)**:
   - Clicks `"Open in Sales & Receipts"` on `OwnerReportsPage` and verifies `Sales & Receipts` opens with `?salesFrom=...&salesTo=...` populated in the URL and date inputs.
