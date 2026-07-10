# RMO-BIR-TRAD-M4-RC2 Final Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M4-RC2-LIFECYCLE-ACCOUNTING-AND-ADJUSTMENT-COMPLETION`  
**Base (M4-RC1):** `115ad23f62ab64e0d8adb62e9c6cda3e131bfaa5`

## 1. Base / branch / final SHA

| Item | Value |
|------|--------|
| Base | `115ad23f62ab64e0d8adb62e9c6cda3e131bfaa5` |
| Final tip | *(after push)* |
| Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` untouched |
| Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` untouched |
| M5+ recovery branches | Not merged/created |

## 2. Study decision

**GO WITH SOURCE-LOCKED ADJUSTMENT OUTPUT**

Legal CRM/POS adjustment document naming/series remains unresolved in primary sources; operational lifecycle completes with `INTERNAL_ADJUSTMENT_DOCUMENT` (no legal series allocation).

## 3. Changed files (summary)

- Study matrix docs under `00-HANDOFF/RMO_BIR_TRAD_M4_RC2_*`
- Core lifecycle domain: contracts, state machine, refund line calculator
- Snapshot V3 line-tax freeze (draft builder + evidence builder)
- Migration **v17** accounting tables
- `SqliteLifecycleAccountingService` (line refund, tax reverse, settlement, optimistic concurrency, failure injection)
- Host API + Sales History operator UI
- Runtime gate `rmo-bir-trad-document-lifecycle-accounting-runtime`
- Version-assert updates for v17 / future probe 18

## 4. Snapshot V1/V2/V3

| Schema | When |
|--------|------|
| V1 | No buyer/statutory, no line-tax freeze |
| V2 | Buyer and/or statutory, no line-tax freeze |
| V3 | Line-tax evidence frozen (tax engine path) — buyer may also be present |

V1/V2 historical hashes unchanged for rows without V3 members.

## 5–12. Lifecycle behavior

- **State machine:** Committed → Partially/FullyRefunded / Voided / Adjusted; post-commit Cancel forbidden  
- **Refund:** explicit sale item + qty + disposition; pure calculator; no money-ratio stock  
- **Tax:** Vatable/VAT/exempt/zero/SSPT/non-VAT exempt reverse components persisted  
- **Stock:** Restock / Damaged (no sellable increase) / Service  
- **Payment:** cash settlement updates `expected_cash_cents`; card external can stay PendingExternal  
- **Adjustment:** internal numbered document + hash; `legal_series_allocated=0`  
- **Cancel:** draft-only via `CancelDraftSaleAsync`  
- **Reprint:** REPRINT in HTML/thermal; idempotent rebuild  
- **Concurrency:** `WHERE state_version = $expected`  

## 13–16. Gates

```text
--only rmo-bir-trad-document-lifecycle-accounting-runtime  PASS
--only rmo-bir-trad-document-lifecycle-runtime             PASS
--only rmo-bir-trad-document-lifecycle                     PASS
--only rmo-bir-trad-snapshot-capture-rc2                   PASS
--only rmo-bir-qa1-real-db                                 PASS (v17)
Full CoreSmoke                                             PASS
```

## 17. UI / manual

Host Sales History panel: reprint / void / line refund with disposition + reason + internal watermark notes.  
Manual operator acceptance **pending Kirch**.

## 18–19. Incomplete / external

- Legal adjustment series / BIR official title for returns: **blocked on source**  
- Card processor integration: PendingExternal only  
- Full permission matrix per role (UI-level): partial  
- Physical printer / print-to-PDF: not claimed  
- No M5 started; eSales schema still external  

## 20. Commit / push

Filled after push. Local HEAD = origin RC2; Sandbox/installer unchanged; no M5 branch.

## 21. Next action

**Maintainer review of M4-RC2 before any M5 recovery handoff.**  
M5 must not proceed without authoritative eSales schema resolution.
