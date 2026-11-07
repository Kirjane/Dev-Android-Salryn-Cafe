# Salryn POS — Playwright Visual Baseline & QA Verification Report (Phase A0 / R1 + Wave A1–A4 Implementation)

**Target Application**: Salryn POS (Base Retail Edition + Signed U1 Accounts Receivable / Utang + Owner Financial Analytics)  
**Branch**: `antigravity/salryn-premium-pos-ux` (stacked on `codex/salryn-owner-analytics-w0-w4` @ `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`)  
**Worktree**: `C:\Salryn-Antigravity`  
**Phase**: `ANTIGRAVITY A0-R1 + Wave A1–A4 Implementation` ([GitHub Issue #19](https://github.com/Kirjane/Salryn/issues/19) / [GitHub Issue #20](https://github.com/Kirjane/Salryn/issues/20) / [Draft PR #21](https://github.com/Kirjane/Salryn/pull/21))  
**Status**:
- **`MOCKED_UI` Suite (`npm run test:e2e`)**: **PASS (`24 / 24` tests passing, exit code `0`)**
- **`MOCKED_UI` Visual Regression Gate (`npm run test:visual`)**: **PASS (`16 / 16` visual specs, `42 / 42` `toHaveScreenshot` comparisons passing with `updateSnapshots: "none"`, exit code `0`)**
- **`INTEGRATED_HOST` Suite (`npm run test:integrated-host`)**: **BLOCKED / NOT RUN (`overallVerdict: "BLOCKED"`, `code: "BLOCKED_NO_HOST"`, exit code `1` — no live licensed Host running on `127.0.0.1:5299`)**

---

## 1. Execution Environment, Determinism & Command Verification

| Property | Value |
|---|---|
| Operating System | Windows 10/11 x64 (`win32`) — required baseline OS for `"Segoe UI"` system font rendering |
| Shell | PowerShell (`pwsh`) |
| Node / Frontend Root | `C:\Salryn-Antigravity\src\Salryn.App.Web` |
| Test Runner | `@playwright/test` (`^1.65.0`, pinned in `package-lock.json`) |
| Browser Engine | Playwright-bundled Headless Chromium (`channel: undefined`, `Chrome/158.0.8127.18`) |
| Default Desktop Viewport | `1366 × 768` (`deviceScaleFactor: 1`, `hasTouch: false`, `isMobile: false`) |
| Timezone / Locale | `Asia/Manila` / `en-PH` |
| Frozen Fixture Clock | `2026-04-19T10:30:00+08:00` (`await page.clock.setFixedTime(...)` in `beforeEach`) |
| Pinned Terminal Machine Code | `SALRYN-MC-1111-2222-3333-4444` (seeded in `localStorage` before page load) |
| Snapshot Comparison Config | `updateSnapshots: "none"`, `maxDiffPixelRatio: 0.001`, `threshold: 0.2`, `animations: "disabled"`, `caret: "hide"`, `scale: "css"` |

### Verified Commands & Exit Codes

| Command | Working Directory | Target Project / Scope | Result | Exit Code |
|---|---|---|---|---|
| `npm ci` | `src/Salryn.App.Web` | Clean lockfile dependency installation | **PASS** | `0` |
| `npm run build` | `src/Salryn.App.Web` | TypeScript (`tsc -b`), Vite bundle (`vite build`), and build stamp (`scripts/write-build-stamp.mjs`) | **PASS** | `0` |
| `npm run test:e2e` | `src/Salryn.App.Web` | `playwright test --project=mocked-ui` (strictly `MOCKED_UI`: 16 visual specs + 8 interaction/defect-fix specs; never runs `integrated-host`) | **24 passed** | `0` |
| `npm run test:visual` | `src/Salryn.App.Web` | `playwright test --project=mocked-ui --grep @visual` (strict `toHaveScreenshot` compare/fail against 42 committed PNGs; never updates files) | **16 passed (42 snapshots matched)** | `0` |
| `npm run test:visual:update` | `src/Salryn.App.Web` | `playwright test --project=mocked-ui --grep @visual --update-snapshots=all` (manual, human-reviewed baseline update only) | **16 passed** | `0` |
| `npm run test:integrated-host` | `src/Salryn.App.Web` | `playwright test --project=integrated-host` (unmocked live Host gate on `127.0.0.1:5299`) | **1 failed, 2 did not run (`BLOCKED_NO_HOST`)** | `1` |

---

## 2. Two-Tier QA Separation (`MOCKED_UI` vs `INTEGRATED_HOST`)

Per `.agents/skills/salryn-playwright-ux-qa/SKILL.md` and `SALRYN-AG-A0-PLAYWRIGHT-REGRESSION-HARDENING-R1`, `playwright.config.ts` enforces two strictly isolated Playwright projects:

1. **Project `mocked-ui` (`e2e/mocked-ui-visual-baseline.spec.ts` & `e2e/mocked-ui-interactions-and-defects.spec.ts`)**:
   - Intercepts only `/api/*` HTTP requests (`url.pathname.startsWith("/api/")`) via `e2e/fixtures/mockApi.ts` using deterministic Philippine retail fixtures (`e2e/fixtures/syntheticData.ts`).
   - Asserts 42 first-party `await expect(page).toHaveScreenshot(snapshotName, { fullPage })` comparisons against committed baselines in `src/Salryn.App.Web/e2e/__screenshots__/*.png`.
   - Never runs `integrated-host.spec.ts` and never claims backend cryptographic license verification, SQLite persistence, or live LAN pairing.

2. **Project `integrated-host` (`e2e/integrated-host.spec.ts`)**:
   - Invoked **only** via `npm run test:integrated-host`.
   - Runs zero network mocks and zero entitlement bypasses against `SALRYN_INTEGRATED_HOST_URL` (default `http://127.0.0.1:5299`).
   - Exits **non-zero (`1`)** unless all three serial checks pass:
     1. Live `/health` HTTP `200` (`BLOCKED_NO_HOST` on `ECONNREFUSED`).
     2. Live `/api/license/status` is `Licensed`, `Host` type, and `machineMatched: true`.
     3. Disposable QA operator (`SALRYN_QA_HOST_USERNAME` / `SALRYN_QA_HOST_PASSWORD`) signs in through the real UI and completes a read-only navigation journey (`Sales & Receipts`, `Reports`).
   - Writes a redacted diagnostic artifact to `test-results/integrated-host-probe.json` (untracked; contains no license payloads, signatures, tokens, cookies, or customer data).

---

## 3. Complete Index of 42 Committed Expected Snapshots (`src/Salryn.App.Web/e2e/__screenshots__/`)

All 42 expected PNG snapshots are committed under `src/Salryn.App.Web/e2e/__screenshots__/` at their original paths and are verified by `await expect(page).toHaveScreenshot(...)`:

| # | Snapshot Filename | Surface / Component | Persona / Gate State | Viewport | Key Visual & UX Observations |
|---|---|---|---|---|---|
| 01 | `01-startup-unavailable.png` | `ConsoleApp` startup error gate | Unauthenticated (`503` on `/api/bootstrap/status`) | `1366×768` | Full-screen `bg-navy-900` card with `"Salryn POS unavailable"` and `"Try again"` CTA. |
| 02 | `02-license-unlicensed.png` | `LicensePage` (Host pre-auth gate) | `status: "Unlicensed"`, `machineMatched: false` | `1366×768` | Renders outside `AppShell`; shows Machine Code card, copy button, and `"Import license"` CTA. |
| 03 | `03-license-wizard-modal.png` | `LicensePage` Activation Wizard modal | Step 2 (`"Terms & Conditions"`) open via `"Import license"` | `1366×768` | Modal displays scrollable EULA text and acceptance checkbox before file upload. |
| 04 | `04-bootstrap-setup.png` | `BootstrapSetupPage` | `firstRunRequired: true` | `1366×768` | First-run onboarding card for store name + initial Admin account creation. |
| 05 | `05-login-page.png` | `LoginPage` (idle) | Unauthenticated | `1366×768` | Clean centered card on `bg-navy-900` with store branding (`Balite Neighborhood Mart`). |
| 06 | `06-login-error.png` | `LoginPage` (error state) | HTTP `401` invalid credentials | `1366×768` | Displays inline danger alert `"Invalid username or password."` inside login card. |
| 07 | `07-dashboard-healthy.png` | `DashboardPage` (healthy) | Admin + U1 entitled (`status: "ok"`) | `1366×768` | Shows `"All reads healthy"` green status pill, 4 KPI cards, and Owner home summary. |
| 08 | `08-dashboard-warnings.png` | `DashboardPage` (warnings) | Admin + U1 entitled (`status: "Attention needed"`) | `1366×768` | Shows `"Read warnings present"` amber pill, backup age warning, and low-stock items list. |
| 09 | `09-checkout-shift-closed.png` | `CheckoutPage` (shift closed) | Admin / Cashier (`currentShift: null`) | `1366×768` | Shift status banner shows closed state (`"Cashier workstation locked"`); `"Open Shift"` active. |
| 10 | `10-checkout-cash-cart.png` | `CheckoutPage` (open shift + Cash cart) | Open shift, 2 cart lines (`PHP 383.00`), `Cash` tendered `PHP 1,000.00` | `1366×768` | Shows 3-column grid + top payment mode bar; `Change due` shows `PHP 617.00` in emerald. |
| 11 | `11-checkout-utang-mode.png` | `CheckoutPage` (`Utang` mode defect) | Open shift, `Utang` mode selected | `1366×768` | **Visual proof of P0 defects `UX-001` & `UX-002`**: Payment mode & customer selector sit detached above the 3-column grid, while the right settlement column still shows `Cash tendered` and red `"Insufficient cash"` text inside `Change due`. |
| 12 | `12-checkout-review-modal.png` | `CheckoutPage` Review Checkout modal | `F9` / `"Review Checkout"` dialog open | `1366×768` | Pre-post verification modal showing line count, payment breakdown, and confirm button. |
| 13 | `13-checkout-fast-add-modal.png` | `CheckoutPage` Fast Add Product modal | Unmatched barcode `9999999999999` | `1366×768` | Inline modal (`"Fast Add Unknown Barcode"`) allowing Admin/Manager to create and price an unknown barcode without leaving checkout. |
| 14 | `14-sales-history-list.png` | `SalesHistoryPage` | Admin (`Sales.ViewHistory` + `Sales.Void`) | `1366×768` | Filterable receipt table with status pills (`Completed`, `Voided`) and payment method badges. |
| 15 | `15-sales-receipt-detail-modal.png` | `SalesHistoryPage` Sale Detail modal | Receipt `OR-20260419-0024` open | `1366×768` | Shows line items, payment breakdown, monospace 80mm thermal receipt preview (`<pre>`), and Void controls. |
| 16 | `16-receivables-utang-ledger.png` | `ReceivablesPage` (U1 Utang Ledger) | Admin + `ReceivablesUtangLedger` entitled | `1366×768` | Master-detail layout: top 4 AR KPI cards, left customer list (`Maria Clara Dela Cruz`), right customer ledger, payment collection form, and balance adjustment form. |
| 17 | `17-owner-reports-analytics.png` | `OwnerReportsPage` (full analytics + U1) | Admin + U1 entitled | `1366×768` | Period presets, 6 executive financial cards (Gross, Voided, Net Sales, Cash Collected, Utang Created, Utang Collected), Cash vs Credit split, Top Products, Void/Adjustment Accountability, and U1 AR Aging buckets. |
| 18 | `18-owner-reports-no-u1.png` | `OwnerReportsPage` (without U1) | Admin without `ReceivablesUtangLedger` (`403` on `/api/reports/owner/receivables`) | `1366×768` | Core sales analytics render normally while the Receivables Aging section degrades gracefully to an informational `"AR analytics unavailable"` alert. |
| 19 | `19-products-catalog-detail.png` | `ProductsPage` (master-detail) | Admin, catalog list + detail inspector | `1366×768` | Left catalog table + right product detail inspector with `"Edit Product"`, `"Stock-In"`, `"Adjust Stock"`, and `"View Movements"`. |
| 20 | `20-products-create-modal.png` | `ProductsPage` Create Product modal | `"Create Product"` custom `Dialog` open | `1366×768` | Multi-field product creation modal (custom `Dialog` lacks `Escape` handler — `UX-004`). |
| 21 | `21-inventory-movements.png` | `InventoryPage` | Admin | `1366×768` | Stock movement audit ledger with `StockIn`, `SaleOut`, and `Adjustment` badges and reason codes. |
| 22 | `22-inventory-stockin-modal.png` | `InventoryPage` Stock-In modal | `"Stock-In"` custom `Dialog` open | `1366×768` | Supplier delivery entry modal (also uses custom `Dialog` without `Escape` handler — `UX-004`). |
| 23 | `23-users-roles-permissions.png` | `UsersPage` | Admin (`Users.Manage`) | `1366×768` | 3-column layout: Users table, Role Permission Matrix, and Selected User detail / password reset panel. |
| 24 | `24-audit-logs.png` | `AuditPage` | Admin (`Audit.View`) | `1366×768` | Dual-tab Security & Operational Audit events table + Application Error logs. |
| 25 | `25-store-settings.png` | `StoreSettingsPage` | Admin (`Settings.Manage`) | `1366×768` | Store identity, TIN, receipt header/footer, paper width (`58mm`/`80mm`), and logo management. |
| 26 | `26-backup-restore-verified.png` | `BackupRestorePage` (verified state) | Admin (`Backup.Manage`) after clicking `"Verify"` | `1366×768` | **Visual proof of `UX-010` & toast overlap**: Simultaneous toast notification (overlapping top-right header user info), inline green alert, and modal confirmation dialog. |
| 27 | `27-lan-terminals-future-feature.png` | `HostLanControlPage` | Admin (`StoreTerminal.View`) | `1366×768` | Displays `"Future Feature — Preview Only"` alert alongside live LAN toggle, join code, and pairing approval tables (`UX-009`). |
| 28 | `28-license-authenticated.png` | `LicensePage` (inside `AppShell`) | Admin authenticated (`status: "Licensed"`) | `1366×768` | Shows `"Salryn is activated"`, `Valid` badge, license metadata, and enabled feature pills (`ReceivablesUtangLedger`). |
| 29 | `29-persona-cashier-shell.png` | `AppShell` + `CheckoutPage` (Cashier) | Cashier (`roleId: ...0003`) | `1366×768` | Auto-redirects from `dashboard` to `checkout`; sidebar shows strictly 2 items (`POS Checkout`, `Sales & Receipts`). |
| 30 | `30-persona-manager-shell.png` | `AppShell` + `DashboardPage` (Manager) | Manager (`roleId: ...0002`) | `1366×768` | Sidebar shows Cashier + Manager groups (`Utang Ledger`, `Dashboard`, `Reports`, `Product Catalog`, `Stock Movements`, `Audit`); hides Admin config pages. |
| 31 | `31-persona-no-u1-shell.png` | `AppShell` (Admin without U1) | Admin with `enabledFeatures: []` | `1366×768` | `Utang Ledger` is cleanly omitted from the Manager sidebar group when `ReceivablesUtangLedger` is not licensed. |
| 32 | `32-responsive-1024x768-checkout.png` | `CheckoutPage` @ `1024×768` | Legacy POS XGA (`4:3`) | `1024×768` | **Visual proof of `UX-007`**: Below `xl` (`1280px`), `CheckoutPage`'s 3-column grid collapses into a vertical stack, pushing Cart and Settlement below the fold. |
| 33 | `33-responsive-1280x720-dashboard.png` | `DashboardPage` @ `1280×720` | Compact HD counter monitor | `1280×720` | KPI grid fits cleanly at `1280×720`. |
| 34 | `34-responsive-1920x1080-owner-reports.png` | `OwnerReportsPage` @ `1920×1080` | Full HD back-office monitor | `1920×1080` | Generous whitespace; `max-w-[1600px]` container centers report cards cleanly. |
| 35 | `35-responsive-390x844-mobile-owner-reports.png` | `OwnerReportsPage` @ `390×844` | Mobile owner check (`iPhone 14`) | `390×844` | **Visual proof of `UX-006`**: Fixed `w-60` (`240px`) sidebar never collapses into a drawer on mobile, leaving only `150px` for `<main>`. |
| 36 | `36-scale-125pct-checkout.png` | `CheckoutPage` @ `1366×768` (`125%` zoom) | Windows `125%` DPI scaling simulation | `1366×768` | Effective width drops to `~1092px` (`< 1280px` `xl` breakpoint), triggering single-column stack on standard `1366×768` POS screens. |
| 37 | `37-scale-150pct-checkout.png` | `CheckoutPage` @ `1366×768` (`150%` zoom) | Windows `150%` DPI scaling simulation | `1366×768` | High-magnification stress test showing header pill truncation and vertical stacking. |
| 38 | `38-internal-official-setup.png` | `OfficialSetupPage` (internal) | Admin via synthetic `popstate` (`page: "official-setup"`) | `1366×768` | Renders internal BIR/EIS dry-run setup cards (unreachable from standard sidebar navigation — `UX-011`). |
| 39 | `39-internal-traditional-reporting.png` | `TraditionalReportingPage` (internal) | Admin via synthetic `popstate` (`page: "traditional-reporting"`) | `1366×768` | Renders `"Internal Compliance Reporting Lab"` with warning banner and X/Z/EJ/eSales draft controls (`UX-011`). |
| 40 | `40-terminal-activation-gate.png` | `TerminalActivationPage` | Unlicensed Terminal on `salryn-terminal.localhost` | `1366×768` | Full-screen dark activation gate (`SALRYN Terminal`) with deterministic machine code `SALRYN-MC-1111-2222-3333-4444`. |
| 41 | `41-terminal-remote-landing.png` | `RemoteClientLandingPage` | Licensed Terminal on `salryn-terminal.localhost` | `1366×768` | Terminal Mode client entry screen with `"Open Salryn POS terminal"` primary card and device tools. |
| 42 | `42-terminal-remote-pos-pairing.png` | `RemotePosClientPage` (unpaired `S1_UNPAIRED`) | Licensed Terminal requesting Host pairing | `1366×768` | `"Connect terminal to Host"` screen with `"Host join code"` and `"Terminal display name"` inputs. |

---

## 4. Automated Interaction & UX Defect Resolution Verification (`e2e/mocked-ui-interactions-and-defects.spec.ts`)

In addition to the 16 visual baseline tests (42 `toHaveScreenshot` assertions), `e2e/mocked-ui-interactions-and-defects.spec.ts` runs 8 behavioral and defect-resolution specs inside the `mocked-ui` project:

1. **Zero Unexpected Runtime `pageerror` Events Across Core Navigation (`PASS`)**:
   - Navigates sequentially across all 13 standard sidebar destinations (`POS Checkout` through `LAN Terminals · Future Feature`), waits for each page's `<main> <h1>` to render, records any console errors as test annotations, and asserts `pageErrors.length === 0`.
2. **Checkout Keyboard Barcode Scan (`Enter`) & `F9` Review Shortcut (`PASS`)**:
   - Enters barcode `4800011001015` into the scan input and presses `Enter`; verifies `Sinandomeng Premium Rice 5kg` (`PHP 285.00`) is added to the cart.
   - Clicks quick-tender `500` and presses `F9`; verifies `"Review Checkout"` opens and `Escape` closes it.
3. **`UX-004 FIXED` (`ProductsPage` Dialog Closes Cleanly on `Escape` Key) (`PASS`)**:
   - Opens `"Create Product"` modal in `ProductsPage`, presses `Escape`, and verifies the shared accessible `Dialog` closes and restores focus.
4. **`UX-005 FIXED` (`ProductsPage` `"View Movements"` Button Navigates Directly to `Stock Movements` Filtered by Product) (`PASS`)**:
   - Selects `Sinandomeng Premium Rice 5kg` in `ProductsPage`, clicks `"View Movements"`, and verifies direct navigation to `Stock Movements` (`/?page=inventory&productId=...`) with the active product filter pill.
5. **`UX-008 FIXED` (Sidebar Navigation Synchronizes URL Query Parameter and Supports Browser `Back` Navigation) (`PASS`)**:
   - Navigates via sidebar `Dashboard` → `Product Catalog`, verifies `?page=products` in the URL, calls `await page.goBack()`, and verifies return to `Dashboard`.
6. **Cross-Page Drilldown Verification — `OwnerReportsPage` → `SalesHistoryPage` (`PASS`)**:
   - Clicks the daily sales drilldown row on `OwnerReportsPage` and verifies `Sales & Receipts` opens with `salesFrom=2026-04-19` in the URL.
7. **`UX-001 & UX-002 FIXED` (`Utang` Mode Disables `Cash tendered` and Never Shows False `"Insufficient cash"` Warning) (`PASS`)**:
   - Adds an item to the cart, selects `Utang` in `"Sale payment"`, and verifies `Cash tendered` is disabled, `"Insufficient cash"` has count `0`, and `"Utang credit balance"` is visible.
8. **`UX-010 FIXED` (Backup Verification Completes Inline Without Blocking Modal `OK` Popup) (`PASS`)**:
   - Clicks `"Verify Backup"` on `Backup & Restore` and verifies `"Restore Backup"` is immediately enabled alongside the inline verification banner without requiring a modal `OK` dismissal.
