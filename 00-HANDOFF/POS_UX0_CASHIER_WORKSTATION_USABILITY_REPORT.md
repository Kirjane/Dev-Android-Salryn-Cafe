# POS-UX0 Cashier Workstation Usability Repair Report

Date: 2026-07-13  
Branch: `KIRCH-POS-UX0-CASHIER-WORKSTATION-USABILITY`  
Base branch: `KIRCH-POS-M1-RECEIPT-SNAPSHOT-FILE-RECEIPT`  
Base SHA: `365a6080f5c744422596180168d560f2cdbed4a9`  
Old repair SHA: `d93e27cda9df993127360a61cc1b5a77fb2961c9`  
Head before this report update: `65f2579d33da1fa978acece1bd45ee8416dd8983`

Authoring mode: GitHub connector-backed frontend-only repair. Local shell gates and manual browser proof were not executable from this session.

---

## 1. Scope

This repair is limited to the remaining Product Catalog movement defect from Kirch manual proof.

In scope:

- Product Catalog page shell height stability
- Product Catalog search/filter fixed area
- Product Catalog bounded results scrolling
- Product Catalog selected detail panel stability
- report update

Out of scope and not implemented:

- POS-M2
- backend checkout calculation changes
- receipt renderer changes
- receipt file writer changes
- printer integration or physical printing behavior
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

## 2. Files changed in this repair

- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `00-HANDOFF/POS_UX0_CASHIER_WORKSTATION_USABILITY_REPORT.md`

No checkout, receipt, backend, schema, printer, barcode, Fast Add, stock write, void, installer, RMO, BIR, or eSales files were changed.

---

## 3. Kirch manual proof result

Manual proof after SHA `d93e27cda9df993127360a61cc1b5a77fb2961c9`:

Passed:

- Review Checkout modal
- Confirm Sale before commit
- Sale Completed modal
- receipt file status shown
- F9 / Enter / F5 / Esc behavior
- Stock Movements date controls
- Sales History date controls
- sale completes
- receipt `.txt` created
- Sales History dialog
- no console/backend errors

Failed:

- Product Catalog search still moved the whole page when typing.

---

## 4. Exact Product Catalog fix

Before this repair:

- Product Catalog still used normal document flow around the page shell.
- The shell height was only bounded at `xl` breakpoint.
- Search/filter result state changes could still resize the page body on some laptop proof conditions.
- Selected product details were visually separate but the page could still move when the results panel changed.

After this repair:

- Product Catalog now uses a fixed bounded page shell:

```text
h-[calc(100vh-9rem)]
max-h-[calc(100vh-9rem)]
overflow-hidden
```

- The bounded shell applies generally, not only at `xl` width.
- The search/filter block is a `shrink-0` fixed area inside the shell.
- Result status uses a fixed-height header row instead of loading/error components that resize the body.
- Results are contained inside a `min-h-0 flex-1 overflow-y-auto overscroll-contain` panel.
- Pagination is a fixed-height footer inside the results panel.
- The selected product area is a fixed grid row on smaller widths and a side panel on `xl`, both with internal scroll.
- Selecting or clearing a product no longer appends a detail card below the page.
- Typing each search/filter character should update only internal content, not the page/body scroll height.

---

## 5. Checkout modal and receipt behavior untouched

This repair did not change:

- `CheckoutPage.tsx`
- two-stage review/confirm checkout behavior
- Sale Completed modal behavior
- keyboard shortcut behavior
- checkout API call shape
- checkout backend calculation
- POS-M1 receipt snapshot renderer
- POS-M1 receipt file writer
- receipt `.txt` creation behavior

Receipt regression remains dependent on local/manual proof because this session cannot run the application.

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

## 7. Gates

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

## 8. New manual proof checklist

Run after pulling this branch:

- [ ] Product Catalog remains on one bounded page shell at laptop height and 80-100% zoom.
- [ ] Typing each search character does not move the body/page scroll.
- [ ] Category, brand, and status filter changes do not move the body/page scroll.
- [ ] Product results scroll internally.
- [ ] Pagination stays fixed at the bottom of the results panel.
- [ ] Selected product details do not push the page down.
- [ ] Selected product details scroll internally when long.
- [ ] Product Catalog naming remains unchanged.
- [ ] No product create/edit/deactivate/Fast Add controls appear.
- [ ] Existing checkout review modal remains unchanged.
- [ ] Existing Sale Completed modal remains unchanged.
- [ ] Receipt file is still created after checkout.
- [ ] No console errors.
- [ ] No backend exceptions.

---

## 9. Non-claims

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

## 10. Status

Status: `BLOCKED`

Reason: frontend-only Product Catalog repair was authored through the GitHub connector, but required local build, smoke, web build, git checks, and browser/manual proof could not be executed from this session.
