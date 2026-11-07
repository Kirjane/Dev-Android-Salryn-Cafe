# Salryn W4 cash policy decision register

All rules below are **PENDING KIRCH APPROVAL**. They are implemented only in an isolated draft candidate and are not live operating policy.

| ID | Provisional candidate rule | State | Decision/alternative requiring authority |
|---|---|---|---|
| W4-P1 | Cashier enters and confirms a nonnegative physical count; it is never copied from expected and is immutable after submission. | PENDING KIRCH APPROVAL | Approve who may abandon/correct a count before submission and whether witnessed counts are required. |
| W4-P2 | Expected cash is opening + cash sale net of change + cash AR collections + documented cash-in − actual cash refunds/payouts/cash-out. Utang/noncash/no-cash voids contribute zero. | PENDING KIRCH APPROVAL | Approve mixed-tender allocation and the operational evidence required for an actual refund/payout. |
| W4-P3 | Verified reconciliation requires authoritative session-linked event identity, actor, method, type, source, cents, timestamp, and replay key. Missing coverage fails closed. | PENDING KIRCH APPROVAL | Decide whether a force/unreconciled close exists, which roles may perform it, and its required reason/witness/escalation. Candidate intentionally provides no bypass. |
| W4-P4 | `variance = counted - verified expected`; every nonzero amount is `PendingReview` with no tolerance. | PENDING KIRCH APPROVAL | Approve review SLA, acknowledgment roles and whether any tolerance is lawful/business-acceptable. |
| W4-P5 | Events/reviews are append-only, replay-safe; legacy sessions remain unverified and are never reconstructed from timestamps. | PENDING KIRCH APPROVAL | Approve retention/export requirements and whether a separately witnessed legacy annotation is useful without changing verification status. |

Implementation boundary: acknowledgment records a reviewer and reason but never changes counted or expected cash. Licensing, signing, installer, production LAN, deployment, distribution and merge remain outside W4.

R2 authority clarification: the count submitter is technically denied self-acknowledgment, history is bounded and `Audit.View`-gated, and unauthorized callers are denied. `Audit.View` is still an existing broad audit permission—not an owner-approved financial-approval role. Enabling discrepancy acknowledgment for a final role matrix remains **PENDING KIRCH APPROVAL**; no force/unreconciled close exists.

