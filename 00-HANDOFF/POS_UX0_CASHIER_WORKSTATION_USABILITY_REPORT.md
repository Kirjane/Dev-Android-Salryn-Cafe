# POS-UX0 Cashier Workstation Usability Repair Report

Date: 2026-07-12  
Branch: `KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY`  
Base branch: `KIRCH-POS-M1-RECEIPT-SNAPSHOT-FILE-RECEIPT`  
Base SHA: `365a6080f5c744422596180168d560f2cdbed4a9`  
Head at report authoring: `20f1ffc5411b5f3e64637a9135a1136e765d4435`

Authoring mode: GitHub connector-backed frontend-only repair. Local shell gates and manual browser proof were not executable from this session.

---

## 1. Scope

POS-UX0 repairs cashier workstation usability only.

In scope:

- checkout laptop-safe layout
- checkout shift-closed lock state
- product search/results bounded scroll areas
- Product Management naming cleanup to Product Catalog
- Inventory naming cleanup to Stock Movements
- stock movement filter/layout clarification
- Sales History detail separation into a dialog
- report file

Out of scope and not implemented:

- backend transaction rewrites
- checkout calculation changes
- receipt rendering changes
- printer logic
- barcode scanner logic
- Fast Add Product
- product write backend
- stock-in/adjustment backend
- void sale
- installer/package
- RMO/BIR/eSales
- Crystal runtime
- cloud upload

---

## 2. Files changed

- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `00-HANDOFF/POS_UX0_CASHIER_WORKSTATION_USABILITY_REPORT.md`

No backend, schema, receipt renderer, printer, RMO, BIR, eSales, or installer files were changed.

---

## 3. Defect fixes

### 3.1 Checkout layout scroll problem

Before:

- Checkout used three visual columns but product cards and cart content could extend page height.
- Cashier could need normal page scrolling to reach payment or complete checkout controls.

After:

- Workstation uses a laptop-safe fixed grid on large screens:
  - left: product search/results
  - center: active cart
  - right: payment/summary
- Product results use a dedicated scroll area.
- Cart lines use a dedicated scroll area.
- Payment and Complete Checkout stay in the right column and remain visible during normal cashier flow.
- Extremely small viewports may still scroll normally.

### 3.2 Shift gate

Before:

- Checkout controls were still visually present when the shift was closed, even though backend validation could reject checkout.

After:

- Closed shift shows `Cashier workstation locked`.
- Product/cart/payment/complete checkout controls are hidden until the shift is open.
- Opening the shift refreshes shift/product data and transitions into workstation mode.
- Closing the shift clears the active cart and returns the page to the locked state.

### 3.3 Naming cleanup

Before:

- Navigation/page text used `Products` / `Product Management` and `Inventory`, which implied write/maintenance capability.

After:

- Product page/nav label is `Product Catalog`.
- Inventory page/nav label is `Stock Movements`.
- Copy explicitly says both pages are read-only in this lane.

### 3.4 Product Catalog search layout

Before:

- Product list/results could push the page vertically while typing or changing filters.

After:

- Catalog search uses a bounded results panel.
- Filters stay in a fixed header area.
- Product details are shown in a stable side panel.

### 3.5 Checkout laptop-safe layout

Before:

- Payment controls could be visually displaced by product/cart content.

After:

- Product, cart, and payment areas are separated.
- Product/cart internal scrolling does not move the full page during normal laptop-height usage.
- Complete Checkout remains in the payment column when cart/payment are valid.

### 3.6 Sales History detail

Before:

- Sale detail rendered inline below the list, causing layout movement.
- Existing official lifecycle buttons were present in the detail area.

After:

- Sale rows support row click and an explicit `View` button.
- Detail opens in a separated read-only dialog.
- The detail dialog exposes no reprint, void, refund, printer, official output, RMO, BIR, or eSales action.

### 3.7 Stock Movements usability

Before:

- Inventory page title was broad.
- Movement type was free text.
- Movement table and filters could move with page content.

After:

- Page is named `Stock Movements`.
- Movement type uses a dropdown of known movement names.
- Reference/reason are minimized to table evidence instead of open-ended filters.
- Low-stock and movement ledger panels use stable bounded areas.

---

## 4. Receipt regression note

POS-UX0 does not change POS-M1 receipt snapshot, renderer, file writer, or checkout backend integration.

Manual proof source provided by Kirch:

```text
Receipt: POS-1783869860-F505DED8-000002
File: C:\Users\kirch\Salryn\.salryn-dev\receipts\POS-1783869860-F505DED8-000002-5a97203a0c8f45c5ae7aaf35c794e082.txt
```

Receipt regression still requires local/browser proof after this UX branch is pulled.

---

## 5. Gates

Required commands:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY
git reset --hard origin/KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Connector result:

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke` | `BLOCKED` — local shell unavailable. |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt` | `BLOCKED` — local shell unavailable. |
| `npm --prefix src\Salryn.App.Web run build` | `BLOCKED` — local shell unavailable. |
| `git diff --check` | `BLOCKED` — local shell unavailable. |
| `git status` | `BLOCKED` — local worktree unavailable. |

---

## 6. Manual proof checklist

Run after local build/web build passes:

- [ ] Checkout usable on laptop screen without normal sale scrolling.
- [ ] Shift closed locks workstation.
- [ ] Shift open unlocks workstation.
- [ ] Search/add/cart/payment/complete checkout visible and usable.
- [ ] Receipt file still created after checkout.
- [ ] Product Catalog search does not move whole page.
- [ ] Sales History detail opens cleanly in dialog.
- [ ] Stock Movements page is clearer and stable.
- [ ] No console errors.
- [ ] No backend exceptions.

---

## 7. Non-claims

This branch does not claim:

- GREEN
- ACCEPTED
- STORE_READY
- BETA_READY
- PRODUCTION_READY
- PRINTER_READY
- backend transaction repair
- checkout math change
- receipt renderer change
- receipt writer change
- barcode scanner support
- Fast Add support
- product write support
- stock-in/adjustment support
- void sale support
- reprint UI support
- installer readiness
- RMO/BIR/eSales readiness

---

## 8. Status

Status: `BLOCKED`

Reason: frontend-only repair was authored through the GitHub connector, but required local build, smoke, web build, git checks, and browser/manual proof could not be executed from this session.
