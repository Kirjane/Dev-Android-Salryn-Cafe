# M4-RC2 State Transition Matrix

| From | Action | To | Notes |
|------|--------|-----|-------|
| *(none / lazy)* | first load | Committed | From original EJ sale |
| Committed | Reprint | Committed | Event only; no sequence advance |
| Committed | Void | Voided | Full stock reverse once; settlement recorded |
| Committed | Refund (partial) | PartiallyRefunded | Line qty + tax + settlement |
| Committed | Refund (full remaining) | FullyRefunded | Exhausts remaining qty/amounts |
| PartiallyRefunded | Refund (partial) | PartiallyRefunded | Cumulative qty/amounts |
| PartiallyRefunded | Refund (full remaining) | FullyRefunded | |
| PartiallyRefunded / FullyRefunded | Void | **reject** | VoidNotAllowed |
| Voided | Refund / Void | **reject** | |
| FullyRefunded | Refund | **reject** | OverRefund / RefundNotAllowed |
| Committed+ | Cancel | **reject** | Post-commit cancel forbidden |
| Draft (no EJ) | Cancel | CancelledBeforeCommit | Checkout draft path; no official number |
| * | Adjustment (internal) | Adjusted / prior | Append-only internal doc; no legal series |

Pending external settlement overlays: `RefundPendingSettlement` when settlement incomplete.
