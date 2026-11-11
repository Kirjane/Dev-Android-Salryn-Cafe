---
name: salryn-deadline-delivery
description: Canonical end-to-end CEO Paul Salryn School Supplies delivery mission; all business requirements in scope, one Antigravity worker using Forge self-review and live acceptance to deliver functional Windows POS.
---

# Salryn CEO Delivery — Complete Business Scope and Acceptance Matrix

**One mission; one writer (Antigravity); one candidate; one installable deliverable.** Read root `AGENTS.md` and `.agents/skills/salryn-forge-cycle/SKILL.md` first. Do not start another activation or assign work to Codex.

## Actual evidence and source baseline

- The CEO's `ZAY OFFICE AND SCHOOL SUPPLIES` screenshot showing **Generate PO -> HTTP 400** is from the **older Codex-installed version**. It is reported evidence on that build, **not proof R2.1 still has the bug**. Reproduce with approved PR, supplier and line allocations on the present source, log exact response; correct if extant.
- Antigravity's R2.1 installed screenshot is a different `KIRJANE IT SOLUTIONS` QA store. The verified source is draft PR #32, SHA `d07f73a21c2832257d48f5023d01047149e82003`, branch `antigravity/w5-contract-reconciliation-r21`. Earlier QA reports verified navigation and license but **not** this entire new CEO business specification.
- Existing UI/source: `src/Salryn.App.Web/src/navigation.ts`, `security/permissions.ts`, `pages/ProcureToPayPage.tsx`, `pages/CheckoutPage.tsx`, `pages/OwnerReportsPage.tsx`; API `src/Salryn.App.Web/src/api/procureToPay.ts`, `ownerReports.ts`; Local API `src/Salryn.Api.Local/LocalProcureToPayEndpoints.cs`, `LocalOwnerReportsEndpoints.cs`; service `src/Salryn.Infrastructure/Data/Services/SqliteProcureToPayService.*.cs`; roles `src/Salryn.Infrastructure/Data/Seeding/DefaultSeedData.cs`; `tests/Salryn.Tests/LpiProcureToPayRuntimeChecks.cs`. Check current source before editing.
- Verified source conflicts: `navigation.ts` still says `Utang Ledger`; cashier `Procurement.Request` is seeded in `DefaultSeedData.cs` but `canAccessPage()` restricts cashier to checkout/sales; PR self-approval is blocked in `SqliteProcureToPayService.Procurement.cs` and frontend; P2P is a single manager workspace, and quotation/PO/receiving/AP tabs already exist.
- Existing tests available: `e2e/integrated-host-lpi-acceptance.spec.ts`, `e2e/w5-lpi-runtime-regressions.spec.ts`, `e2e/jane-checkout-workstation.spec.ts`; use them, expand only with required new journeys. Report actual commands and results.

## Delivery objective and scheduling

**CEO's complete requested scope stays active.** Deliver working behavior, not a provisional dashboard with disabled buttons. Optimize the work by **dependency order and shared model changes**: roles/navigation and source identities -> PR/quotes/PO -> RR/stock/returns -> AP -> checkout tender -> customer quotation/order -> dashboard/reports -> Windows acceptance. Execute Forge `implement -> self-review -> test -> bounded rework -> retest -> accept` after each integrated slice.

Four hours is the product delivery target; seven hours is the client visit. Build the highest-coverage candidate with real functionality. Do not preemptively label new features `DEFERRED`. At any cutoff report each unmet requirement transparently; do not hide it, fake PASS or weaken business integrity to satisfy the clock.

## CEO acceptance scenarios (all in scope)

### C01 — Manager/admin/cashier information architecture
- **Manager navigation:** Dashboard; Accounts Receivables; Accounts Payable (**separate sidebar entry/screen, not just last tab**); Reports; Inventory; Stock Movements; Logistics, Procurement & Inventory. Clearly organized, no duplicate misleading entries. Admin appropriate additional system settings.
- **Cashier navigation:** POS Checkout, Sales & Receipts, Logistics/Procurement limited to **Purchase Request, price quotation and supplier assignment**. Hide and server-deny receiving, stock count/returns, AP, treasury and administrative actions. All authenticated end users can submit PR, with correct entitlement enforcement. Verify direct API negative tests.
- Rename visible `Utang` label to **Accounts Receivables** in manager/AR presentation, while preserving legacy database/API keys and existing receivables behavior. For every AR request/credit request or originating credit-sale record, display the actual request/transaction date and the responsible operator's verified full display name (and customer identity where applicable); do not silently substitute login username as a complete name. For credit tender, use clear customer-readable `Credit / Accounts Receivable` terminology.

### C02 — Purchase Requisition (PR)
- Any authenticated end user can request; persist **full legal/display name** as actual user attribution, timestamp/local display, department, required date, items, qty, estimated costs and reason. PR number unique, ordered and monotonically sequential under concurrency.
- Draft/submit/review/approve/reject; admin or manager may **self-confirm their own PR** per CEO's stated exception. Record self-confirm policy/audit event, approver ID/full name/time, old/new status; do not claim independent approval. Cashier can request but does not gain full approval powers.
- Generate accurate **print/preview** with PR #, dates, requester, item lines, totals and approved-by **printed name**/approval timestamp derived from persisted audit. No fake signature drawing. Actual handwritten/e-signature method requires a separate explicit implementation if demanded, not fabricated imagery.
- Acceptance: cashier PR -> manager approval -> number + approver identity on print -> persistence after restart; manager/admin own PR self-confirm audit; cashier unauthorized approval denied.

### C03 — Supplier assignment, quotations, PR review, PO
- Approved PR visible for procurement and supplier assignment; allow multiple supplier offers and line-level price, qty, lead time, terms, validity and extended totals. Quote comparisons correctly match `RequisitionLineId` even for repeated products.
- Approved PR -> **Generate PO** for chosen supplier(s) with allocation and requested balance checks. No unexplained HTTP 400; present validation messages for no supplier, unquoted line or invalid split. Prevent duplicate orders and over-allocation.
- Purchase Order gets its **own unique continuous PO number**, supplier, linked PR, approved by/date, line totals and terms. Must have usable screen preview/print for supplier and status lifecycle (draft/approved/sent/partially received/completed).
- Test one supplier and split-by-supplier PO, wrong-role denial, double-submit, exact amounts, restart. Compare printed PO to database row.

### C04 — Receiving, delivery receipt, stock and supplier returns
- Against sent/approved PO, capture original supplier delivery receipt number, delivered vs ordered, accepted/rejected/short/over quantities, receiving operator/full name and date, target location/bucket. Generate **Receiving Report (RR)** on screen and print, tied to PO/supplier/DR with accurate lines and counts.
- Stock changes **only at authorized posting/commit**, once. Validate accepted remaining, quarantine/damaged buckets, location-specific movements, stock valuation missing-cost disclosure. Repeat receipt and overdelivery exceptions cannot silently inflate stock.
- Supplier returns linked to posted RR line; support partial returns, cumulative ceilings and correct stock/debit memo/ledger effects. Ensure rejected/nonaccepted goods do not become sellable stock.
- Payment terms/due date visible and correctly handed to invoice/AP. Test partial receiving, repeat/idempotent retry, return twice within limit, over-return denial, reconciliation and restart.

### C05 — Accounts Payable + treasury
- Dedicated AP navigation with supplier invoice intake, PO/RR **three-way match**, approved variance, payable posting, aging, payment terms, due date and settlement.
- Treasury/disbursement workflow: authorized cash/bank/cheque record, payment reference, partial/full amounts, actual payment date and approval. Provide cheque preview/print using actual bank/beneficiary/amount/date/reference fields; printing a cheque does not itself mark a payment settled, and any bank-side clearance must not be invented. Supplier AP cash payments must **not** debit POS shift drawer. No direct cashier-only AP permissions.
- Preserve AP amount outstanding, returns/debit memos and audit chain across restart. Make source-free/unmatched documents explicit instead of pretending matched/paid.
- Test happy path, partial, overpayment rejection, wrong-role/approval denial, duplicate payment attempt and correct AP aging.

### C06 — Multimodal customer checkout and split tender
- Offer customer tender methods **Cash, GCash, Maya, Debit Card, Credit / Accounts Receivable**, and **mixed payment** across compatible tenders. CEO called this multimodal; avoid confusing customer AR credit with external credit-card authorization.
- Define tender allocation per transaction in integer cents with references for external modes when required, balance = sale total, valid change from cash only, partial/full credit record, returns/void reversals and receipt breakdown.
- If no live gateway exists, implement an explicitly **recorded externally confirmed payment** workflow (not a fabricated integration/settlement). Pending or unverified transfers must not be reported as bank-confirmed. Card handling must not store PAN/CVV.
- Protect shift reconciliation: drawer changes only for actual cash; GCash/Maya/debit and AR remain separate. Test each tender, mixed tender with cash + digital + AR, insufficient/over allocation, failed confirmation, receipt, void and restart.

### C07 — Cashier price lookup and customer canvassing
- Fast product/price lookup, multi-item customer **quotation/canvass** workspace with quantities, prices, optional customer/expiry, subtotals and printable/shareable **QUOTATION / NOT A PAID RECEIPT** document.
- No cash shift, sale, stock deduction, payment or official receipt number from viewing/printing a quotation. Optional quote-to-checkout conversion must revalidate current prices/stock and create one real sale only on actual paid checkout.
- Test quote create/edit/print/reopen, price lookup, no financial mutation, and explicit optional conversion without duplicate sale.

### C08 — Client orders workspace
- Manager/admin can record customer-initiated orders: customer identity/contact (only fields needed), items, promised date, estimated/confirmed totals, deposit status if supported, order state and responsible operator.
- Distinguish order from POS completed sale and PR to suppliers. Reserved/fulfilled stock behavior must be explicit and transactional; never decrement inventory just for a draft. Convert/fulfil once to appropriate checkout/sale without double billing.
- Test creation, edit, cancellation, fulfilment, availability, permissions, audit and restart. Show meaningful state and clear linkage.

### C09 — Dashboard, Reports and analytics presentation
- Improve manager Dashboard information density/hierarchy using *real* sales, cash received, AR, AP, inventory/low-stock and operations signals. Distinguish missing/unavailable from zero.
- Reports screen: genuine date-filtered chart visualizations (sales trend, tender mix, top products, AR aging, AP aging and inventory where actual data exists), readable KPIs, export and drilldowns; no repeated giant empty cards/progress-list facsimiles.
- Use existing owner reports APIs in `LocalOwnerReportsEndpoints.cs`/`ownerReports.ts`; expand only if necessary for accurate endpoints. Historical unit cost missing means `coverage incomplete`, not fabricated margin. Test chart values against SQLite totals/voids/collections, empty and failed queries, desktop/WebView2 sizes.
- Preserve Jane's compact Checkout layout and keyboard/shift controls without replacing it with old UI.

### C10 — Client-requested formats discovery (nonblocking input)
- Client may supply preferred PR/PO/RR/quotation paper formats later. **Do not block core printable documents awaiting templates**: use clean clearly labeled MSME business forms with truthful identifiers/approval and adjustable layout. Never claim BIR/tax certification from style alone.

### C11 — Windows install, license, data and client handover
- One committed candidate branch and PR, repeatable installer from exact source SHA. Existing installed QA build `TEST QA PAUL / KIRJANE IT SOLUTIONS` does not equal client target.
- Test the full Windows app including signed machine-bound license, user sign-in, all role menus, Jane checkout, customer quote, tender modes, C02-C05 complete P2P, client orders, charts, print/preview, backup/restart and data preservation.
- Synthetic/isolated local business writes only; customer business DB read-only for smoke until authorized deployment. Backup SQLite WAL-consistently + license and prove recovery before in-place install. Compare before/after records and hashes; never reset client accounts or infer passwords.
- Report target store identity/version, Git commit, installer SHA-256, built binaries, accepted checklist, screenshots, authentic UI→HTTP→SQLite traces, blocker and rollback.

## Definition of DONE
A requirement is DONE when **the visible workflow is usable, actual API/service/persistence agrees with it, permissions and invalid paths are verified, amounts/stock/IDs reconcile, and restarting retains the correct state**. A mock-only test, screenshot of a tab, successful build or `/health` response is **not** complete feature acceptance. Evaluate each C01–C11 separately.

## Required delivery response
- Completed requirements with test evidence and source SHA; partial and failed requirements explicitly named.
- Test exit codes, positive/negative business journey outcomes, accounting/stock before/after, print screenshots and Windows WebView2 proof.
- Exact candidate Git branch/commit, draft PR, installer hash, actual installed test target, DB/license preservation, rollback readiness.
- State: `ACCEPTED_INTEGRATED`, `ACCEPTED_WINDOWS_QA`, `DELIVERED_CLIENT`, `PARTIAL_WITH_GAPS` or `FIX_REQUIRED`; never claim `DELIVERED_CLIENT` from a QA store screenshot.

Execute. Do not generate another multi-agent plan or stop after compiling.
