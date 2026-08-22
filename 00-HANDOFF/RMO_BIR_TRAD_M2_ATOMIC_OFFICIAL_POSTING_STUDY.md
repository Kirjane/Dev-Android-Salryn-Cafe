# RMO-BIR TRAD-M2 — Atomic Official Posting Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M2-ATOMIC-OFFICIAL-POSTING`  
**Base:** TRAD-M1 `121b7838f09d04db0f7224fcab3d9b382d04f1ad`  
**Decision:** **GO — HARDEN + WIRE TAX SPINE + ACTIVATION DEFAULT-OFF HOST FACADE**

---

## 1. Repo truth (what already exists)

`SqliteOfficialCheckoutPersistenceService.CompleteOfficialSaleAsync` already performs a **single shared SQLite transaction**:

1. Official sequence allocation participant  
2. Snapshot draft (tax buckets when classifications + taxpayer mode supplied)  
3. Completed sale write participant (sale/items/payments/stock)  
4. Electronic journal append participant  

Runtime smoke (`J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks`) already proves:

- commit advances sequence once and writes sale + EJ  
- failure mid-path rolls back (no orphan sequence, no partial sale/EJ)

This is the Traditional CRM/POS atomic core. TRAD-M2 does **not** invent a second parallel posting engine.

---

## 2. Gaps vs accreditation-readiness program

| Gap | TRAD-M2 action |
|-----|----------------|
| Taxpayer mode optional / not fail-closed | Require known mode; validate line classifications via `SalrynTaxpayerTaxPolicy` |
| Tax policy freeze not explicit | Freeze `OfficialTaxPolicySnapshot` into tax mode notes / evidence path |
| Host-facing activation default-off | `HostAtomicOfficialPostingService` blocks unless explicit internal activation + readiness |
| Invoice-first document kind | Prefer `OFFICIAL_INVOICE` via `SalrynInvoiceDocumentPolicy` |
| POS checkout not bound | **Out of scope for M2** — host atomic path only; checkout bind later under activation |
| Real invoice print/PDF | TRAD-M3 |
| Void/refund docs | TRAD-M4 |

---

## 3. Activation policy (fail closed)

Production-facing atomic posting remains **default off**:

- `ProductionOfficialPostingActivation.IsEnabled` = **false** by default  
- Host facade refuses with stable code `AtomicOfficialPosting.ActivationDisabled`  
- Internal CoreSmoke may exercise the existing persistence service **directly** (as J21 already does) for atomicity proofs without flipping production activation  
- Even when activation is later enabled, readiness + NOT_ACCREDITED / official_output_enabled rules still apply  

Honest boundary: enabling activation in code is **not** accreditation and **not** authority to issue official invoices in the field without CoA/PTU and Kirch authorization.

---

## 4. Invariants

1. Allocate + sale + EJ in one transaction (existing)  
2. Rollback leaves zero orphan numbers (existing + reassert with tax mode required)  
3. Invalid tax mode/classification fails before allocate when possible; if inside tx, full rollback  
4. Idempotency: same sale id must not double-allocate (sale PK uniqueness provides hard fail)  
5. No automatic checkout wire-up in M2  

---

## 5. GO / STOP

**GO** — harden and facade the existing atomic path; do not rewrite.

STOP only if shared-transaction rollback proof regresses or tax-mode requirement breaks readiness without a narrow fix.
