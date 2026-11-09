# KIRION FORGE — ARGON LPI R2 REPAIR & CLOSEOUT REPORT

**Mission:** `ARGON-SALRYN-LPI-PR26-FINANCIAL-STOCK-INTEGRITY-REPAIR-R2`  
**Role:** Single-Writer Backend and Frontend Integration Maintainer (Argon)  
**Technical Authority:** Kirch Ivan Balite, Engineer  
**Product:** Base Salryn Retail POS — Logistics, Procurement & Inventory Management  
**Repository:** `Kirjane/Salryn`  
**Pull Request:** [#26](https://github.com/Kirjane/Salryn/pull/26)  
**Authoritative Review Reference:** [PR #26 Review Comment 6745663160](https://github.com/Kirjane/Salryn/pull/26#issuecomment-6745663160)  
**Worktree:** `C:\Salryn-Argon-LPI`  
**Branch:** `antigravity/argon-salryn-procure-to-pay-r1`  
**Baseline Head:** `c538bcf29c8679fe282af930afcd18a225b2c198`  
**Timestamp:** November 10, 2026  

---

## 1. Executive Summary

In response to the authoritative review on PR #26, this R2 delivery reproduces, repairs, and verifies all six P1 defects (P1-01 through P1-06). The repair pass restores complete financial dual control, strict cryptographic request idempotency, cross-POS stock bucket atomicity, explicit supervisory exception governance, and accurate alignment with the 20 slides of the development brief (`SALRYN_Logistics_Procurement_Inventory_Development_Brief.pptx`).

All six P1 defects were reproduced in automated regression tests (`LpiP1IntegrityReproductionChecks.cs`) before their closures were verified. All reproduction assertions passed with exit code 0. Full visual evidence of updated financial approval workflows and overdelivery governance was re-captured into `00-HANDOFF/evidence/lpi/`.

---

## 2. P1 Findings: Reproduction, Root Cause, Remediation & Verification

### P1-01: Three-Way Matching Integrity

- **Defect Description:** In R1, `SqliteProcureToPayService.Payables.cs` allowed supplier invoices without genuine purchase order or receiving evidence to automatically receive `Matched` status and post to Accounts Payable. There was no cumulative unbilled quantity tracking, allowing duplicate invoicing of the same received items.
- **Reproduction Test:** `TestP101ThreeWayMatchingIntegrityAsync`
  - Created source-free invoice (`MissingPO` / no RR); verified it previously auto-matched.
  - Submitted invoice with mismatched supplier/PO IDs; verified cross-document leakage.
  - Invoiced receiving report twice for full quantity; verified duplicate billing occurred.
- **Root Cause:** Invoice matching evaluation evaluated only `TotalAmountCents == PoTotalAmountCents` without verifying line-level purchase order items, receiving report item links, or cumulative previously invoiced quantities across the PO/RR lines.
- **Remediation Implemented:**
  - `SqliteProcureToPayService.Payables.cs`: Enforced multi-point 3-way matching rules:
    - Invoices without PO and RR links receive status `PendingDocuments` and **never** post to Accounts Payable or ledger balances.
    - Verified strict supplier identity match across Invoice, PO, and Receiving Report (`Invoice.SupplierId == PO.SupplierId == RR.SupplierId`).
    - Enforced PO line matching: unit cost differences trigger `PriceVariance`; received vs invoiced quantity differences trigger `QuantityVariance`.
    - Added cumulative unbilled quantity verification: `Sum(accepted_qty) - Sum(billed_qty)`. Invoicing beyond unbilled received quantity triggers `Overbilled` and aborts AP posting.
- **Verification Evidence:**
  - `TestP101ThreeWayMatchingIntegrityAsync`: PASS.
  - Source-free invoice remains in `PendingDocuments` with zero AP posting.
  - Cross-supplier and cross-PO references rejected with deterministic validation errors.
  - Duplicate invoicing blocked with code `Overbilled`.

---

### P1-02: Payment Authorization & Separation of Duties

- **Defect Description:** In R1, `LocalProcureToPayEndpoints.cs` and `SqliteProcureToPayService.Payables.cs` allowed the recording operator to simultaneously approve their own supplier payment (`approvedByUserId = actorUserId`). Payments posted immediately as `Completed` without supervisory review.
- **Reproduction Test:** `TestP102PaymentAuthorizationAndSeparationOfDutiesAsync`
  - Submitted supplier payment; verified payment recorded and self-approved in one step.
  - Attempted payment recording with a non-manager role; verified bypass occurred.
- **Root Cause:** Backend endpoints lacked dual-control workflow and assigned the caller ID to both `paid_by_user_id` and `approved_by_user_id`.
- **Remediation Implemented:**
  - Added payment lifecycle status model: `SupplierPaymentStatuses.PendingApproval` and `SupplierPaymentStatuses.Approved`.
  - Added dedicated endpoint `POST /api/lpi/payments/{id}/approve` guarded by permission `Payables.ApprovePayment`.
  - Enforced strict backend separation of duties: if `ApproverUserId == RecordedByUserId`, the service rejects with `ProcureToPay.SelfApprovalForbidden`.
  - Recorded payments remain in `PendingApproval` and do not deduct from the supplier invoice balance until supervisory approval occurs.
  - Proved cash drawer isolation: Supplier payments do **not** write to `cash_drawer_events`, mutate cashier shift floats, or touch `accounts_receivable_entries` (U1).
- **Verification Evidence:**
  - `TestP102PaymentAuthorizationAndSeparationOfDutiesAsync`: PASS.
  - Self-approval attempt returned `SelfApprovalForbidden`.
  - Independent manager approval transitioned payment to `Approved` and deducted invoice balance.
  - Zero cash drawer events or customer receivables entries generated.

---

### P1-03: Financial Data Authorization Matrix

- **Defect Description:** Financial AP endpoints were accessible under coarse inventory or report viewing permissions, exposing supplier payables ledger, payment histories, and aging analytics to warehouse operators without financial clearance.
- **Reproduction Test:** `TestP103FinancialDataAuthorizationAsync`
  - Sent requests with Anonymous, `Inventory.View`, and `Reports.View` tokens to `/api/lpi/invoices`, `/api/lpi/payments`, `/api/lpi/payables/ledger`, and `/api/lpi/payables/aging`.
  - Verified non-financial roles previously accessed supplier payment records and AP aging details.
- **Root Cause:** Endpoint authorization decorators fell back to `Inventory.View` or omitted HTTP-level permission checks for read routes.
- **Remediation Implemented:**
  - Guarded all AP routes (`/api/lpi/invoices`, `/api/lpi/payments`, `/api/lpi/payables/ledger`, `/api/lpi/payables/aging`) strictly with `LocalApiPermissions.PayablesView`.
  - On the overview dashboard (`/api/lpi/overview`), if the authenticated user lacks `PayablesView`, financial metrics (`TotalAccountsPayableCents`, `OverduePayablesCents`, `PendingPaymentsCount`) are redacted to 0.
- **Verification Matrix:**

| Role / Permissions | Route | Status Code | Result |
| :--- | :--- | :--- | :--- |
| Anonymous | `GET /api/lpi/invoices` | `401 Unauthorized` | PASS |
| Anonymous | `GET /api/lpi/payments` | `401 Unauthorized` | PASS |
| Anonymous | `GET /api/lpi/payables/ledger` | `401 Unauthorized` | PASS |
| Anonymous | `GET /api/lpi/payables/aging` | `401 Unauthorized` | PASS |
| `Inventory.View` only | `GET /api/lpi/invoices` | `403 Forbidden` | PASS |
| `Inventory.View` only | `GET /api/lpi/payments` | `403 Forbidden` | PASS |
| `Inventory.View` only | `GET /api/lpi/payables/ledger` | `403 Forbidden` | PASS |
| `Inventory.View` only | `GET /api/lpi/payables/aging` | `403 Forbidden` | PASS |
| `Reports.View` only | `GET /api/lpi/invoices` | `403 Forbidden` | PASS |
| `Reports.View` only | `GET /api/lpi/payments` | `403 Forbidden` | PASS |
| `Reports.View` only | `GET /api/lpi/payables/ledger` | `403 Forbidden` | PASS |
| `Reports.View` only | `GET /api/lpi/payables/aging` | `403 Forbidden` | PASS |
| `Payables.View` | All AP endpoints | `200 OK` | PASS |

---

### P1-04: Transaction Idempotency & SHA-256 Payload Hash Binding

- **Defect Description:** In R1, client request IDs were tracked superficially without binding to the request payload. Replays with altered amounts or changed payment destinations could corrupt records or be silently ignored.
- **Reproduction Test:** `TestP104TransactionIdempotencyAsync`
  - Sent payment request with RequestId `R-1` and amount 5,000 cents.
  - Replayed request with RequestId `R-1` and altered amount 9,000 cents.
  - Verified whether altered payload was rejected or erroneously accepted.
- **Root Cause:** Table schemas tracked only `client_request_id` text without storing or verifying a canonical payload hash.
- **Remediation Implemented:**
  - Added `payload_hash` (TEXT, SHA-256) column to `supplier_payments` and `receiving_reports` in migration v27.
  - Normalized payload parameters (IDs, amounts, line quantities) into a deterministic canonical string and computed SHA-256 hex digest.
  - Enforced behavior:
    - Same request ID + identical hash: Return original transaction without duplicating stock movements or financial ledger entries.
    - Same request ID + altered hash: Reject with `409 Conflict` (`Lpi.Payment.IdempotencyConflict` / `Lpi.Receiving.IdempotencyConflict`).
- **Verification Evidence:**
  - `TestP104TransactionIdempotencyAsync`: PASS. Replay returned original payment; altered payload rejected with deterministic conflict.

---

### P1-05: Inventory Consistency Across All POS Stock Writers

- **Defect Description:** In R1, `product_stock_buckets` operated as a siloed inventory subsystem. POS checkout sales, sales voids, legacy Stock-In, and legacy stock adjustments did not update `product_stock_buckets`, creating desynchronization between product stock on hand and location stock buckets. Non-resale operating supplies also leaked into sellable stock.
- **Reproduction Test:** `TestP105InventoryConsistencyAcrossPosAsync`
  - Reconciled stock-on-hand vs `product_stock_buckets` across 7 sequential POS lifecycle operations:
    1. Migration v27 backfill of pre-existing legacy product stock into `MAIN-STORE` `Available` bucket.
    2. Receiving of resale inventory vs non-resale store supplies.
    3. POS checkout sale reducing stock.
    4. POS sale void restoring stock.
    5. Legacy Stock-In increasing stock.
    6. Legacy Stock Adjustment removing stock.
    7. Physical count adjustment.
- **Root Cause:** POS write repositories (`SqliteSaleWriteRepository.cs`, `SqliteInventoryWriteRepository.cs`) executed before LPI and lacked hooks to update `product_stock_buckets`. Non-resale purchase items had `ProductId` linked without filtering resale flags.
- **Remediation Implemented:**
  - **Migration v27 Backfill:** Automatically initialized `product_stock_buckets` for all pre-existing active products into `MAIN-STORE` `Available` with initial `stock_on_hand_x1000`.
  - **StockBucketSyncHelper:** Created centralized atomic bucket updater:
    - Hooked into `SqliteSaleWriteRepository.AddCompletedSaleAsync` (reduces `Available` bucket in `MAIN-STORE`).
    - Hooked into `SqliteSaleWriteRepository.MarkVoidedAsync` (restores `Available` bucket in `MAIN-STORE`).
    - Hooked into `SqliteInventoryWriteRepository.AddStockInMovementAsync` (increments `Available` bucket).
    - Hooked into `SqliteInventoryWriteRepository.AddStockAdjustmentMovementAsync` (adjusts `Available` bucket).
  - **Resale Boundary Guard:** Non-resale purchases (supplies, equipment) are routed to store expense accounts and do not update sellable `MAIN-STORE` stock buckets.
- **Verification Evidence:**
  - `TestP105InventoryConsistencyAcrossPosAsync`: PASS across all 7 stages. Product stock-on-hand exactly equaled `MAIN-STORE` bucket quantity after every transaction.

---

### P1-06: Overdelivery Approval Authority & Dual Control

- **Defect Description:** In R1, an operator possessing multiple roles could implicitly approve an overdelivered receiving report during submission, bypassing independent supervisory review.
- **Reproduction Test:** `TestP106OverdeliveryApprovalAuthorityAsync`
  - Created receiving report with 12 units delivered against 10 units ordered.
  - Attempted posting without approval; attempted receiver self-approval.
- **Root Cause:** Receiving report creation permitted auto-posting when the submitting user held supervisory permissions, collapsing separation of duties.
- **Remediation Implemented:**
  - Migration v27 created dedicated audit table: `lpi_overdelivery_approvals`.
  - Unapproved overdeliveries are flagged `HasOverdelivery = true` and placed into `PendingExceptionApproval` status.
  - Added dedicated endpoint `POST /api/lpi/receiving-reports/{id}/approve-overdelivery`.
  - Required independent supervisor identity (`approver_user_id != received_by_user_id`), mandatory reason, and frozen snapshot of approved line quantities (`approved_lines_json`).
  - Posting blocks until an active, unrevoked record in `lpi_overdelivery_approvals` matches the exact receiving report line quantities.
- **Verification Evidence:**
  - `TestP106OverdeliveryApprovalAuthorityAsync`: PASS.
  - Receiver self-approval blocked with `SelfApprovalForbidden`.
  - Independent supervisor approval recorded in `lpi_overdelivery_approvals`.
  - Subsequent posting succeeded and updated PO line quantities and stock buckets atomically.

---

## 3. Reconciled Specification Traceability (20 Slides)

Authoritative Document: `SALRYN_Logistics_Procurement_Inventory_Development_Brief.pptx` (Issue #25)

| Slide # | Specification Area | Implemented Source / Capabilities | Verification Status | Outstanding Policy / Scope Gaps |
| :---: | :--- | :--- | :---: | :--- |
| **1** | Title & Architecture | Logistics, Procurement & Inventory Management module for Salryn POS | Complete | None |
| **2** | Business Objective | Modern retail supply chain: PR -> RFQ -> PO -> RR -> AP -> Inventory | Complete | None |
| **3** | Core Workflow Loop | Complete closed-loop procure-to-pay workflow with audit trail | Verified | None |
| **4** | Purchase Requisitions | PR drafting, item lines, estimated costs, departmental approval | Verified | None |
| **5** | Supplier Management & RFQ | S0-S3 supplier onboarding, RFQ creation, quotation comparison matrix | Verified | None |
| **6** | Purchase Orders | PO generation from PR/RFQ, supplier binding, cancellation controls | Verified | None |
| **7** | Goods Receiving & Inspection | RR creation, accepted vs rejected counts, quarantine location routing | Verified | None |
| **8** | Overdelivery Governance | P1-06 explicit supervisory approval in `lpi_overdelivery_approvals` | Verified | None |
| **9** | Stock Cards & Location Buckets | Multi-bucket tracking (`Available`, `Quarantine`, `Damaged`), POS sync | Verified | Multi-warehouse transfer UI deferred |
| **10** | Supplier Invoicing & 3-Way Match | P1-01 Line-level 3-way matching, tolerance, overbilling prevention | Verified | Automated OCR invoice scan deferred |
| **11** | Accounts Payable Ledger | Supplier AP entries, running balances, credit terms, aging analysis | Verified | None |
| **12** | Payment Settlement Controls | P1-02 Dual-control payment recording & supervisor approval | Verified | External bank gateway API deferred |
| **13** | Inventory Valuation & Margin | FIFO / Moving average unit cost tracking, POS margin preservation | Verified | Landed cost duty allocation deferred |
| **14** | Supplier Returns (Debit Memo) | Return to vendor for damaged/rejected goods, AP debit deduction | Verified | Supplier credit memo refund check |
| **15** | Stock Adjustments & Physical Count | Cycle counting, discrepancy reconciliation, write-off approvals | Verified | Barcode terminal batch counting |
| **16** | Permissions & Role Security | P1-03 Strict HTTP permission matrix, dual-control approval guards | Verified | None |
| **17** | Idempotency & Concurrency | P1-04 Request ID + SHA-256 payload hash binding, conflict rejection | Verified | None |
| **18** | POS & W4 Reconciliation Isolation | Zero interference with cash drawer floats, receipts, or U1 utang | Verified | None |
| **19** | Definition of Done (DoD) | Unit tests, CoreSmoke integration, visual evidence, audit logs | Verified | None |
| **20** | Operational Runbook | Database migration v27, seed catalog, backup & restore protocol | Verified | None |

---

## 4. Test Matrix & Verification Evidence

All test executions performed in worktree `C:\Salryn-Argon-LPI` on branch `antigravity/argon-salryn-procure-to-pay-r1`:

| Suite / Check Name | Scope | Result | Exit Code |
| :--- | :--- | :---: | :---: |
| `dotnet build Salryn.sln` | Full solution compilation | **PASS** | 0 |
| `lpi-p1-integrity` | P1-01 through P1-06 reproduction & regression suite | **PASS** | 0 |
| `lpi` | LPI Core runtime: S0-S3 suppliers, PR, PO, RR, AP, 3-way match, backup | **PASS** | 0 |
| `lpi-evidence` | Automated CDP/Puppeteer screenshot capture across 8 views | **PASS** | 0 |
| `npm run build` | Frontend TypeScript check & Vite production bundle compilation | **PASS** | 0 |

### Screenshot Evidence Captured (`00-HANDOFF/evidence/lpi/`)

1. `01_lpi_overview_dashboard.png`: Overview dashboard with redacted metrics for unauthorized users.
2. `02_lpi_supplier_directory.png`: Supplier registry with scorecards and payment terms.
3. `03_lpi_purchase_requisitions.png`: Purchase requisition workflow with approval states.
4. `04_lpi_quotations_comparison.png`: RFQ quotation comparison and winning quote selection.
5. `05_lpi_purchase_orders.png`: Purchase order tracking and line receipt progress.
6. `06_lpi_goods_receiving.png`: Goods receiving report with overdelivery approval modal.
7. `07_lpi_inventory_stock_cards.png`: Product stock cards and location bucket distributions.
8. `08_lpi_ap_3way_matching.png`: AP 3-way matching table with dual-control payment approvals.

---

## 5. Merge Conflict Ledger Against Integration Branch

PR #26 maintains strict worktree isolation from `integration/salryn-w4-premium-ux-r1`. The following files contain integration intersections to be reconciled during final merge staging after Codex candidate sealing:

| File | PR #26 Modification | Integration Branch Purpose | Resolution Strategy |
| :--- | :--- | :--- | :--- |
| `src/Salryn.Infrastructure/Data/SalrynDatabaseMigrationCatalog.cs` | Registers migration v27 (`ProcureToPaySchemaMigration`) | Registers v26 and related migrations | Union: append v27 after latest migration in catalog |
| `src/Salryn.Infrastructure/Data/Seeding/DefaultSeedData.cs` | Adds LPI permissions and test users | Adds POS/W4 role permissions | Union: include both permission sets |
| `src/Salryn.App.Web/src/navigation.ts` | Adds LPI navigation item and permissions | Adds W4 Cash Drawer / Session nav | Union: maintain both navigation entries |
| `src/Salryn.Api.Local/LocalApiApplicationFactory.cs` | Maps LPI endpoints | Maps W4 / Lan Terminal endpoints | Union: keep both endpoint route mappings |

---

## 6. Unresolved Policy Decisions (Pending Technical Authority Alignment)

1. **Overdelivery Tolerance Margin:** Currently, any delivery quantity > 100% of PO ordered quantity requires explicit supervisory approval. Should an automatic tolerance window (e.g., ±2% for bulk items) be supported without supervisor signoff?
2. **Payment Money Source Integration:** Supplier payments in R2 remain purely Accounts Payable ledger records (`supplier_payable_entries`) and do not deduct from the POS cashier cash drawer. When a real cash payout or corporate bank source is authorized, should a separate corporate disbursement voucher workflow be introduced?
3. **Quarantine Write-Off Authority:** Damaged or rejected goods routed to the Quarantine bucket can be returned to vendor via Debit Memo. If goods are condemned on site, should an independent scrap/write-off approval event be required?

---

## 7. R2 Delivery Status

PR #26 has completed all required P1 corrections, regression validations, visual evidence updates, and development brief reconciliations.

**PR #26 Status:** DRAFT — Ready for Independent R2 Source Review.
