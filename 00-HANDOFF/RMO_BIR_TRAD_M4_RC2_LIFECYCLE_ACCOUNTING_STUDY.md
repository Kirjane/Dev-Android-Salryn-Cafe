# M4-RC2 Lifecycle Accounting Study

**Base:** `115ad23f62ab64e0d8adb62e9c6cda3e131bfaa5`  
**Decision:** GO WITH SOURCE-LOCKED ADJUSTMENT OUTPUT

## Inventory of existing systems

| Area | Location |
|------|----------|
| Sale / sale_items / qty_x1000 | InitialSchema + SqliteSaleWriteRepository |
| Stock movements | stock_movements; Sale/VoidSale/Damage/StockIn |
| Payments | payments (Cash method only currently) |
| Cashier session cash | cashier_sessions.expected_cash_cents |
| Sale void fields | sales.voided_* |
| Tax classifications | OfficialDocumentLineTaxClassification + TaxBucketEngine |
| Snapshots | V1/V2 OfficialDocumentSnapshotEvidenceBuilder |
| Sequence | bir_official_document_sequences |
| EJ | bir_electronic_journal_entries + events |
| Lifecycle v16 | official_document_lifecycle_* |
| Invoice render | InvoiceHtmlRenderer / Thermal |
| Permissions | DefaultSeedData role permissions |
| Checkout draft cancel | existing sale Draft status path (no official number) |
| Migration max | 16 → RC2 adds 17 |

## Architecture (separated)

1. Core contracts + state machine + pure refund/tax calculators  
2. Snapshot V3 line-tax freeze on new posts  
3. Infrastructure accounting service (tx coordinator)  
4. Host API (session actor)  
5. Host web lifecycle panel  
6. Runtime gates with failure injection  

## Legal adjustment naming

Unresolved in primary sources available in-repo for CRM/POS productization.  
Internal kind: `INTERNAL_ADJUSTMENT_DOCUMENT`. Legal series allocation: **not enabled**.
