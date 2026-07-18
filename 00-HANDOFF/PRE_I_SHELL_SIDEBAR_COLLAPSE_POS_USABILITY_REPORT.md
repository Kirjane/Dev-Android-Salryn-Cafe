# PRE-I Shell / Sidebar Collapse / POS Usability Report

## Candidate

- Base branch: `KIRCH-PRE-I-PRODUCT-SKU-BARCODE-CONTRACT`
- Accepted base SHA: `656023da9610a8a3c7796d42fa156bd20f5eab7f`
- Candidate branch: `KIRCH-PRE-I-SHELL-SIDEBAR-COLLAPSE-POS-USABILITY`
- Implementation commit: `0e1a6d604b0bd845ef40eb622280bee1e9f3410a`
- Decision: `PRE_I_SHELL_SIDEBAR_COLLAPSE_POS_USABILITY_REPAIRED_AND_PASSED`
- Promotion: not performed

## Scope result

This lane is limited to frontend shell, layout, responsive-overflow behavior, and a focused source gate. It does not change backend business logic, audit behavior, licensing behavior, LAN behavior, or printer behavior.

1. The primary sidebar now collapses from 240 px to 64 px, persists the preference in local storage, exposes accessible expand/collapse labels and state, and preserves icon navigation and the pending-terminal indicator.
2. POS Checkout uses a compact command header and a compact wrapping operator/shift strip.
3. The desktop Checkout grid now gives Payment / Checkout the widest share: scan `0.85fr`, cart `0.95fr`, payment `1.2fr` with a 21 rem minimum.
4. A successful normal shift-open no longer renders the backend success message as a page-level warning. The existing success notification remains the success treatment; error behavior remains unchanged.
5. Scan, cart, and payment columns use shrink-safe minimums and rebalance without widening the application shell.
6. Product Catalog's selected-product card remains in natural page flow, so the main workspace owns vertical scrolling and the panel is not trapped by a forced viewport height.
7. Product, Audit, Inventory, and Sales filter rows wrap with flexible controls.
8. The shell, root document, dialogs, media, and page workspaces were audited for horizontal overflow. Wide data tables retain their existing local horizontal scroll boundary.

## Browser runtime proof

Runtime QA used an isolated development store and local API only. Browser console errors were zero.

| Viewport / page | Result |
| --- | --- |
| 1280 px shell, expanded | Sidebar 240 px; main 1040 px; document scroll width equals 1280 px viewport. |
| 1280 px shell, collapsed | Sidebar 64 px; main 1216 px; document scroll width equals viewport. |
| 1280 px Checkout | Scan 321 px, cart 359 px, payment 454 px; no page-level shift-open warning; main scroll width equals client width. |
| 760 px Product Catalog | Search/filter controls wrapped into two rows; document width remained 760 px. |
| Selected product | Card content was fully represented in natural flow; main workspace owned vertical scroll; no card clipping or horizontal overflow. |
| 390 px Product and Checkout | Document width remained 390 px; main scroll width equaled client width with the 64 px sidebar. |

The isolated QA state was not production data, and the QA API process was stopped after inspection.

## Automated gates

- `npm --prefix src/Salryn.App.Web run build` — PASS. The existing Vite chunk-size advisory remains informational.
- `dotnet build Salryn.sln -c Release` — PASS. Existing warnings only; zero errors.
- Focused CoreSmoke `pre-i-shell-sidebar-collapse-pos-usability` — PASS.
- Full `Salryn.Tools.CoreSmoke` with the canonical activation-proof manifest — PASS: `SALRYN Core smoke checks passed.`
- Focused regression gates — PASS:
  - `web-a3-shell-design-system`
  - `v2-react-checkout`
  - `pos-manual1-frontend-repair`
  - `pos-manual1-r3-frontend-safety-ux-repair`
  - `pre-i-client-facing-product-polish`
  - `pre-i-action-notification-system`
  - `pre-i-product-sku-barcode-contract`
  - `pos-m3-barcode-scanner-cashier-flow`
  - `pos-m4-fast-add-unknown-barcode-inventory`
  - `pos-m5-product-write-management`
  - `pos-m6-stock-in-stock-adjustment`
- `git diff --check` — PASS.

## Files changed

- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/components/ui/CommandBar.tsx`
- `src/Salryn.App.Web/src/components/ui/Dialog.tsx`
- `src/Salryn.App.Web/src/index.css`
- `src/Salryn.App.Web/src/pages/AuditPage.tsx`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx`
- `tests/Salryn.Tests/PreIShellSidebarCollapsePosUsabilityChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Remaining manual judgment

No automated or browser-runtime blocker remains. Kirch may still judge preferred sidebar default, Checkout density, and payment-panel feel on the actual target display and touch hardware before promotion.
