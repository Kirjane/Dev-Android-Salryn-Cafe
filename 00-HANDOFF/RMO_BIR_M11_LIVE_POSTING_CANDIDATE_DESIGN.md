# RMO-BIR-M11 Live Posting Candidate Design

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M11-GUARDED-LIVE-POSTING-CANDIDATE-HARDENING`

**Status:** Design + guarded candidate skeleton. **Not production-enabled.**  
**Disclaimer:** Internal design. No compliance claim. No official issuance.

---

## 1. Traditional CRM/POS candidate flow

1. Read operational sale (read-only) or synthetic dry-run shape.  
2. Validate official setup + active series.  
3. Preview official number (no allocate).  
4. Plan immutable official sale record linked to operational `sale_id`.  
5. Plan EJ append + audit link in same future transaction.  
6. Plan output package (shape only).  
7. Plan traditional monthly eSales export + examinable journal/Z relationship.  
8. **BlockBeforeMutation** — do not execute.

## 2. EIS e-invoice/e-receipt candidate flow

Same as traditional through plan stages, then:

6. Plan EIS transmission: JSON + EIS Unique ID (schema **unresolved**).  
7. Plan validation seal form (QR/barcode/URL) for e-docs only.  
8. Require future EIS CERT + PTT productization.  
9. **BlockBeforeMutation** — do not transmit.

## 3. Atomic transaction design (future live)

Single host SQLite transaction:

1. Lock/allocate next official number.  
2. Insert immutable official sale header/lines.  
3. Append EJ event referencing official sale + number.  
4. Write audit link.  
5. Commit.

Only after commit: attempt output package generation (retryable, same number).

## 4. Failure / crash handling

| Failure point | Rule |
|---------------|------|
| Before allocate | No number consumed |
| Mid-transaction | Rollback; no orphan number |
| After commit, output fails | Official sale exists; retry output; **no** re-allocate |
| After commit, transmit fails (EIS) | Outbox/retry later; do not invent schema in M11 |

## 5. Official number no-reuse invariant

Committed allocation never reused. Preview does not allocate. Candidate never allocates.

## 6. EJ append invariant

Live append only in same transaction as official sale post. Candidate sets `ejWillAppendLive = false`.

## 7. Reprint invariant

Same official number; REPRINT marker + reprint timestamp; no sequence advance.

## 8. Void/refund invariant

Reversal/adjustment document series distinct from SI/OR; reason + authorization; original immutable; EJ linked.

## 9. Z-reading / eSales relationship

- **Traditional:** Z-reading ranges/totals from official sales; monthly eSales report per machine (process from RMO 12-2012); examiner journal extract.  
- **EIS:** Z may still exist for POS class; EIS JSON is separate transmission path when class applies.

## 10. Why live posting remains blocked

- Checkout bind not implemented  
- Official output engine missing  
- EIS JSON API schema unresolved  
- eSales live push not implemented  
- Product operationalization incomplete  
- No production activation flag  
- Accreditation / PTU / EIS CERT lifecycle not productized  

## 11. What must be true before checkout binding

1. Dual-track product decision for store class (traditional vs EIS).  
2. Atomic allocate + official sale + EJ implemented and tested.  
3. Output engine path decided and gated.  
4. If EIS: official JSON schema + CERT/PTT path.  
5. If traditional: monthly eSales export path + journal/Z integrity.  
6. Source gates + real DB QA (backup/restore/migration).  
7. Explicit human activation design (still non-claim of compliance).  
8. Checkout wiring behind feature flag default off until final acceptance.

---

## M11 code surface

- Contracts: dual-track stages, plans, forced flags  
- Service: always-blocked planner + Sqlite read-only probes  
- API: `POST /api/official/live-posting-candidate/plan`, `GET .../status`  
- UI: blocked status card only  
