# Salryn W4 cash/session source model

Status: draft candidate stacked on PR #18. Base Salryn POS with licensed U1 AR only; no Café or production activation.

## Authoritative write map

| Source action | Persistent source | Drawer event | Session link | W4 treatment |
|---|---|---|---|---|
| Open shift | `cashier_sessions` | none | session is authority | New sessions set `cash_coverage_started_at`; opening float is the initial expected amount. |
| Completed cash sale | `sales`, `payments`, stock/receipt writes | `CashSaleNet`, inflow = cash tender minus change | `sales.cashier_session_id` | Event is inserted in the same checkout transaction and deduplicated by sale identity. |
| Utang sale | sale plus AR ledger | no drawer event unless the same sale also has actual cash tender | sale session | Full Utang contributes zero drawer cash. Mixed tender contributes only net cash. |
| AR cash collection | `accounts_receivable_collections` | `ArCashCollection` inflow | new nullable `cashier_session_id` | Event and collection share a transaction. A cash collection with no open authoritative shift remains unlinked and blocks verified close for that operator/time window. |
| AR noncash collection | collection row | none | collection may retain session for traceability | No physical drawer movement. |
| Collection void/reversal | void metadata and compensating AR semantics | none automatically | existing collection identity | Bookkeeping reversal is not proof that cash physically left the drawer. An actual repayment must be recorded as `CashRefund`. |
| Sale void without cash returned | sale void state | none | sale identity retained | No phantom outflow. An actual cash return requires an explicit `CashRefund`. |
| Cash-in / cash-out / actual refund | `cash_drawer_events` | `CashIn` (+), `CashOut` (-), `CashRefund` (-) | signed-in operator's open session | Positive cents, reason and replay key required. |
| Close shift | reconciliation plus session close columns | none | cashier's own open session | Explicit nonnegative physical count and confirmation required. Expected is opening plus source-verified signed events; count is never prefilled. Migration 26 persists/backfills the canonical close note so same-ID replay compares operator, count, confirmation and note. |
| Discrepancy review | `cash_drawer_reviews` | none | closed reconciliation | `Audit.View` authority appends acknowledgment and reason; original count is immutable. |
| Restart / retry | existing SQLite rows | no synthesized event | stored session ID | Unique source/event and request keys make same-data replay idempotent; changed-data reuse fails. |
| Legacy shift | pre-v25 rows | none synthesized | no coverage start | Explicit `LegacyUnverified`; W4 safe close fails closed rather than attributing history by timestamp. |

## Expected amount

`opening float + SUM(direction * amount_cents)` over authoritative events for that session. Sales use cash tender minus change. Utang originated, noncash tenders/collections, and voids without actual cash movement are excluded.

Before `Verified`, close performs a transactionally consistent 1:1 comparison. Each session sale's computed cash tender less change must match exactly one `CashSaleNet` event with the same sale, session, cashier, direction, method and cents. Each session-linked cash AR collection must likewise match exactly one `ArCashCollection`. Missing, orphaned, spoofed or mismatched events block close; aggregate cancellation cannot make bad sources valid. Voids/reversals do not infer physical refunds, while an actual cash return is a separately request-keyed `CashRefund`.

## Unsupported or policy-dependent semantics

- There is no inferred retroactive attribution of old sales or collections.
- Automatic refund inference is deliberately unsupported because a void/reversal does not prove physical cash movement.
- A forced unverified close workflow is not enabled. Its authority and mandatory audit policy require Kirch's decision.
- Remote POS cannot close a host drawer; it returns `PhysicalCountRequired`. Production LAN remains unchanged/disabled.

