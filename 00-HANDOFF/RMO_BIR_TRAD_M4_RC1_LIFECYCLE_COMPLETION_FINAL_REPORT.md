# M4-RC1 Lifecycle Completion — Final Report

**Branch:** `KIRCH-RMO-BIR-TRAD-M4-RC1-LIFECYCLE-COMPLETION`  
**Base (predecessor scaffold M4 tip):** `d47ec3a85de637f1087b15f55fe88dac1cad5422`  
**Date:** 2026-07-10  
**Program:** Traditional CRM/POS RMO recovery (not BIR accreditation)

## 1. Predecessor / base / final SHA

| Item | Value |
|------|--------|
| Predecessor (archival M4 scaffold) | `d47ec3a85de637f1087b15f55fe88dac1cad5422` |
| Base for this branch | same |
| Final tip | *(filled after commit)* |
| Sandbox (untouched) | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| Installer reference (untouched) | `6b9c46676390b8a86a989eca600cb02e406ce865` |

## 2. Study decision

**GO WITH NARROW FIX + MIGRATION v16.**  
Prior M4 only appended EJ lifecycle markers without sale/stock/idempotency/runtime proof. Sources: recovery handoff, M4-RC1 study, state-transition matrix, source-applicability matrix.

## 3. What changed (engineering)

| Area | Change |
|------|--------|
| Schema v16 | `official_document_lifecycle_state`, `_idempotency`, `_refund_lines` |
| Catalog / factory / QA1 | SupportedMigrationVersion = 16; migration registered |
| Completion service | `SqliteOfficialDocumentLifecycleCompletionService` — one SQLite transaction for operational effects + EJ event + state + idempotency |
| API | `/api/official/document-lifecycle/*` uses completion service; **actor from session only**; idempotency key required |
| Reprint | Renders HTML/thermal with visible `REPRINT` before return; no sequence advance; original snapshot immutable |
| Void | Marks sale voided + reverses stock + VOID EJ event |
| Refund | Partial/full with remaining balance; over-refund rejected; stock return by ratio |
| Runtime gate | `rmo-bir-trad-document-lifecycle-runtime` |

## 4. Runtime architecture / behavior

1. Resolve original by journal id / document number / sale id (strict; ambiguous fails).  
2. Load or create lifecycle state from original total in snapshot.  
3. Guard transitions (void only from Committed; no refund after void/full; no reprint from voided/cancelled).  
4. Apply operational effects (void sale/stock or partial stock return).  
5. Append append-only EJ event (never UPDATE original EJ payload/hash).  
6. Upsert lifecycle state + store idempotency result.  
7. Commit once.

Production official posting activation remains **OFF**. Checkout is not bound to official posting for production.

## 5–8. Matrices / proofs (runtime gate)

| Proof | Result |
|-------|--------|
| Missing identity | Fail `DocumentLifecycle.IdentityRequired` |
| Reprint HTML contains REPRINT | Pass |
| Idempotent reprint same event id | Pass |
| Partial refund + stock restore | Pass |
| Over-refund | Fail `DocumentLifecycle.OverRefund` |
| Full refund then further refund | Fail |
| Sequence not advanced by lifecycle | Pass |
| Void sale status + stock + VOID event | Pass |
| Double void | Fail |
| Migration max | 16 |

VAT/non-VAT policy unchanged (M1). Concurrency: state_version upsert in same tx; idempotency key conflict on hash mismatch. Security: API actor from session cookie, not body.

## 9. Gate commands / results

```text
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-document-lifecycle-runtime
→ [TRAD-M4-RC1] Lifecycle runtime checks passed. SALRYN Core smoke checks passed.

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-document-lifecycle
→ (source/scaffold gate)

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-snapshot-capture-rc2
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-atomic-posting
```

Full CoreSmoke is recommended after push; report stamp if full run is separate.

## 10. Failures fixed during RC1

| Failure | Root cause | Fix |
|---------|------------|-----|
| CS0103 InvoiceOutput | Wrong type name in test | Use `InvoiceOutputFormat` + using |
| CS0103 OfficialSafetyConstants | Missing using in API | `using Salryn.Core.Official` |
| FK on seed roles | Custom Cashier role id vs unique name from DefaultSeedData | Use `DefaultSeedData.CashierRoleId` |
| FK product columns | Wrong product column names | Match real schema (`selling_price_cents`, `unit_id`, …) |
| FK second seed | `INSERT OR REPLACE` machine profile after EJ exists | `INSERT OR IGNORE` machine/sequence only once |

## 11. Manual evidence

**Not executed.** QA1 Official Setup UI, print previews, physical printer remain Human Action Queue.

## 12. Honest incomplete / external

- Adjustment legal document series: not productized  
- Cancel operational path: not completion-wired in RC1 (unknown/cancel fails at completion switch)  
- Application package / BIR submission: not in M4  
- Production activation: OFF  
- Not accredited / not certified / not submitted  

## 13. Commit / push

Filled after git commit and push. Local HEAD must equal origin HEAD; Sandbox and installer refs unchanged.

## 14. Next recovery branch

`KIRCH-RMO-BIR-TRAD-M5-RC1-XZ-EJ-ESALES-RUNTIME` from this tip.  
Hard-stop if authoritative BIR eSales schema still missing — draft only, no invented schema.

---

**Status:** M4-RC1 engineering runtime complete (automated). Manual acceptance pending. Not application-ready. Not BIR accredited.
