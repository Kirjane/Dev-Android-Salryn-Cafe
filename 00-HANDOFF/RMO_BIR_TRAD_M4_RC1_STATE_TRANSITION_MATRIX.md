# M4-RC1 State Transition Matrix

| From | Action | To | Sale effect | Stock | Payment | Sequence | EJ event |
|------|--------|-----|-------------|-------|---------|----------|----------|
| Committed | Reprint | Committed | none | none | none | none | REPRINT |
| Committed | Void | Voided | status Voided | VoidSale reverse | obligation recorded in metadata | none | VOID |
| Committed | Refund(partial) | PartiallyRefunded | stays Completed | reverse qty | refund amount in metadata | none | REFUND |
| PartiallyRefunded | Refund(to full) | FullyRefunded | stays Completed* | reverse remaining | accum refund | none | REFUND |
| Voided | Void/Refund | rejected | — | — | — | — | — |
| FullyRefunded | Refund | rejected | — | — | — | — | — |
| any | missing identity | rejected | — | — | — | — | — |

\* Full refund does not invent a new SaleStatus; lifecycle state tracks FullyRefunded separately.
