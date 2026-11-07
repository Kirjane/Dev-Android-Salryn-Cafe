# Salryn W4 owner decision packet

This packet supports Kirch's decision; it does not approve or activate policy.

| Decision | Option A | Option B | Operator consequence | Current candidate |
|---|---|---|---|---|
| Physical count submission | Cashier alone confirms | Cashier plus witness | A is faster; B adds control and staffing cost | Cashier confirmation only; **PENDING** |
| Verified expected cash | Exact authoritative source/event equality | Allow manual reconstruction | Exact equality blocks when evidence is incomplete; reconstruction risks fictional attribution | Exact/fail-closed; **PENDING** |
| Missing coverage close | Stop close and escalate | Audited force/unreconciled close by a defined role | Stop is safest but may delay end-of-day; force-close needs strict reason/witness/alert | Stop; no force endpoint; **PENDING** |
| Variance review authority | Dedicated manager/owner permission and distinct reviewer | Generic `Audit.View` | Dedicated permission is clearer; generic audit access may over-authorize | Self-review denied, `Audit.View` route provisional; **PENDING** |
| Variance threshold | Every nonzero difference reviewed | Approved tolerance | No tolerance is strict; tolerance reduces reviews but requires explicit business/legal basis | Every nonzero; **PENDING** |
| Actual refund proof | Separate cash-refund action with source/reference and reason | Infer from void/reversal | Separate action reflects physical cash; inference can fabricate outflow | Separate event; **PENDING** |
| Legacy sessions | Label unverified forever | Add non-verifying annotation | Annotation may aid context but must not turn history verified | Legacy/unverified; **PENDING** |

## Targeted physical acceptance checklist

Use disposable training data and an authorized QA Host only.

Cashier:

1. Open with zero and nonzero float; verify displayed opening amount.
2. Complete cash, over-tender/change, full Utang and split cash/Utang sales; count the physical training cash independently.
3. Record an approved cash-in/out and an actual cash refund; confirm no refund appears from void alone.
4. Enter a physical count without seeing a suggested/prefilled expected value; correct it before submission.
5. Retry after simulated network uncertainty with the same request; verify one close and immutable count.
6. Confirm late movement after close is denied.

Manager/owner:

1. Sign in separately; verify cashier cannot review their own discrepancy.
2. Open bounded closed history and compare exact events to synthetic receipts/collection records.
3. Acknowledge shortage and overage with reasons; verify original count/expected/variance remain unchanged.
4. Verify an unauthorized role receives 403 for history/review.
5. Exercise a missing-source fixture and confirm close stops with no bypass.

Record observer, role, machine, date/time, exact build SHA, disposable scenario IDs and observations. Do not use real client cash or approve production use through this checklist alone.
