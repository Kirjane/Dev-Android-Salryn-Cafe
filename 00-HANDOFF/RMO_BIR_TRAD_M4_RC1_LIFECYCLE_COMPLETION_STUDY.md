# M4-RC1 Lifecycle Completion Study

**Base:** M4 scaffold `d47ec3a85de637f1087b15f55fe88dac1cad5422`  
**Decision:** **GO WITH NARROW FIX + MIGRATION v16**

## Why prior M4 is incomplete
Marker-only EJ append without sale/stock/payment/idempotency/runtime proof.

## Design
1. Migration v16: lifecycle state + idempotency tables.
2. Strict original identity required (journal ID and/or document number; never empty scan).
3. Actor from session (API); runtime service requires actor GUID.
4. One SQLite transaction for: operational sale void/refund effects + stock + EJ event + lifecycle state + idempotency.
5. Reprint: render REPRINT into HTML/thermal before hash; fail if render fails (no orphan event without artifact intent policy: event+output same transaction where possible).
6. Adjustment series: internal reference id only; legal adjustment document type not invented.

## Runtime gate
`rmo-bir-trad-document-lifecycle-runtime`
