# M4-RC2 Payment / Refund Settlement Matrix

| Tender | Immediate complete? | Cash session effect | Status |
|--------|---------------------|---------------------|--------|
| Cash | Yes | expected_cash_cents -= refund | Completed |
| Card/external (no processor) | No | none | PendingExternal until ack |
| Mixed | Per component | Cash portion only | Partial pending allowed |

## Void

- Post-settlement void: records settlement disposition; does not delete original payment rows.  
- Stock reversed exactly once.  

## Idempotency

Same key + same request hash → same settlement IDs, no second cash debit, no second stock movement.
