# M5-RC2 X/Z/EJ/eSales Completion Study

**Base:** M4-RC2 `05af9e22fbfabbdf77b70379323ec68960293782` (v17)  
**Archival M5-RC1:** `0e3b21d341c25fe4b954cb0251270a5d220bf332` (diverged; port ideas only)

## Decision

**GO WITH LIFECYCLE-AWARE M5 COMPLETION.**

Reuse host reporting endpoint shapes and draft export concepts from M5-RC1. Rewrite totals against M4-RC2 lifecycle state, refund lines, tax reversals and settlements. Keep BIR eSales schema unconfirmed with an adapter boundary. No v18 required if Z lifecycle control totals freeze into existing `metadata_json` / enrichment payload.

## Study answers

| # | Answer |
|---|--------|
| 1 | Valid on v17: endpoint map pattern, draft CSV framing, status honesty flags. Invalid: gross-only EJ reads, zero-on-malformed, no refund netting. |
| 2 | Gross-only original snapshot totals are wrong after void/partial/full refund without lifecycle netting. |
| 3 | EJ export must include ORIGINAL, REPRINT, VOID, REFUND (+ tax reverse lines), ADJUSTMENT, settlement facts, Z references. |
| 4 | `bir_z_readings` has unique range index, evidence hash, enrichment metadata — sufficient for freeze without v18. |
| 5 | **v18 not required** for M5-RC2 if control totals live in enrichment/metadata. |
| 6 | **No** authoritative BIR monthly eSales transmission schema is confirmed in-repo. |
| 7 | `ITraditionalESalesSchemaProvider` + `UnconfirmedTraditionalESalesSchemaProvider` seals labels/transmission; future confirmed adapter plugs in without rewriting projection. |

## Canonical projection

One service projects period documents from EJ + lifecycle tables. X, Z, EJ export, eSales draft all consume it.
