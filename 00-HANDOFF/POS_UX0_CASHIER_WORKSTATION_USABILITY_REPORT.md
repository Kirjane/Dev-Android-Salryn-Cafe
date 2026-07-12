# POS-UX0 Cashier Workstation Usability Repair Report

Date: 2026-07-13  
Branch: `KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY`  
Base branch: `KIRCH-POS-M1-RECEIPT-SNAPSHOT-FILE-RECEIPT`  
Base SHA: `365a6080f5c744422596180168d560f2cdbed4a9`  
Old repair SHA: `002ecdf978f18b84171265ef6583f7ad5246a996`  
Head before this report update: `20bfd3223e8a4a50a1d67ff8f524858d28f2ffb9`

Authoring mode: GitHub connector-backed frontend-only repair. Local shell gates and manual browser proof were not executable from this session.

---

## 1. Scope

POS-UX0 repairs cashier workstation usability only.

In scope:

- checkout laptop-safe layout
- checkout shift-closed lock state
- product search/results bounded scroll areas
- two-stage checkout confirmation
- large sale-completed dialog
- safe keyboard shortcut hints
- Product Catalog stable results and side detail panel
- Stock Movements date controls and closed movement-type filter
- Sales History date controls and read-only detail dialog
- report file

Out of scope and not implemented:

- backend transaction rewrites
- checkout calculation changes
- receipt rendering changes
- receipt file writer changes
- printer integration
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

No backend, schema, receipt renderer, receipt writer, printer, RMO, BIR, eSales, or installer files were changed.

---

## 3. Original defect fixes

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
- Payment and checkout controls stay in the right column and remain visible during normal cashier flow.
- Extremely small viewports may still scroll normally.

### 3.2 Shift gate

Before:

- Checkout controls were still visually present when the shift was closed, even though backend validation could reject checkout.

After:

- Closed shift shows `Cashier workstation locked`.
- Product/cart/payment/checkout controls are hidden until the shift is open.
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

### 3.5 Sales History detail

Before:

- Sale detail rendered inline below the list, causing layout movement.
- Existing official lifecycle buttons were present in the detail area.

After:

- Sale rows have an explicit `View` action.
- Detail opens in a separated read-only dialog.
- The detail dialog exposes no reprint, void, refund, printer, official output, RMO, BIR, or eSales action.

---

## 4. Kirch manual proof repair findings

Kirch manual proof after POS-M1 receipt file success found these UX defects still present:

1. Product Catalog search still moved the whole page at laptop height / 80-100% zoom.
2. Sale completion was not obvious enough after checkout.
3. Checkout needed a review/confirm step so the sale does not commit on the first button press.
4. Keyboard guidance was missing for safe cashier actions.
5. Date filtering still used vague free-text controls in Stock Movements and Sales History.

---

## 5. POS-UX0 repair update

### 5.1 Product Catalog search stability

Repair:

- Product Catalog now uses a fixed large-screen shell height.
- Search/filter controls are in a fixed header section.
- Product results are inside a bounded `overflow-y-auto` panel.
- Selected product details stay in a side panel instead of rendering below the results.
- Typing search/filter text should no longer push the full page down during normal laptop-height use.

### 5.2 Sale completion modal

Repair:

- Successful checkout now opens a large `Sale Completed` dialog.
- Dialog shows:
  - receipt number
  - sale id
  - total
  - amount paid
  - change
  - receipt file status if the checkout response includes it
- Dialog includes a clear `New Sale (F5)` action.
- Dialog explicitly says physical printing is not available in this lane.
- No physical printer button was added.

### 5.3 Two-stage checkout confirmation

Repair:

- The payment panel primary action is now `Review Checkout`.
- `Review Checkout` opens a confirmation dialog.
- Confirmation dialog shows:
  - cart line count
  - item count
  - total
  - cash tendered
  - change
  - line summary
- Sale commits only after `Confirm Sale`.
- Insufficient cash remains blocked before the confirmation dialog can open.

### 5.4 Keyboard shortcut hints

Repair:

- Added safe hints only:
  - `F9 = Review Checkout`
  - `Enter = Confirm` when the confirmation dialog is open
  - `F5 = New Sale` after sale completed
  - `Esc = Close/cancel dialog`
- No barcode scanner behavior was added.
- No Fast Add behavior was added.

### 5.5 Date controls

Repair:

- Stock Movements now uses date inputs for from/to filters.
- Sales History now uses date inputs for from/to filters.
- Stock Movements movement type uses a dropdown instead of vague free text.
- Receipt number remains a free-text filter because it is an identifier search.

---

## 6. What remains intentionally parked

Still parked by instruction:

- POS-M2 printer adapter
- ESC/POS printer
- Windows printer
- LAN printer
- barcode scanner logic
- Fast Add Product
- product write backend
- stock-in/adjustment backend
- void sale
- reprint UI
- installer/package
- RMO/BIR/eSales
- Crystal runtime
- cloud upload

---

## 7. Receipt regression note

POS-UX0 does not change POS-M1 receipt snapshot, renderer, file writer, or checkout backend integration.

Manual proof source provided by Kirch:

```text
Receipt: POS-1783869860-F505DED8-000002
File: C:\Users\kirch\Salryn\.salryn-dev\receipts\POS-1783869860-F505DED8-000002-5a97203a0c8f45c5ae7aaf35c794e082.txt
```

Receipt regression still requires local/browser proof after this UX branch is pulled.

---

## 8. Gates

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

## 9. New manual checklist

Run after local build/web build passes:

- [ ] Product Catalog search/filter typing does not move the whole page at laptop height and 80-100% zoom.
- [ ] Product Catalog results remain inside the bounded scroll area.
- [ ] Selected product panel does not push results/page downward.
- [ ] Checkout `Review Checkout` opens a confirmation dialog.
- [ ] Insufficient cash blocks before confirmation.
- [ ] Confirm dialog shows cart count, total, cash tendered, and change.
- [ ] Sale commits only after `Confirm Sale`.
- [ ] Successful sale opens a large completion dialog.
- [ ] Completion dialog shows receipt number, sale id, total, amount paid, change, and receipt file status if available.
- [ ] `New Sale (F5)` resets the workstation after completion.
- [ ] F9 opens checkout review when ready.
- [ ] Enter confirms only when confirmation dialog is open.
- [ ] Esc closes/cancels open dialogs.
- [ ] Stock Movements date filters use date controls.
- [ ] Sales History date filters use date controls.
- [ ] Sales History detail opens cleanly in a read-only dialog.
- [ ] Receipt file is still created after checkout.
- [ ] No console errors.
- [ ] No backend exceptions.

---

## 10. Non-claims

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

## 11. Status

Status: `BLOCKED`

Reason: frontend-only repair was authored through the GitHub connector, but required local build, smoke, web build, git checks, and browser/manual proof could not be executed from this session.
