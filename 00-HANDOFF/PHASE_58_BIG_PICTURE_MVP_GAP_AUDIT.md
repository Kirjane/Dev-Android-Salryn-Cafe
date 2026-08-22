# Phase 58 - Big Picture MVP Gap Audit

## Status

Audit-only phase. No feature source code changed.

## Purpose

Pause checkout-first development and review SALRYN as a full sellable POS MVP.

## Current Reality

Checkout is now the strongest module.

Completed checkout path:

- cashier shift gate
- checkout session context
- product search
- draft cart
- cash payment
- completion dry run
- submit guard
- guarded live sale completion
- confirmation dialog
- result dialog
- post-completion lock

Current shape:

```text
Backend/domain foundation: broad
Checkout UI: deep
Other business module UI: still thin
```

## Bigger Picture Decision

Do not continue to receipt print preparation yet.

Receipt printing is useful, but product management, inventory operations, and sales history are more important for a sellable V1.

## MVP Priority Order

```text
Phase 59 - Product Management UI Foundation
Phase 60 - Product Create/Edit Workflow
Phase 61 - Inventory Operations UI Foundation
Phase 62 - Stock In / Adjustment Workflow
Phase 63 - Sales History / Receipt Lookup
Phase 64 - Receipt Summary / Print Preparation
Phase 65 - Daily Sales / Low Stock Reports
Phase 66 - Store Settings / Backup
```

## Module Gap Audit

### Product Management

Priority: critical.

Needed:

- product list
- add product
- edit product
- barcode
- selling price
- cost price
- category
- brand
- unit
- active/inactive

Reason:

A POS is not owner-usable if products must be edited outside the app.

### Inventory Operations

Priority: critical.

Needed:

- stock in
- stock adjustment
- damaged/expired stock
- low stock list
- movement ledger
- stock-on-hand audit view

Reason:

Inventory traceability is one of SALRYN's main selling points.

### Sales History / Receipt Lookup

Priority: high.

Needed:

- completed sales list
- search by receipt number
- view sale details
- view sale items
- view payments
- receipt summary from saved sale

Reason:

Receipt printing should come after saved-sale lookup, not only from the live checkout screen.

### Receipt / Print

Priority: medium.

Delay this until after product management, inventory, and sales history basics.

### Reports

Priority: medium.

Needed:

- daily sales report
- low stock report
- product sales report
- inventory movement report

### Store Settings / Backup

Priority: high for deployment.

Needed:

- store name
- receipt header/footer
- local backup
- restore safety
- database path visibility

## Final Decision

Next phase should be:

```text
Phase 59 - Product Management UI Foundation
```

## Comment Style

Implementation comments must use:

```csharp
// Kirch: ...
```
