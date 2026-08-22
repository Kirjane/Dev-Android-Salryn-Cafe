# FRONTEND CHECKOUT UX M1 — Workstation Redesign Report

## Branch / base

Branch: `KIRCH-FRONTEND-CHECKOUT-UX-M1`

Base branch: `KIRCH-REPAIR-MANUAL-M1-FIRST-RUN-DEV-SEED`

Accepted base SHA: `d552a7e56301a28bbe1d61b59c8f356ea0734ede`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the final response or repository branch head for the exact final SHA.

## Scope

This lane redesigns only the Checkout UI/UX.

It does not redesign Products, Inventory, Users, Sales History, Dashboard, app shell, backend, installer, launcher, RMO/BIR/eSales, reporting/export, licensing, seed data, database schema, or protected branches.

The accepted base already contains manually proven first-run/bootstrap/dev-seed repairs:

- `MP-M1-006` bootstrap/auth repair
- `MP-M1-007` dev seed FK repair

Those repairs are treated as base truth and are not reworked by this lane.

## Source audit summary

Audited before editing:

- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/api/checkout.ts`
- `src/Salryn.App.Web/src/api/products.ts`
- `src/Salryn.App.Web/src/api/types.ts`
- `src/Salryn.App.Web/src/api/session.ts`
- shared UI primitives under `src/Salryn.App.Web/src/components/ui`
- image/media/product image search terms

Existing checkout API truth:

- `GET /api/session`
- `GET /api/products`
- `GET /api/shift`
- `POST /api/shift/open`
- `POST /api/shift/close`
- `POST /api/checkout`

Existing product fields available to checkout: `id`, `sku`, `barcode`, `name`, `unitName`, `categoryName`, `brandName`, `sellingPrice`, `costPrice`, `stockOnHand`, `reorderLevel`, `trackStock`, `isActive`.

Existing checkout submit contract retained: product lines use `productId` and `quantityX1000`; payments use `method: Cash` and `amountCents`.

Paged product response supports `items`, `totalCount`, `pageNumber`, `pageSize`, and `totalPages`.

## What was wired before

Before this UX pass, Checkout already used real APIs for session/operator display, shift state, open shift, close shift, product search/list, checkout submit, and backend error mapping.

The weak parts were presentation and operator flow: product picker was table-first, cart was table-heavy, tender panel was plain, checkout result was diagnostic rather than receipt-like, runtime proof took too much visual attention, product refetch after successful checkout was not clear, and low/out-of-stock states were weak.

## What remains wired after

After this UX pass, Checkout still uses the same repo contracts:

- session from `fetchSession()`
- shift from `fetchHostShift()`
- open/close shift through `openHostShift()` / `closeHostShift()`
- products from `fetchProducts({ search, isActive: true, page, pageSize })`
- checkout completion through `submitCheckout({ lines, payments: Cash })`
- backend `ApiError` mapping preserved

No fake data source was introduced.

## UX changes

Checkout was reshaped into a POS-style workstation:

1. Top strip with operator, role, shift state, short shift ID, opened time, opening cash, refresh, and compact shift controls.
2. Three-zone layout: product search/picker, active cart, and payment/receipt summary.
3. Product picker now uses a large scan/search input, result count from paged API metadata, text-first product cards, SKU/barcode, category/brand chips, price/unit, stock state, disabled add for out-of-stock tracked items, and simple Prev/Next pagination.
4. Cart now uses cashier-readable line cards with product name, SKU, unit, unit price, quantity minus/input/plus controls, remove action, line total, item count, clear action, and tracked stock cap handling.
5. Tender panel now has a large subtotal block, cash tender input, quick Exact/500/1000 buttons, large change due, explicit disabled-state reason, and large Complete Checkout button.
6. Success state now shows receipt number, short sale ID, total, change, backend status/message, and New sale action.
7. Products and shift are refetched after successful checkout.
8. Diagnostics moved to a lower collapsible runtime proof panel.

## What was not wired and left not invented

Not added: discounts, non-cash payment methods, customer accounts, receipt printing, scanner hardware integration, official/BIR/eSales live posting, backend product search endpoint changes, backend checkout contract changes, or product image/media functionality.

## No-product-image confirmation

This lane does not add product images, image upload, image URL fields, media fields, thumbnail columns, placeholder product photos, fake product cards with pictures, seeded images, or database image columns.

Checkout product cards are text-first and use existing product identity fields only: name, SKU, barcode, category, brand, unit, price, stock, and status.

## Known limitations

- Connector session cannot run local restore, build, npm build, app launch, or WebView manual proof.
- Checkout remains cash-only because the wired contract only supports cash.
- Product picker remains paged at 25 results per page with simple Prev/Next.
- Quantity controls use whole-number quantity steps; backend still receives `quantityX1000`.
- Product stock shown is product API state; backend remains final authority during checkout.

## Required gates

No local commands were run by this ChatGPT connector session.

Required local gates:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only manual-m1-bootstrap-auth-repair`
- `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-lan-bind-truth`
- `dotnet run --project tools/Salryn.Tools.CoreSmoke`
- `npm --prefix src/Salryn.App.Web run build`
- `git diff --check`
- `git status`

## Required manual checkout proof

Manual proof still requires Kirch/Sol local execution:

1. launch app with existing safe seeded dev DB
2. sign in with the accepted admin account
3. open Checkout
4. confirm operator/shift strip loads
5. open shift if closed
6. search terms: `hammer`, `rice`, `paint`, `screw`, `noodles`
7. add at least three products
8. change quantities
9. remove one item
10. enter insufficient cash and confirm disabled/warning state
11. enter sufficient cash
12. complete local checkout
13. confirm backend success, receipt/sale result, total/change, no crash
14. start new sale
15. search products again
16. confirm no product images appear in Checkout
17. stop app cleanly

## Non-claims

This connector session does not claim local restore pass, local build pass, CoreSmoke pass, npm build pass, app launch proof, manual checkout proof, receipt printing, official/BIR/eSales posting, installer proof, beta readiness, production readiness, or branch promotion.

## Status

Connector-authored status: `CANDIDATE_PENDING_SOL_REVERIFY`.

Acceptance status should become `GREEN_AUTOMATED_ONLY` only after automated gates pass, and `GREEN_AUTOMATED_AND_MANUAL_CHECKOUT_PROVEN` only after the manual checkout flow passes.
