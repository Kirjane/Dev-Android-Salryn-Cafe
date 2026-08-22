# RMO-B1 Official Checkout Data-Integrity Current-State Seal — Salryn

Status: documentation and guard seal only.

## Source Stamp

| Field | Value |
|---|---|
| Seal batch | RMO-B1 (DATA-A1) |
| Branch | `KIRCH-FREEHAND` |
| RMO state study commit | `4458d2395075ea91736d0bdbca678ea72e8c6586` |
| Base runtime SHA (pre-study) | `1d509158b2964d040ddfba195817f1ba0d40ce03` |
| Sealed study reference | `00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/` |

## Scope

Single-machine official checkout data-integrity spine only.

Salryn has a repo-backed single-machine official checkout data-integrity spine that can be studied and guarded. This seal records the current internal engineering state for that spine. It does not add runtime behavior.

This seal is not BIR certification, not BIR accreditation, not full RMO 24-2023 compliance, and not a printer or official-output certification.

## What Is Sealed

The following items are sealed only where current source and runtime smoke prove them.

### 1. Official checkout persistence service exists

- `SqliteOfficialCheckoutPersistenceService` orchestrates official checkout persistence.
- `CompleteOfficialSaleAsync` is the public entry point.

Evidence: `SqliteOfficialCheckoutPersistenceService`, `J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks`.

### 2. Shared SQLite transaction boundary

- `SqliteSharedTransactionBoundary` owns one SQLite connection and transaction per official checkout attempt.
- Successful delegated work commits; failed `Result` or thrown work rolls back.

Evidence: `SqliteSharedTransactionBoundary`, `J21Rmo242023SharedSqliteTransactionBoundaryImplementationChecks`.

### 3. Official sequence allocation inside the shared transaction

- `SharedSqliteOfficialDocumentSequenceAllocationParticipant` advances `bir_official_document_sequences.current_number` inside the caller-owned transaction.
- Allocation is gated by active accredited machine profile with official output enabled.

Evidence: `SharedSqliteOfficialDocumentSequenceAllocationParticipant`, `J21Rmo242023SharedTransactionOfficialSequenceAllocationParticipantChecks`.

### 4. Completed sale write inside the shared transaction

- `SharedSqliteCompletedSaleWriteParticipant` writes the completed sale through `SqliteSaleWriteRepository.WriteCompletedSale` using the shared connection and transaction.

Evidence: `SharedSqliteCompletedSaleWriteParticipant`, `SqliteSaleWriteRepository`.

### 5. Sale items, payments, and stock movement inside the sale write path

- `CompleteCheckoutSaleRequest` carries items, payments, and stock movements.
- The completed-sale participant passes all three collections into the shared sale write path.

Evidence: `SharedSqliteCompletedSaleWriteParticipant`, `J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks`.

### 6. Snapshot and journal contract evidence before journal append

- `OfficialDocumentSnapshotDraftBuilder` builds snapshot draft evidence from trusted persisted machine profile input.
- `OfficialDocumentJournalAppendContractBuilder` builds the journal append contract from the snapshot draft and allocated official document number.

Evidence: `OfficialDocumentSnapshotDraftBuilder`, `OfficialDocumentJournalAppendContractBuilder`, `SqliteOfficialCheckoutPersistenceService`.

### 7. ORIGINAL electronic journal append inside the same transaction

- `SqliteElectronicJournalAppendParticipant` inserts `bir_electronic_journal_entries` and an ORIGINAL `bir_electronic_journal_entry_events` row inside the shared transaction.

Evidence: `SqliteElectronicJournalAppendParticipant`, `J21Rmo242023SqliteElectronicJournalAppendParticipantInsertChecks`.

### 8. Runtime smoke commit path

`J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks` proves a successful official checkout:

- official sequence advances once
- `sales` row is inserted
- `sale_items` row is inserted
- `payments` row is inserted
- `stock_movements` row is inserted
- `bir_electronic_journal_entries` row is inserted
- `bir_electronic_journal_entry_events` row is inserted
- sale `receipt_number` equals the allocated official document number
- journal `official_document_number` equals the allocated official document number

### 9. Runtime smoke rollback path

The same runtime smoke proves a failed stock movement rolls back without drift:

- sequence does not advance
- no extra `sales`, `sale_items`, `payments`, `stock_movements`, journal entry, or journal event rows remain
- product stock is unchanged

Failure code used: `Sales.NegativeStockNotAllowed`.

### 10. Official checkout readiness is fail-closed

- `SqliteOfficialCheckoutReadinessService` checks persisted active machine profile and active sequence evidence before persistence touches rows.
- `OfficialCheckout.ReadinessBlocked` blocks official completion when readiness fails.
- `CheckoutOfficialModeReadinessBoundary` exposes readiness to checkout UI without allowing official mode selection when blocked.
- No accredited machine profile is seeded by default, so official mode remains dormant on fresh installs.

Evidence: `SqliteOfficialCheckoutReadinessService`, `OfficialCheckoutCompletionBoundary`, `CheckoutOfficialModeReadinessBoundary`, `J21Rmo242023OfficialCheckoutReadinessGateChecks`, `J21Rmo242023CheckoutOfficialModeUiReadinessChecks`.

### 11. UI/presenter wiring exists but is not certified here

- `CheckoutPresenter` can route completion through `OfficialCheckoutCompletionBoundary` when official mode is selected.
- `SalrynAppServices` composes readiness and completion boundaries.
- This seal does not claim production UI readiness, operator certification, or legal approval of checkout UI behavior.

Evidence: `CheckoutPresenter`, `SalrynAppServices`, `OfficialCheckoutCompletionBoundary`.

## Implemented But Not Fully Sealed Here

The following may exist in source, tests, or composition, but RMO-B1 does not certify their production readiness:

- reprint, void, refund, cancel, and correction journal services may exist, but B1 does not certify UI readiness or production policy for them
- X-reading and Z-reading foundations may exist, but B1 does not certify X/Z report readiness
- text-preview renderer and output packaging may exist, but B1 does not certify official receipt printing or external output certification
- `VAT_BUCKET_ENGINE_DEFERRED` remains the checkout tax-mode placeholder; B1 does not certify VAT correctness

## Explicit Non-Claims

This seal is not BIR certification.

This seal is not BIR accreditation.

This seal is not full RMO 24-2023 compliance.

This seal is not legal or accounting approval.

This seal does not certify receipt printing.

This seal does not certify an e-invoicing system.

This seal makes no LAN claim.

This seal makes no printer, PDF, Crystal, ESC/POS, or scanner claim.

This seal makes no Web/API official-output claim.

This seal makes no VAT correctness claim.

This seal makes no authorized reprint workflow claim.

This seal makes no X-reading or Z-reading report readiness claim.

This seal makes no export or transmission claim.

## Why LAN Is Not Next

LAN is not required before sealing the single-machine official checkout data spine.

LAN would add:

- multi-terminal identity
- clock and order policy
- concurrent sequence ownership beyond single-process optimistic guards
- SQLite or server coordination across terminals
- cashier and session collision risk
- audit and journal ordering risk across terminals

Therefore LAN waits until single-machine invariants are sealed and reviewed.

## Current Documentation Drift

Earlier historical compliance-matrix phase notes such as "no checkout wiring" or "no X/Z reading" were true for their phase, but are not sufficient as current-state summaries after later work.

This seal adds a current-state addendum in `RMO_24_2023_COMPLIANCE_MATRIX.md` instead of rewriting phase history line-by-line.

## Remaining Work

- full RMO matrix closure
- VAT bucket engine
- authorized reprint workflow
- void, refund, and cancel journal UI and policy seal
- X-reading and Z-reading current-state seal
- backend report package
- export, transmission, and evidence pack
- backup and retention current-state seal
- LAN and multi-terminal design later
- external legal, accounting, and BIR review

## Promotion Warning

Not stable until explicitly reviewed and promoted.

Do not promote this seal to Sandbox without explicit review. This document records internal engineering controls only.
