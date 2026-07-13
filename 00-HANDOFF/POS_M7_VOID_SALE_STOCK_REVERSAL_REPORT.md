# POS-M7 Void Sale / Stock Reversal Report

Branch: `KIRCH-POS-M7-VOID-SALE-STOCK-REVERSAL`  
Base: POS-M6 final automated-proof SHA

## Feature

- Adds guarded `POST /api/sales/{saleId}/void` requiring `Sales.Void`.
- Requires a reason and only accepts completed sales.
- Reverses persisted `SALE` stock movements through atomic `ISaleWriteRepository.MarkVoidedAsync` using `VoidSale` movements.
- Preserves the original sale, payments, and receipt snapshot; rejects double void.
- Sales History exposes the action only to permissioned sessions, displays void evidence, and blocks normal reprint for voided sales.
- No refund/cash payout, partial return, official/BIR/RMO/eSales semantics, or direct stock SQL was added.

## Automated proof

`PosM7VoidSaleStockReversalChecks` creates a temporary initialized SQLite database, bootstraps an Admin session, writes a tracked product and completed sale, then proves sale stock reduction, reason validation, void status, atomic stock restoration, one reversal movement, unchanged payment/sale counts, immutable receipt snapshot, and double-void rejection.

## Gates

Build and focused POS-M7 behavior proof pass. The first full CoreSmoke run exposed a stale WEB-B0 classification that still treated the Sales History page as mutation-free. The narrow repair removes only that page from the legacy aggregate; the read-only sales API remains covered, and POS-M7 owns the guarded void UI and behavior proof.

Final results:

- build: PASS, 0 errors (3 existing nullable warnings).
- full CoreSmoke: PASS.
- targeted POS-M1 through POS-M7 CoreSmoke: PASS.
- web build: PASS (bundle-size advisory only).
- `git diff --check`: PASS.
- worktree is committed clean at milestone closeout.

## Manual proof

Deferred to Kirch: operator clarity and runtime walkthrough. No manual-proven claim.

## Status

`CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`
