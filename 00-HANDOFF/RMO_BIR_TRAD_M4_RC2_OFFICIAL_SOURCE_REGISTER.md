# M4-RC2 Official Source Register

| Source | Use | Status |
|--------|-----|--------|
| RMO 24-2023 | CRM/POS traditional requirements, EJ, X/Z themes | In-repo compliance matrix / digests |
| RR 7-2024 | Invoice-first policy | Used by M1 spine |
| RMC 77-2024 + Annex A1–B6 | Invoice samples / VAT presentation | Used by M3 render |
| Sales returns / credit-debit / adjustment document naming for CRM/POS | **Unresolved for product legal labels** | Fail-closed internal adjustment document only |

## Unresolved questions (explicit)

1. Exact BIR-mandated document title for a partial return after an official invoice (CRM/POS class).  
2. Whether a new official series number is always required vs event-only for same-day void.  
3. Card/acquirer refund settlement rules for MSME POS (no processor integration in Salryn).

## Product policy until sources resolve

- Operational void/refund/return execute with immutable evidence.  
- Adjustment output title: `INTERNAL ADJUSTMENT DOCUMENT` + original invoice reference.  
- No invented PTU/CoA/series claims.  
- Production activation OFF.
